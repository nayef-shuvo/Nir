using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class BinderTests
{
    private static (BoundProgram? Program, IReadOnlyList<string> Diagnostics) Bind(string text)
    {
        var source = new SourceText("test.nir", text);
        var bag = new DiagnosticBag();
        var unit = SyntaxParser.Parse(source, bag);
        var program = Binder.Bind(unit, source, bag);

        return (program, bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList());
    }

    [Fact]
    public void Valid_program_binds_without_diagnostics()
    {
        var (program, diagnostics) = Bind("fn main() {\n    print(\"hi\")\n}");

        Assert.Empty(diagnostics);
        Assert.NotNull(program);
        Assert.Equal("main", program.EntryPoint.Name);
        Assert.Single(program.Functions);
    }

    [Fact]
    public void Call_before_declaration_is_allowed()
    {
        var (program, diagnostics) = Bind("fn main() {\n    greet()\n}\nfn greet() {\n    print(\"hi\")\n}");

        Assert.Empty(diagnostics);
        Assert.Equal(2, program!.Functions.Length);
    }

    [Fact]
    public void Undefined_function_reports_NIR2001()
    {
        var (_, diagnostics) = Bind("fn main() {\n    prnt(\"x\")\n}");

        Assert.Equal(["NIR2001 2:5"], diagnostics);
    }

    [Fact]
    public void Missing_main_reports_NIR2002_at_1_1()
    {
        var (_, diagnostics) = Bind("fn other() {\n}");

        Assert.Equal(["NIR2002 1:1"], diagnostics);
    }

    [Fact]
    public void Duplicate_function_reports_NIR2003_at_second_name()
    {
        var (_, diagnostics) = Bind("fn main() {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2003 3:4"], diagnostics);
    }

    [Fact]
    public void Print_without_arguments_reports_NIR2004()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print()\n}");

        Assert.Equal(["NIR2004 2:5"], diagnostics);
    }

    [Fact]
    public void Print_with_two_arguments_reports_NIR2004()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(\"a\", \"b\")\n}");

        Assert.Equal(["NIR2004 2:5"], diagnostics);
    }

    [Fact]
    public void Print_with_unit_argument_reports_NIR3006()
    {
        var (_, diagnostics) = Bind("fn greet() {\n}\nfn main() {\n    print(greet())\n}");

        Assert.Equal(["NIR3006 4:5"], diagnostics);
    }

    [Fact]
    public void Error_argument_does_not_add_a_second_diagnostic()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(prnt(\"x\"))\n}");

        Assert.Equal(["NIR2001 2:11"], diagnostics);
    }

    [Fact]
    public void User_function_named_print_wins_over_builtin()
    {
        var (program, diagnostics) = Bind("fn print() {\n}\nfn main() {\n    print()\n}");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundExpressionStatement>(Assert.Single(main.Body.Statements)).Expression);
        Assert.IsType<UserFunctionSymbol>(call.Target);
    }

    [Fact]
    public void Print_binds_to_the_builtin()
    {
        var (program, _) = Bind("fn main() {\n    print(\"hi\")\n}");

        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundExpressionStatement>(Assert.Single(program!.Functions[0].Body.Statements)).Expression);
        Assert.IsType<BuiltinFunctionSymbol>(call.Target);
    }

    [Fact]
    public void Bind_returns_null_when_errors()
    {
        var (program, diagnostics) = Bind("fn main() {\n    prnt(\"x\")\n}");

        Assert.Null(program);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Syntax_errors_are_not_repeated_by_the_binder()
    {
        var (_, diagnostics) = Bind("fn main( {\n}");

        Assert.All(diagnostics, d => Assert.StartsWith("NIR1", d, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("int", "10")]
    [InlineData("double", "2.5")]
    [InlineData("bool", "true")]
    [InlineData("string", "\"s\"")]
    public void Print_picks_the_overload_for_the_argument_type(string parameterType, string argument)
    {
        var (program, diagnostics) = Bind($"fn main() {{\n    print({argument})\n}}");

        Assert.Empty(diagnostics);
        var call = (BoundCallExpression)((BoundExpressionStatement)program!.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(parameterType, call.Target.ParameterTypes[0].Name);
    }

    [Fact]
    public void Int_literal_at_the_limit_is_accepted()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(2147483647)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Int_literal_out_of_range_reports_NIR3004()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(2147483648)\n}");

        Assert.Equal(["NIR3004 2:11"], diagnostics);
    }

    [Fact]
    public void Double_literal_out_of_range_reports_NIR3004()
    {
        var huge = new string('9', 400) + ".0";

        var (_, diagnostics) = Bind($"fn main() {{\n    print({huge})\n}}");

        Assert.Equal(["NIR3004 2:11"], diagnostics);
    }

    [Theory]
    [InlineData("1 + 2.5")]
    [InlineData("1.5 * 2")]
    [InlineData("true + false")]
    [InlineData("true < false")]
    [InlineData("\"a\" + \"b\"")]
    [InlineData("\"a\" == \"b\"")]
    [InlineData("1 == 1.0")]
    [InlineData("1 < true")]
    public void Invalid_operand_types_report_NIR3003(string expression)
    {
        var (_, diagnostics) = Bind($"fn main() {{\n    print({expression})\n}}");

        Assert.Equal(["NIR3003 2:11"], diagnostics);
    }

    [Fact]
    public void Operator_message_names_both_types()
    {
        var source = new SourceText("t.nir", "fn main() {\n    print(1 + 2.5)\n}");
        var bag = new DiagnosticBag();
        Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        Assert.Equal("operator '+' cannot be applied to 'int' and 'double'", Assert.Single(bag).Message);
    }

    [Fact]
    public void Minus_on_bool_reports_NIR3003()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(-true)\n}");

        Assert.Equal(["NIR3003 2:11"], diagnostics);
    }

    [Fact]
    public void Unit_operand_reports_NIR3003()
    {
        var (_, diagnostics) = Bind("fn greet() {\n}\nfn main() {\n    print(greet() + 1)\n}");

        Assert.Equal(["NIR3003 4:11"], diagnostics);
    }

    [Theory]
    [InlineData("1 + 2", "int")]
    [InlineData("1.5 * 2.0", "double")]
    [InlineData("1 < 2", "bool")]
    [InlineData("1.5 >= 2.0", "bool")]
    [InlineData("true == false", "bool")]
    [InlineData("-3", "int")]
    [InlineData("-(1.5)", "double")]
    public void Valid_operations_have_the_expected_type(string expression, string expectedType)
    {
        var (program, diagnostics) = Bind($"fn main() {{\n    print({expression})\n}}");

        Assert.Empty(diagnostics);
        var call = (BoundCallExpression)((BoundExpressionStatement)program!.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(expectedType, call.Arguments[0].Type.Name);
    }

    [Fact]
    public void Error_operand_reports_nothing_more()
    {
        var (_, diagnostics) = Bind("fn main() {\n    print(nope() + 1.5 * true)\n}");

        // `1.5 * true` is its own mistake; `nope() + (...)` adds nothing because its operand already failed.
        Assert.Equal(["NIR2001 2:11", "NIR3003 2:20"], diagnostics);
    }

    // Binds `fn main() {` followed by each given statement on its own line, indented by 4 spaces, then `}`.
    // Statement n is therefore on line n + 1, and its first character is in column 5.
    private static (BoundProgram? Program, IReadOnlyList<string> Diagnostics) BindBody(params string[] statements) =>
        Bind("fn main() {\n" + string.Concat(statements.Select(s => "    " + s + "\n")) + "}");

    [Fact]
    public void Declared_variable_can_be_used()
    {
        var (program, diagnostics) = BindBody("let x = 1", "print(x)");

        Assert.Empty(diagnostics);
        var declaration = (BoundVariableDeclaration)program!.Functions[0].Body.Statements[0];
        Assert.Equal(TypeSymbol.IntType, declaration.Variable.Type);
        Assert.False(declaration.Variable.IsMutable);
    }

    [Fact]
    public void Mutable_variable_is_marked_mutable()
    {
        var (program, _) = BindBody("let mut x = 1");

        Assert.True(((BoundVariableDeclaration)program!.Functions[0].Body.Statements[0]).Variable.IsMutable);
    }

    [Fact]
    public void Undefined_variable_reports_NIR2001()
    {
        var (_, diagnostics) = BindBody("print(y)");

        Assert.Equal(["NIR2001 2:11"], diagnostics);
    }

    [Fact]
    public void Use_before_declaration_reports_NIR2001()
    {
        var (_, diagnostics) = BindBody("print(x)", "let x = 1");

        Assert.Equal(["NIR2001 2:11"], diagnostics);
    }

    [Fact]
    public void Initializer_does_not_see_its_own_variable()
    {
        var (_, diagnostics) = BindBody("let x = x");

        Assert.Equal(["NIR2001 2:13"], diagnostics);
    }

    [Fact]
    public void Redeclaration_in_the_same_block_reports_NIR2005()
    {
        var (_, diagnostics) = BindBody("let x = 1", "let x = 2");

        Assert.Equal(["NIR2005 3:9"], diagnostics);
    }

    [Fact]
    public void Redeclaration_with_another_type_reports_NIR2005()
    {
        var (_, diagnostics) = BindBody("let x = 1", "let mut x = 2.5");

        Assert.Equal(["NIR2005 3:13"], diagnostics);
    }

    [Fact]
    public void Variables_of_different_functions_are_separate()
    {
        var (_, diagnostics) = Bind("fn other() {\n    let x = 1\n}\nfn main() {\n    print(x)\n}");

        Assert.Equal(["NIR2001 5:11"], diagnostics);
    }

    [Fact]
    public void Same_name_in_two_functions_is_fine()
    {
        var (_, diagnostics) = Bind("fn other() {\n    let x = 1\n}\nfn main() {\n    let x = 2\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Assignment_to_immutable_reports_NIR3002()
    {
        var (_, diagnostics) = BindBody("let x = 1", "x = 2");

        Assert.Equal(["NIR3002 3:5"], diagnostics);
    }

    [Fact]
    public void Assignment_to_mutable_is_fine()
    {
        var (_, diagnostics) = BindBody("let mut x = 1", "x = x + 1");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Assignment_to_unknown_name_reports_NIR2001()
    {
        var (_, diagnostics) = BindBody("y = 1");

        Assert.Equal(["NIR2001 2:5"], diagnostics);
    }

    [Fact]
    public void Assignment_of_another_type_reports_NIR3001()
    {
        var (_, diagnostics) = BindBody("let mut x = 1", "x = 2.5");

        Assert.Equal(["NIR3001 3:9"], diagnostics);
    }

    [Fact]
    public void Immutable_and_mistyped_assignment_reports_both()
    {
        var (_, diagnostics) = BindBody("let x = 1", "x = true");

        Assert.Equal(["NIR3002 3:5", "NIR3001 3:9"], diagnostics);
    }

    [Fact]
    public void Annotation_must_match_the_initializer()
    {
        var (_, diagnostics) = BindBody("let x: int = 2.5");

        Assert.Equal(["NIR3001 2:18"], diagnostics);
    }

    [Theory]
    [InlineData("let x: int = 1")]
    [InlineData("let x: double = 2.5")]
    [InlineData("let x: bool = true")]
    [InlineData("let x: string = \"s\"")]
    public void Matching_annotation_is_accepted(string statement)
    {
        var (_, diagnostics) = BindBody(statement);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Unknown_annotation_reports_NIR2006_once()
    {
        var (_, diagnostics) = BindBody("let x: foo = 1", "print(x)");

        Assert.Equal(["NIR2006 2:12"], diagnostics);
    }

    [Fact]
    public void Variable_cannot_have_unit_type()
    {
        var (_, diagnostics) = BindBody("let x = print(1)", "print(1)");

        Assert.Equal(["NIR3005 2:9"], diagnostics);
    }

    [Fact]
    public void Failed_initializer_still_declares_the_variable()
    {
        var (_, diagnostics) = BindBody("let x = nope()", "print(x)", "x = 1");

        Assert.Equal(["NIR2001 2:13", "NIR3002 4:5"], diagnostics);
    }

    [Fact]
    public void Error_typed_variable_does_not_cascade_through_operators()
    {
        var (_, diagnostics) = BindBody("let x = nope()", "print(x + 1)");

        Assert.Equal(["NIR2001 2:13"], diagnostics);
    }
}
