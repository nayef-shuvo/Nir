using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Semantics;

public class ControlFlowBinderTests
{
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) Bind(string text)
    {
        var source = new SourceText("test.nir", text);
        var bag = new DiagnosticBag();
        var program = Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }

    // `lines` become the body of main, one per line, each indented by 4 spaces: line n of the body is line n + 1 of the file.
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind("fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    // ---- conditions ----

    [Theory]
    [InlineData("if 1 {", "NIR3001 2:8")]
    [InlineData("if \"x\" {", "NIR3001 2:8")]
    [InlineData("if 2.5 {", "NIR3001 2:8")]
    [InlineData("while 0 {", "NIR3001 2:11")]
    public void A_condition_that_is_not_a_bool_reports_NIR3001(string header, string expected)
    {
        var (_, _, diagnostics) = BindMain(header, "}");

        Assert.Equal([expected], diagnostics);
    }

    [Fact]
    public void Condition_message_names_both_types()
    {
        var (_, bag, _) = BindMain("if 1 {", "}");

        Assert.Equal("cannot convert 'int' to 'bool'", Assert.Single(bag).Message);
    }

    [Theory]
    [InlineData("if nope() {")]
    [InlineData("while nope() {")]
    public void An_error_in_the_condition_adds_nothing(string header)
    {
        var (_, _, diagnostics) = BindMain(header, "}");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR2001", diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_comparison_is_reported_once_not_also_as_a_bad_condition()
    {
        var (_, _, diagnostics) = BindMain("if 1 < 2.5 {", "}");

        Assert.Equal(["NIR3003 2:8"], diagnostics);
    }

    [Fact]
    public void If_else_if_else_binds_and_else_if_is_an_else_block_holding_one_if()
    {
        var (program, _, diagnostics) = BindMain("let x = 3", "if x < 1 {", "} else if x < 5 {", "} else {", "}");

        Assert.Empty(diagnostics);
        var first = (BoundIfStatement)program!.Functions[0].Body.Statements[1];
        var nested = (BoundIfStatement)Assert.Single(first.ElseBlock!.Statements);
        Assert.NotNull(nested.ElseBlock);
    }

    // ---- loops ----

    [Fact]
    public void For_with_int_bounds_binds_an_immutable_int_variable()
    {
        var (program, _, diagnostics) = BindMain("for i in 0..3 {", "    print(i)", "}");

        Assert.Empty(diagnostics);
        var loop = (BoundForStatement)program!.Functions[0].Body.Statements[0];
        Assert.Equal(TypeSymbol.IntType, loop.Variable.Type);
        Assert.False(loop.Variable.IsMutable);
        Assert.False(loop.IsInclusive);
    }

    [Fact]
    public void Inclusive_range_is_marked_inclusive()
    {
        var (program, _, _) = BindMain("for i in 0..=3 {", "}");

        Assert.True(((BoundForStatement)program!.Functions[0].Body.Statements[0]).IsInclusive);
    }

    [Theory]
    [InlineData("for i in 0..2.5 {", "NIR3001 2:17")]
    [InlineData("for i in 1.5..3 {", "NIR3001 2:14")]
    [InlineData("for i in 0..\"x\" {", "NIR3001 2:17")]
    public void A_range_bound_that_is_not_an_int_reports_NIR3001(string header, string expected)
    {
        var (_, _, diagnostics) = BindMain(header, "}");

        Assert.Equal([expected], diagnostics);
    }

    [Fact]
    public void The_loop_variable_cannot_be_assigned()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..3 {", "    i = 1", "}");

        Assert.Equal(["NIR3002 3:9"], diagnostics);
    }

    [Fact]
    public void The_loop_variable_ends_with_the_loop()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..3 {", "}", "print(i)");

        Assert.Equal(["NIR2001 4:11"], diagnostics);
    }

    [Fact]
    public void The_bounds_cannot_use_the_loop_variable()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..i {", "}");

        Assert.Equal(["NIR2001 2:17"], diagnostics);
    }

    [Fact]
    public void Break_and_continue_inside_loops_bind()
    {
        var (_, _, diagnostics) = BindMain("while true {", "    if true {", "        break", "    }", "    continue", "}");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("break", "break")]
    [InlineData("continue", "continue")]
    public void Break_and_continue_outside_a_loop_report_NIR3010(string keyword, string word)
    {
        var (_, bag, diagnostics) = BindMain(keyword);

        Assert.Equal(["NIR3010 2:5"], diagnostics);
        Assert.Equal($"'{word}' can only be used inside a loop", bag.Single().Message);
    }

    [Fact]
    public void Break_after_the_loop_has_ended_is_outside_it()
    {
        var (_, _, diagnostics) = BindMain("while true {", "}", "break");

        Assert.Equal(["NIR3010 4:5"], diagnostics);
    }

    [Fact]
    public void Break_in_the_next_function_is_not_inside_the_previous_loop()
    {
        var (_, _, diagnostics) = Bind("fn a() {\n    while true {\n    }\n}\nfn main() {\n    break\n}");

        Assert.Equal(["NIR3010 6:5"], diagnostics);
    }

    // ---- scopes ----

    [Fact]
    public void A_nested_block_cannot_redeclare_a_local_of_an_enclosing_block()
    {
        var (_, bag, diagnostics) = BindMain("let total = 0", "if true {", "    let total = 1", "}");

        Assert.Equal(["NIR2005 4:13"], diagnostics);
        Assert.Equal("'total' is already declared in an enclosing block", bag.Single().Message);
    }

    [Fact]
    public void A_nested_block_cannot_redeclare_a_parameter()
    {
        var (_, _, diagnostics) = Bind("fn f(x: int) {\n    if true {\n        let x = 1\n    }\n}\nfn main() {\n}");

        Assert.Equal(["NIR2005 3:13"], diagnostics);
    }

    [Fact]
    public void A_loop_body_cannot_redeclare_the_loop_variable()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..3 {", "    let i = 1", "}");

        Assert.Equal(["NIR2005 3:13"], diagnostics);
    }

    [Fact]
    public void A_loop_variable_cannot_reuse_a_local_name()
    {
        var (_, _, diagnostics) = BindMain("let i = 0", "for i in 0..3 {", "}");

        Assert.Equal(["NIR2005 3:9"], diagnostics);
    }

    [Fact]
    public void A_nested_loop_cannot_reuse_the_outer_loop_variable()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..3 {", "    for i in 0..3 {", "    }", "}");

        Assert.Equal(["NIR2005 3:13"], diagnostics);
    }

    [Fact]
    public void Blocks_side_by_side_may_reuse_names()
    {
        var (_, _, diagnostics) = BindMain(
            "if true {", "    let step = 1", "    print(step)", "}",
            "if true {", "    let step = 2", "    print(step)", "}",
            "for i in 0..2 {", "}",
            "for i in 0..2 {", "}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_name_can_be_declared_after_a_block_that_used_it_has_ended()
    {
        var (_, _, diagnostics) = BindMain("if true {", "    let step = 1", "}", "let step = 2", "print(step)");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Outer_variables_are_visible_and_assignable_inside_blocks()
    {
        var (_, _, diagnostics) = BindMain("let mut n = 0", "while n < 3 {", "    n = n + 1", "}", "print(n)");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_variable_declared_in_a_block_is_not_visible_after_it()
    {
        var (_, _, diagnostics) = BindMain("if true {", "    let inner = 1", "}", "print(inner)");

        Assert.Equal(["NIR2001 5:11"], diagnostics);
    }

    // ---- logical operators ----

    [Theory]
    [InlineData("print(1 && 2)")]
    [InlineData("print(true && 1)")]
    [InlineData("print(\"a\" || true)")]
    [InlineData("print(!1)")]
    [InlineData("print(!\"x\")")]
    public void Logical_operators_need_booleans_and_report_NIR3003(string statement)
    {
        var (_, _, diagnostics) = BindMain(statement);

        Assert.Equal(["NIR3003 2:11"], diagnostics);
    }

    [Theory]
    [InlineData("print(true && false)")]
    [InlineData("print(true || false)")]
    [InlineData("print(!true)")]
    [InlineData("print(1 < 2 && 3 == 3 || !false)")]
    public void Logical_operators_on_booleans_bind(string statement)
    {
        var (program, _, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        var print = (BoundCallExpression)((BoundExpressionStatement)program!.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(TypeSymbol.BoolType, print.Arguments[0].Type);
    }

    // ---- every path returns ----

    [Theory]
    [InlineData("fn f(a: bool) -> int {\n    if a {\n        return 1\n    } else {\n        return 2\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f(a: int) -> int {\n    if a < 0 {\n        return 1\n    } else if a == 0 {\n        return 2\n    } else {\n        return 3\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f(a: bool, b: bool) -> int {\n    if a {\n        if b {\n            return 1\n        } else {\n            return 2\n        }\n    } else {\n        return 3\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f(a: bool) -> int {\n    if a {\n        print(1)\n    }\n    return 2\n}\nfn main() {\n}")]
    public void Functions_whose_every_path_returns_are_accepted(string source)
    {
        var (_, _, diagnostics) = Bind(source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("fn f(a: bool) -> int {\n    if a {\n        return 1\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f(a: bool) -> int {\n    if a {\n        return 1\n    } else {\n        print(2)\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f(a: int) -> int {\n    if a < 0 {\n        return 1\n    } else if a == 0 {\n        return 2\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f() -> int {\n    while true {\n        return 1\n    }\n}\nfn main() {\n}")]
    [InlineData("fn f() -> int {\n    for i in 0..3 {\n        return i\n    }\n}\nfn main() {\n}")]
    public void Functions_with_a_path_that_does_not_return_report_NIR3007(string source)
    {
        var (_, _, diagnostics) = Bind(source);

        Assert.Equal(["NIR3007 1:4"], diagnostics);
    }

    [Fact]
    public void Errors_inside_a_loop_body_are_reported_once()
    {
        var (_, _, diagnostics) = BindMain("for i in 0..3 {", "    print(missing)", "}");

        Assert.Equal(["NIR2001 3:15"], diagnostics);
    }
}
