using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class MemberCallBinderTests
{
    // Statements are on lines 2 and later of `fn main() {`, indented by 4 spaces, so the first starts at 2:5.
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] statements)
    {
        var source = new SourceText("test.nir", "fn main() {\n" + string.Concat(statements.Select(s => "    " + s + "\n")) + "}");
        var bag = new DiagnosticBag();
        var program = Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }

    private static BoundMemberCallExpression PrintedMemberCall(BoundProgram program)
    {
        var print = (BoundCallExpression)((BoundExpressionStatement)program.Functions[0].Body.Statements[0]).Expression;
        return (BoundMemberCallExpression)print.Arguments[0];
    }

    [Fact]
    public void Method_on_a_string_has_the_method_types()
    {
        var (program, _, diagnostics) = BindMain("print(\"abc\".ToUpper())");

        Assert.Empty(diagnostics);
        var call = PrintedMemberCall(program!);
        Assert.Equal("ToUpper", call.Target.Method.Name);
        Assert.Equal(TypeSymbol.StringType, call.Type);
        Assert.Equal(TypeSymbol.StringType, call.Receiver.Type);
    }

    [Theory]
    [InlineData("\"abc\".Contains(\"b\")", "bool")]
    [InlineData("\"abc\".Substring(1)", "string")]
    [InlineData("\"abc\".Substring(1, 1)", "string")]
    [InlineData("\"abc\".IndexOf(\"c\")", "int")]
    [InlineData("\"a-b\".Replace(\"-\", \"+\")", "string")]
    [InlineData("\"  hi \".Trim().ToUpper()", "string")]
    [InlineData("42.ToString()", "string")]
    [InlineData("2.5.ToString()", "string")]
    [InlineData("true.ToString()", "string")]
    [InlineData("(1 + 2).ToString()", "string")]
    [InlineData("10.CompareTo(5)", "int")]
    public void Valid_method_calls_have_the_expected_type(string expression, string expectedType)
    {
        var (program, _, diagnostics) = BindMain($"print({expression})");

        Assert.Empty(diagnostics);
        Assert.Equal(expectedType, PrintedMemberCall(program!).Type.Name);
    }

    [Fact]
    public void Variable_and_parameter_receivers()
    {
        var source = new SourceText("t.nir", "fn shout(s: string) -> string {\n    return s.ToUpper()\n}\nfn main() {\n    let t = \"x\"\n    print(t.Trim())\n}");
        var bag = new DiagnosticBag();
        Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        Assert.Empty(bag);
    }

    [Fact]
    public void Unknown_method_reports_NIR2011_at_the_method()
    {
        var (_, bag, diagnostics) = BindMain("print(\"abc\".Nope())");

        Assert.Equal(["NIR2011 2:17"], diagnostics);
        Assert.Equal("'Nope' is not a member of 'string'", bag.Single().Message);
    }

    [Fact]
    public void Unknown_method_on_a_number()
    {
        var (_, bag, diagnostics) = BindMain("print(42.Nope())");

        Assert.Equal(["NIR2011 2:14"], diagnostics);
        Assert.Equal("'Nope' is not a member of 'int'", bag.Single().Message);
    }

    [Fact]
    public void Wrong_argument_type_reports_NIR3006_at_the_start_of_the_call()
    {
        var (_, _, diagnostics) = BindMain("print(\"abc\".Contains(1))");

        Assert.Equal(["NIR3006 2:11"], diagnostics);
    }

    [Fact]
    public void Wrong_argument_count_reports_NIR2004()
    {
        var (_, bag, diagnostics) = BindMain("print(\"abc\".ToUpper(1))");

        Assert.Equal(["NIR2004 2:11"], diagnostics);
        Assert.Equal("'string.ToUpper' expects 0 argument(s) but got 1", bag.Single().Message);
    }

    [Fact]
    public void Error_receiver_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("print(nope().ToUpper())");

        Assert.Equal(["NIR2001 2:11"], diagnostics);
    }

    [Fact]
    public void Error_argument_adds_nothing()
    {
        var (_, _, diagnostics) = BindMain("print(\"abc\".Contains(nope()))");

        Assert.Equal(["NIR2001 2:26"], diagnostics);
    }

    [Fact]
    public void A_call_that_returns_nothing_has_no_members()
    {
        var (_, bag, diagnostics) = BindMain("print(print(\"a\").ToUpper())");

        Assert.Equal(["NIR2011 2:22"], diagnostics);
        Assert.Equal("'ToUpper' is not a member of '()'", bag.Single().Message);
    }

    [Fact]
    public void Method_returning_an_unsupported_type_reports_NIR3011()
    {
        var (_, _, diagnostics) = BindMain("\"abc\".ToCharArray()");

        Assert.Equal(["NIR3011 2:5"], diagnostics);
    }

    [Fact]
    public void Result_can_be_discarded_or_stored()
    {
        var (_, _, diagnostics) = BindMain("\"abc\".ToUpper()", "let n = \"abc\".IndexOf(\"c\")", "print(n)");

        Assert.Empty(diagnostics);
    }
}
