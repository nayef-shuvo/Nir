using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class StringBinderTests
{
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines)
    {
        var source = new SourceText("test.nir", "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");
        var bag = new DiagnosticBag();
        var program = Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }

    // The argument of `print(...)` in the first statement.
    private static BoundExpression PrintedArgument(BoundProgram program) =>
        ((BoundCallExpression)((BoundExpressionStatement)program.Functions[0].Body.Statements[0]).Expression).Arguments[0];

    // ---- string operators ----

    [Theory]
    [InlineData("print(\"a\" + \"b\")")]
    [InlineData("print(\"a\" + \"b\" + \"c\")")]
    public void Strings_can_be_joined_with_plus(string statement)
    {
        var (program, _, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        Assert.Equal(TypeSymbol.StringType, PrintedArgument(program!).Type);
    }

    [Theory]
    [InlineData("print(\"a\" == \"b\")")]
    [InlineData("print(\"a\" != \"b\")")]
    public void Strings_can_be_compared_for_equality(string statement)
    {
        var (program, _, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        Assert.Equal(TypeSymbol.BoolType, PrintedArgument(program!).Type);
    }

    [Theory]
    [InlineData("print(\"n\" + 5)")]
    [InlineData("print(5 + \"n\")")]
    [InlineData("print(\"a\" - \"b\")")]
    [InlineData("print(\"a\" * \"b\")")]
    [InlineData("print(\"a\" < \"b\")")]
    [InlineData("print(\"a\" >= \"b\")")]
    [InlineData("print(\"a\" == 1)")]
    [InlineData("print(true + \"x\")")]
    public void Other_string_operations_report_NIR3003(string statement)
    {
        var (_, _, diagnostics) = BindMain(statement);

        Assert.Equal(["NIR3003 2:11"], diagnostics);
    }

    [Fact]
    public void Plus_message_names_both_types()
    {
        var (_, bag, _) = BindMain("print(\"n = \" + 5)");

        Assert.Equal("operator '+' cannot be applied to 'string' and 'int'", Assert.Single(bag).Message);
    }

    [Fact]
    public void An_error_operand_of_string_plus_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("print(\"a\" + nope())");

        Assert.Equal(["NIR2001 2:17"], diagnostics);
    }

    [Fact]
    public void String_equality_works_as_a_condition()
    {
        var (_, _, diagnostics) = BindMain("let s = \"x\"", "if s == \"x\" && s != \"y\" {", "}");

        Assert.Empty(diagnostics);
    }

    // ---- interpolation ----

    [Fact]
    public void Every_part_of_an_interpolated_string_is_a_string()
    {
        var (program, _, diagnostics) = BindMain("print($\"i={1} d={2.5} b={true} s={\"s\"}\")");

        Assert.Empty(diagnostics);
        var interpolated = Assert.IsType<BoundInterpolatedString>(PrintedArgument(program!));
        Assert.All(interpolated.Parts, part => Assert.Equal(TypeSymbol.StringType, part.Type));
        Assert.Equal(8, interpolated.Parts.Length);
    }

    [Fact]
    public void Holes_that_are_not_strings_are_wrapped_in_ToString()
    {
        var (program, _, _) = BindMain("print($\"{1}{2.5}{true}{\"s\"}\")");

        var parts = ((BoundInterpolatedString)PrintedArgument(program!)).Parts;
        Assert.All(parts.Take(3), part =>
        {
            var call = Assert.IsType<BoundMemberCallExpression>(part);
            Assert.Equal("ToString", call.Target.Method.Name);
        });
        Assert.IsType<BoundStringLiteral>(parts[3]);
    }

    [Fact]
    public void Empty_text_parts_are_dropped_and_an_empty_string_has_no_parts()
    {
        var (program, _, _) = BindMain("print($\"\")");

        Assert.Empty(((BoundInterpolatedString)PrintedArgument(program!)).Parts);
    }

    [Fact]
    public void Holes_can_hold_expressions_calls_and_method_calls()
    {
        var (_, _, diagnostics) = BindMain("let n = 7", "print($\"{n * 2} {n > 3} {\"abc\".ToUpper()} {$\"{n}\"}\")");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_hole_without_a_value_reports_NIR3012_at_the_expression()
    {
        var (_, bag, diagnostics) = BindMain("print($\"{print(1)}\")");

        Assert.Equal(["NIR3012 2:14"], diagnostics);
        Assert.Equal("cannot use a value of type '()' in an interpolated string", bag.Single().Message);
    }

    [Fact]
    public void An_error_in_a_hole_is_reported_once_and_the_string_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("print($\"a {missing} b\")");

        Assert.Equal(["NIR2001 2:16"], diagnostics);
    }

    [Fact]
    public void Two_bad_holes_are_both_reported()
    {
        var (_, _, diagnostics) = BindMain("print($\"{one}{two}\")");

        Assert.Equal(["NIR2001 2:14", "NIR2001 2:19"], diagnostics);
    }

    [Fact]
    public void An_interpolated_string_is_a_string_for_variables_and_arguments()
    {
        var (_, _, diagnostics) = BindMain("let s: string = $\"x{1}\"", "print(s)");

        Assert.Empty(diagnostics);
    }

    // ---- conversions ----

    [Theory]
    [InlineData("print(5 as double)", "double")]
    [InlineData("print(2.5 as int)", "int")]
    [InlineData("print(-3 as double)", "double")]
    [InlineData("print((1 + 2) as double)", "double")]
    [InlineData("print(5 as int)", "int")]
    [InlineData("print(2.5 as double)", "double")]
    public void Numbers_convert_between_int_and_double(string statement, string expectedType)
    {
        var (program, _, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        Assert.Equal(expectedType, PrintedArgument(program!).Type.Name);
    }

    [Fact]
    public void Converting_to_the_same_type_adds_no_conversion()
    {
        var (program, _, _) = BindMain("print(5 as int)");

        Assert.IsType<BoundIntLiteral>(PrintedArgument(program!));
    }

    [Fact]
    public void A_real_conversion_is_a_BoundConversion_to_the_target()
    {
        var (program, _, _) = BindMain("print(5 as double)");

        var conversion = Assert.IsType<BoundConversion>(PrintedArgument(program!));
        Assert.Equal(TypeSymbol.DoubleType, conversion.Target);
        Assert.Equal(TypeSymbol.IntType, conversion.Operand.Type);
    }

    [Theory]
    [InlineData("print(\"x\" as int)", "cannot convert 'string' to 'int'")]
    [InlineData("print(true as double)", "cannot convert 'bool' to 'double'")]
    [InlineData("print(5 as string)", "cannot convert 'int' to 'string'")]
    [InlineData("print(2.5 as bool)", "cannot convert 'double' to 'bool'")]
    public void Conversions_other_than_int_and_double_report_NIR3001_at_the_cast(string statement, string message)
    {
        var (_, bag, diagnostics) = BindMain(statement);

        Assert.Equal(["NIR3001 2:11"], diagnostics);
        Assert.Equal(message, bag.Single().Message);
    }

    [Fact]
    public void An_unknown_target_type_reports_NIR2006_at_the_type()
    {
        var (_, _, diagnostics) = BindMain("print(5 as nope)");

        Assert.Equal(["NIR2006 2:16"], diagnostics);
    }

    [Fact]
    public void An_error_operand_adds_nothing_to_a_cast()
    {
        var (_, _, diagnostics) = BindMain("print(nope() as int)");

        Assert.Equal(["NIR2001 2:11"], diagnostics);
    }

    [Fact]
    public void A_converted_value_follows_the_usual_type_rules()
    {
        var (_, _, diagnostics) = BindMain("let a: double = 5 as double", "let b: int = 5 as double", "print(1 as double + 2)");

        Assert.Equal(["NIR3001 3:18", "NIR3003 4:11"], diagnostics);
    }
}
