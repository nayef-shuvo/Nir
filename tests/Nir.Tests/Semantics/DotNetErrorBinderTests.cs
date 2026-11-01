using Nir.Compiler.Diagnostics;
using Nir.Compiler.Semantics;
using static Nir.Tests.Support.BinderHarness;

namespace Nir.Tests.Semantics;

public class DotNetErrorBinderTests
{
    // Lines 1 and 2 are the import and a blank line, so `fn run() -> anyerror! {` is line 3 and its statements start on line 4.
    private const string Imports = "using clr::cs::System::{Int32, IO}\n\n";

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindRun(params string[] lines) =>
        Bind(Imports + "fn run() -> anyerror! {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}\nfn main() {\n}");

    [Fact]
    public void Try_on_a_dotnet_call_gives_its_value_type()
    {
        var (program, _, diagnostics) = BindRun("let n: int = try Int32::Parse(\"1\")", "let t: string = try \"abc\".ToUpper()", "try IO::File::Delete(\"x\")");

        Assert.Empty(diagnostics);
        var run = program!.Functions.Single(f => f.Symbol.Name == "run");
        var first = Assert.IsType<BoundTry>(((BoundVariableDeclaration)run.Body.Statements[0]).Initializer);
        var protectedCall = Assert.IsType<BoundProtectedCall>(first.Operand);
        Assert.Equal("anyerror!int", protectedCall.Type.Name);
        Assert.Equal(TypeSymbol.UnitType, ((BoundTry)((BoundExpressionStatement)run.Body.Statements[2]).Expression).Type);
    }

    [Fact]
    public void A_dotnet_call_without_try_or_catch_is_unchanged()
    {
        var (program, _, diagnostics) = BindRun("let n: int = Int32::Parse(\"1\")");

        Assert.Empty(diagnostics);
        var run = program!.Functions.Single(f => f.Symbol.Name == "run");
        Assert.IsType<BoundCallExpression>(((BoundVariableDeclaration)run.Body.Statements[0]).Initializer);
    }

    [Fact]
    public void Catch_on_a_dotnet_call_takes_a_fallback_or_a_handler()
    {
        var (_, _, diagnostics) = BindRun(
            "let a: int = Int32::Parse(\"1\") catch 0",
            "let b: int = Int32::Parse(\"1\") catch e => e.message.Length",
            "let c: int = Int32::Parse(\"1\") catch e => {",
            "    return",
            "}",
            "IO::File::Delete(\"x\") catch e => print(e.message)");

        Assert.Single(diagnostics); // `Length` is a property; everything else is fine
        Assert.StartsWith("NIR2011", diagnostics[0]);
    }

    [Fact]
    public void The_handler_variable_is_an_anyerror()
    {
        var (_, _, diagnostics) = BindRun("let a = Int32::Parse(\"1\") catch e => e.message.Length");
        var (_, _, ok) = BindRun("IO::File::Delete(\"x\") catch e => print(e.message)");

        Assert.Equal(["NIR2011 4:52"], diagnostics);
        Assert.Empty(ok);
    }

    [Fact]
    public void The_fallback_must_have_the_calls_type()
    {
        var (_, bag, diagnostics) = BindRun("let a = Int32::Parse(\"1\") catch \"zero\"");

        Assert.Equal(["NIR3001 4:37"], diagnostics);
        Assert.Equal("cannot convert 'string' to 'int'", bag.Single().Message);
    }

    [Fact]
    public void A_call_with_no_value_has_nothing_to_fall_back_to()
    {
        var (_, _, diagnostics) = BindRun("IO::File::Delete(\"x\") catch 1");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR3001", diagnostics[0]);
    }

    [Fact]
    public void Catch_with_a_value_on_a_dotnet_call_can_sit_inside_a_larger_expression()
    {
        var (_, _, diagnostics) = BindRun("print(1 + (Int32::Parse(\"1\") catch 0) + (Int32::Parse(\"2\") catch e => 0))");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_catch_with_a_block_on_a_dotnet_call_still_has_to_be_the_whole_value_of_a_statement()
    {
        var (_, _, diagnostics) = BindRun("print(Int32::Parse(\"1\") catch e => {\n        return\n    })");

        Assert.Equal(["NIR3026 4:11"], diagnostics);
    }

    [Fact]
    public void Try_on_a_dotnet_call_cannot_sit_inside_a_larger_expression()
    {
        var (_, _, diagnostics) = BindRun("let a = 1 + try Int32::Parse(\"1\")");

        Assert.Equal(["NIR3026 4:17"], diagnostics);
    }

    [Fact]
    public void Try_on_a_dotnet_call_needs_a_function_that_can_return_the_error()
    {
        var (_, _, plain) = Bind(Imports + "fn main() {\n    let a = try Int32::Parse(\"1\")\n}");
        var (_, _, narrow) = Bind(Imports + "type E = error { n: int }\nfn f() -> E!int {\n    let a = try Int32::Parse(\"1\")\n    return a\n}\nfn main() {\n}");

        Assert.Equal(["NIR3023 4:13"], plain);
        Assert.Equal(["NIR3024 5:13"], narrow);
    }

    [Fact]
    public void Arguments_that_have_errors_are_reported_once()
    {
        var (_, _, diagnostics) = BindRun("let a = try Int32::Parse(nope)");

        Assert.Equal(["NIR2001 4:30"], diagnostics);
    }

    [Fact]
    public void Matching_on_a_caught_dotnet_result_works_through_a_variable()
    {
        var (_, _, diagnostics) = Bind(
            Imports + "fn parse(s: string) -> anyerror!int {\n    let n = try Int32::Parse(s)\n    return n\n}\nfn main() {\n    match parse(\"1\") {\n        n: int => print(n)\n        e: anyerror => print(e.message)\n    }\n}");

        Assert.Empty(diagnostics);
    }
}
