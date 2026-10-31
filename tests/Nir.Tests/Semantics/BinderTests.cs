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
    public void Unit_result_is_not_a_string_argument()
    {
        var (_, diagnostics) = Bind("fn greet() {\n}\nfn main() {\n    print(greet())\n}");

        Assert.Equal(["NIR3001 4:11"], diagnostics);
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
}
