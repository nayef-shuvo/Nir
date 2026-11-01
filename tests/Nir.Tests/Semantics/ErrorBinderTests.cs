using Nir.Compiler.Diagnostics;
using Nir.Compiler.Semantics;
using static Nir.Tests.Support.BinderHarness;

namespace Nir.Tests.Semantics;

public class ErrorBinderTests
{
    // Lines 1 and 2 declare the errors and `fn main() {` is line 3, so the first statement of main is on line 4 at column 5.
    private const string Errors =
        "type ParseError = error { message: string, line: int }\n" +
        "type Plain = error { code: int }\n";

    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines) =>
        Bind(Errors + "fn main() {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");

    // A function returning anyerror! so that `try` is allowed; the statements start on line 4.
    private static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) BindInResult(params string[] lines) =>
        Bind(
            Errors + "fn parse(s: string) -> ParseError!int {\n    return 1\n}\nfn plain() -> Plain!string {\n    return \"x\"\n}\nfn none() -> ParseError! {\n}\n" +
            "fn run() -> anyerror!int {\n" + string.Concat(lines.Select(l => "    " + l + "\n")) + "    return 0\n}\nfn main() {\n}");

    // Lines 3 to 10 are the helpers of BindInResult; `fn run` is line 11 and its first statement is line 12.

    // ---- error types ----

    [Fact]
    public void An_error_type_is_a_struct_symbol_marked_as_an_error()
    {
        var (program, _, diagnostics) = BindMain();

        Assert.Empty(diagnostics);
        Assert.All(program!.Structs, s => Assert.True(s.IsError));
        Assert.Equal(["message", "line"], program.Structs[0].Fields.Select(f => f.Name));
    }

    [Fact]
    public void An_error_literal_needs_every_field()
    {
        var (_, bag, diagnostics) = BindMain("let e = error ParseError { message = \"x\" }");

        Assert.Equal(["NIR2017 4:19"], diagnostics);
        Assert.Equal("missing field 'line' in 'ParseError'", bag.Single().Message);
    }

    [Fact]
    public void An_error_literal_checks_field_types()
    {
        var (_, _, diagnostics) = BindMain("let e = error ParseError { message = 1, line = 1 }");

        Assert.Equal(["NIR3001 4:42"], diagnostics);
    }

    [Fact]
    public void An_error_type_cannot_be_made_without_the_error_keyword()
    {
        var (_, bag, diagnostics) = BindMain("let e = Plain { code = 1 }");

        Assert.Equal(["NIR3028 4:13"], diagnostics);
        Assert.Equal("'Plain' is an error type; make one with 'error Plain { ... }'", bag.Single().Message);
    }

    [Fact]
    public void The_error_keyword_needs_an_error_type()
    {
        var (_, bag, diagnostics) = Bind("type P = struct { n: int }\nfn main() {\n    let e = error P { n = 1 }\n}");

        Assert.Equal(["NIR3028 3:19"], diagnostics);
        Assert.Equal("'P' is a struct, not an error type; write it without 'error'", bag.Single().Message);
    }

    [Fact]
    public void An_error_message_makes_an_anyerror()
    {
        var (program, _, diagnostics) = BindMain("let e = error \"boom\"", "let m: string = e.message");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        Assert.Same(AnyErrorTypeSymbol.Instance, ((BoundVariableDeclaration)main.Body.Statements[0]).Variable.Type);
    }

    [Fact]
    public void An_error_message_must_be_a_string()
    {
        var (_, _, diagnostics) = BindMain("let e = error 5");

        Assert.Equal(["NIR3001 4:19"], diagnostics);
    }

    [Fact]
    public void The_fields_of_a_declared_error_are_readable()
    {
        var (_, _, diagnostics) = BindMain("let e = error ParseError { message = \"x\", line = 3 }", "let n: int = e.line", "let m: string = e.message");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_declared_error_without_a_message_field_has_no_message_to_read()
    {
        var (_, _, diagnostics) = BindMain("let e = error Plain { code = 1 }", "let m = e.message");

        Assert.Equal(["NIR2011 5:15"], diagnostics);
    }

    [Fact]
    public void A_declared_error_converts_to_anyerror_and_anyerror_has_a_message()
    {
        var (_, _, diagnostics) = BindMain("let e = error Plain { code = 1 }", "let a: anyerror = e", "let m: string = a.message");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_anyerror_has_nothing_but_a_message()
    {
        var (_, _, diagnostics) = BindMain("let a = error \"x\"", "let n = a.code");

        Assert.Equal(["NIR2011 5:15"], diagnostics);
    }

    [Fact]
    public void An_error_value_cannot_be_changed()
    {
        var (_, bag, diagnostics) = BindMain("let mut e = error ParseError { message = \"x\", line = 1 }", "e.line = 2");

        Assert.Equal(["NIR3002 5:5"], diagnostics);
        Assert.Equal("cannot assign to 'e.line' because error values cannot be changed", bag.Single().Message);
    }

    [Fact]
    public void A_variable_holding_an_error_can_be_replaced()
    {
        var (_, _, diagnostics) = BindMain("let mut e = error ParseError { message = \"x\", line = 1 }", "e = error ParseError { message = \"y\", line = 2 }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Errors_cannot_be_compared_printed_or_interpolated()
    {
        var (_, _, a) = BindMain("let e = error Plain { code = 1 }", "let same = e == e");
        var (_, _, b) = BindMain("let e = error Plain { code = 1 }", "print(e)");
        var (_, bag, c) = BindMain("let e = error \"x\"", "print($\"{e}\")");

        Assert.StartsWith("NIR3003", Assert.Single(a));
        Assert.StartsWith("NIR3006", Assert.Single(b));
        Assert.StartsWith("NIR3016", Assert.Single(c));
        Assert.Equal("cannot put a value of type 'anyerror' in an interpolated string; use its 'message'", bag.Single().Message);
    }

    [Fact]
    public void An_error_type_cannot_have_methods_or_be_borrowed()
    {
        var (_, _, impl) = Bind("type E = error { n: int }\nE impl {\n}\nfn main() {\n}");
        var (_, _, borrow) = Bind("type E = error { n: int }\nfn f(e: &E) {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2014 2:1"], impl);
        Assert.Equal(["NIR3017 2:9"], borrow);
    }

    [Fact]
    public void An_error_type_may_hold_other_errors_and_structs_without_a_cycle_error()
    {
        var (_, _, diagnostics) = Bind("type Inner = error { n: int }\ntype P = struct { x: int }\ntype Outer = error { inner: Inner, p: P, t: (int, string) }\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    // ---- result types ----

    [Fact]
    public void A_result_type_is_a_union_of_the_error_and_the_value()
    {
        var (program, _, diagnostics) = Bind(Errors + "fn f() -> ParseError!int {\n    return 1\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        var result = Assert.IsType<ResultTypeSymbol>(program!.Functions.Single(f => f.Symbol.Name == "f").Symbol.ReturnType);
        Assert.Equal("ParseError!int", result.Name);
        Assert.Equal(["ParseError", "int"], result.Members.Select(m => m.Name));
        Assert.Contains(result, program.Unions);
    }

    [Fact]
    public void The_same_result_written_twice_is_one_type()
    {
        var (program, _, diagnostics) = Bind(Errors + "fn f() -> ParseError!int {\n    return 1\n}\nfn g() -> ParseError!int {\n    return 2\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        Assert.Single(program!.Unions.OfType<ResultTypeSymbol>());
    }

    [Fact]
    public void A_result_without_a_value_has_only_the_error_member()
    {
        var (program, _, diagnostics) = Bind(Errors + "fn f() -> ParseError! {\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        var result = Assert.IsType<ResultTypeSymbol>(program!.Functions.Single(f => f.Symbol.Name == "f").Symbol.ReturnType);
        Assert.Null(result.Success);
        Assert.Single(result.Members);
    }

    [Fact]
    public void The_type_before_the_bang_must_be_an_error_type()
    {
        var (_, bag, diagnostics) = Bind("type P = struct { n: int }\nfn f() -> P!int {\n}\nfn g() -> int!int {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2023 2:11", "NIR2023 4:11"], diagnostics);
        Assert.Equal("the type before '!' must be an error type or 'anyerror', but 'P' is not one", bag[0].Message);
    }

    [Fact]
    public void An_unknown_type_before_the_bang_is_reported()
    {
        var (_, _, diagnostics) = Bind("fn f() -> Nope!int {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2006 1:11"], diagnostics);
    }

    [Fact]
    public void The_value_after_the_bang_cannot_be_an_error_or_a_result()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn f() -> ParseError!Plain {\n}\nfn g() -> ParseError!anyerror {\n}\nfn h() -> ParseError!Plain!int {\n}\nfn main() {\n}");

        Assert.Equal(["NIR2024 3:22", "NIR2024 5:22", "NIR2024 7:22"], diagnostics);
        Assert.Equal("the value after '!' cannot be an error or a result, but 'Plain' is one", bag[0].Message);
    }

    [Fact]
    public void Results_are_values_that_can_be_fields_parameters_and_tuple_elements()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "type S = struct { r: ParseError!int }\nfn f(r: ParseError!int, t: (int, Plain!string)) -> ParseError!int {\n    return r\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    // ---- making results ----

    [Fact]
    public void A_value_and_an_error_both_convert_to_the_result()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn f(flag: bool) -> ParseError!int {\n    if flag {\n        return 1\n    }\n    return error ParseError { message = \"x\", line = 1 }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_declared_error_converts_to_an_anyerror_result()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn f() -> anyerror!int {\n    return error Plain { code = 1 }\n}\nfn g() -> anyerror!int {\n    return error \"text\"\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_wrong_error_or_value_does_not_convert()
    {
        var (_, bag, diagnostics) = Bind(
            Errors + "fn f() -> ParseError!int {\n    return error Plain { code = 1 }\n}\nfn g() -> ParseError!int {\n    return \"x\"\n}\nfn main() {\n}");

        Assert.Equal(["NIR3001 4:12", "NIR3001 7:12"], diagnostics);
        Assert.Equal("cannot convert 'Plain' to 'ParseError!int'", bag[0].Message);
    }

    [Fact]
    public void A_bare_return_succeeds_in_a_function_with_no_value()
    {
        var (program, _, diagnostics) = Bind(
            Errors + "fn f(flag: bool) -> ParseError! {\n    if flag {\n        return\n    }\n    return error ParseError { message = \"x\", line = 1 }\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
        var f = program!.Functions.Single(x => x.Symbol.Name == "f");
        var returnStatement = (BoundReturnStatement)((BoundIfStatement)f.Body.Statements[0]).Then.Statements[0];
        Assert.IsType<BoundUnitSuccess>(returnStatement.Value);
    }

    [Fact]
    public void A_function_with_no_value_may_end_without_a_return()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn f() -> ParseError! {\n    print(\"x\")\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_function_with_a_value_must_still_return_on_every_path()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn f() -> ParseError!int {\n}\nfn main() {\n}");

        Assert.Equal(["NIR3007 3:4"], diagnostics);
    }

    [Fact]
    public void Main_may_return_an_error_result_without_a_value()
    {
        var (_, _, ok) = Bind(Errors + "fn main() -> anyerror! {\n}");
        var (_, _, plain) = Bind(Errors + "fn main() -> Plain! {\n}");
        var (_, bag, withValue) = Bind(Errors + "fn main() -> anyerror!int {\n    return 1\n}");

        Assert.Empty(ok);
        Assert.Empty(plain);
        Assert.Equal(["NIR2007 3:4"], withValue);
        Assert.Equal("'main' must have no parameters and return nothing, or 'E!' for an error type E", bag.Single().Message);
    }

    // ---- try ----

    [Fact]
    public void Try_gives_the_value_and_widens_the_error_to_anyerror()
    {
        var (program, _, diagnostics) = BindInResult("let a: int = try parse(\"x\")", "let b: string = try plain()", "try none()");

        Assert.Empty(diagnostics);
        var run = program!.Functions.Single(f => f.Symbol.Name == "run");
        var tryExpression = Assert.IsType<BoundTry>(((BoundVariableDeclaration)run.Body.Statements[0]).Initializer);
        Assert.Equal(TypeSymbol.IntType, tryExpression.Type);
        Assert.Equal("anyerror!int", tryExpression.FunctionResult.Name);
        Assert.Equal(TypeSymbol.UnitType, ((BoundTry)((BoundExpressionStatement)run.Body.Statements[2]).Expression).Type);
    }

    [Fact]
    public void Try_is_allowed_as_the_whole_value_of_a_let_an_assignment_a_return_and_a_statement()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn run() -> anyerror!int {\n    let mut a = try parse()\n    a = try parse()\n    try parse()\n    let (x, y) = try pair()\n    return try parse()\n}\nfn pair() -> ParseError!(int, int) {\n    return (1, 2)\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("let a = 1 + try parse(\"x\")", "NIR3026 12:17")]
    [InlineData("print(try parse(\"x\"))", "NIR3026 12:11")]
    [InlineData("let a = (try parse(\"x\"))", "NIR3026 12:14")]
    public void Try_anywhere_else_is_reported(string statement, string expected)
    {
        var (_, bag, diagnostics) = BindInResult(statement);

        Assert.Equal(expected, diagnostics[0]);
        if (expected.StartsWith("NIR3026", StringComparison.Ordinal))
        {
            Assert.StartsWith("'try' can only be the whole value", bag[0].Message);
        }
    }

    [Fact]
    public void Try_needs_a_function_that_returns_a_result()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn f() -> int {\n    let a = try parse()\n    return a\n}\nfn main() {\n}");

        Assert.Equal(["NIR3023 7:13"], diagnostics);
        Assert.StartsWith("'try' returns the error to the caller, but 'f' returns 'int'", bag.Single().Message);
    }

    [Fact]
    public void Try_in_main_with_no_result_is_reported()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    try parse()\n}");

        Assert.Equal(["NIR3023 7:5"], diagnostics);
    }

    [Fact]
    public void Try_of_an_error_the_function_cannot_hold_is_reported()
    {
        var (_, bag, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn f() -> Plain!int {\n    let a = try parse()\n    return a\n}\nfn main() {\n}");

        Assert.Equal(["NIR3024 7:13"], diagnostics);
        Assert.Equal(
            "'try' would return a 'ParseError' from 'f', which returns 'Plain!int'; handle it with 'catch' and make a 'Plain' yourself",
            bag.Single().Message);
    }

    [Fact]
    public void Try_of_the_same_error_type_is_fine()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn f() -> ParseError!string {\n    let a = try parse()\n    return \"x\"\n}\nfn main() {\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Try_of_a_plain_value_is_reported()
    {
        var (_, bag, diagnostics) = BindInResult("let a = try 5");

        Assert.Equal(["NIR3025 12:17"], diagnostics);
        Assert.Equal("'try' needs a value of an 'E!T' type, but this is 'int'", bag.Single().Message);
    }

    [Fact]
    public void An_error_inside_a_try_is_reported_once()
    {
        var (_, _, diagnostics) = BindInResult("let a = try nope()");

        Assert.Equal(["NIR2001 12:17"], diagnostics);
    }

    // ---- catch ----

    [Fact]
    public void Catch_forms_all_have_the_value_type()
    {
        var (program, _, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n" +
            "    let a: int = parse() catch 80\n" +
            "    let b: int = parse() catch e => e.line\n" +
            "    let c: int = parse() catch e => {\n        return\n    }\n" +
            "}");

        Assert.Empty(diagnostics);
        var main = program!.Functions.Single(f => f.Symbol.Name == "main");
        Assert.All(main.Body.Statements.Cast<BoundVariableDeclaration>(), s => Assert.IsType<BoundCatch>(s.Initializer));
        var block = (BoundCatch)((BoundVariableDeclaration)main.Body.Statements[2]).Initializer;
        Assert.NotNull(block.Body);
        Assert.Equal("e", ((BoundCatch)((BoundVariableDeclaration)main.Body.Statements[1]).Initializer).Variable!.Name);
    }

    [Fact]
    public void The_handler_variable_has_the_error_type_and_is_immutable()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n" +
            "    let a = parse() catch e => {\n        e = error ParseError { message = \"y\", line = 2 }\n        return\n    }\n" +
            "}");

        Assert.Equal(["NIR3002 8:9"], diagnostics);
    }

    [Fact]
    public void A_catch_variable_follows_the_scope_rules_and_ends_with_the_catch()
    {
        var (_, _, shadow) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let e = 1\n    let a = parse() catch e => 1\n}");
        var (_, _, after) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let a = parse() catch e => 1\n    print(e)\n}");

        Assert.Equal(["NIR2005 8:27"], shadow);
        Assert.Equal(["NIR2001 8:11"], after);
    }

    [Fact]
    public void The_fallback_must_have_the_value_type()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let a = parse() catch \"x\"\n}");

        Assert.Equal(["NIR3001 7:27"], diagnostics);
        Assert.Equal("cannot convert 'string' to 'int'", bag.Single().Message);
    }

    [Fact]
    public void A_result_with_no_value_has_nothing_to_fall_back_to()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn none() -> ParseError! {\n}\nfn main() {\n    none() catch 1\n}");

        Assert.Equal(["NIR3001 6:18"], diagnostics);
        Assert.Contains("carries no value", bag.Single().Message);
    }

    [Fact]
    public void A_handler_on_a_result_with_no_value_need_not_return()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn none() -> ParseError! {\n}\nfn main() {\n    none() catch e => {\n        print(e.message)\n    }\n    none() catch e => print(e.message)\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_block_of_a_catch_with_a_value_must_return_on_every_path()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let a = parse() catch e => {\n        print(e.message)\n    }\n}");

        Assert.Equal(["NIR3027 7:13"], diagnostics);
        Assert.StartsWith("the block of a 'catch' must end with a 'return'", bag.Single().Message);
    }

    [Fact]
    public void A_catch_with_a_block_is_only_allowed_as_the_whole_value_of_a_statement()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    print(parse() catch e => {\n        return\n    })\n}");

        Assert.Equal(["NIR3026 7:11"], diagnostics);
    }

    [Fact]
    public void A_catch_with_a_value_is_allowed_anywhere()
    {
        var (_, _, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    print(1 + (parse() catch 2) + (parse() catch e => e.line))\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Catch_of_a_plain_value_is_reported()
    {
        var (_, bag, diagnostics) = BindMain("let a = 5 catch 1");

        Assert.Equal(["NIR3025 4:13"], diagnostics);
        Assert.Equal("'catch' needs a value of an 'E!T' type, but this is 'int'", bag.Single().Message);
    }

    // ---- match on results ----

    [Fact]
    public void A_match_takes_a_result_apart()
    {
        var (_, _, diagnostics) = Bind(
            Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let r = match parse() { n: int => n, e: ParseError => e.line }\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_match_on_a_result_must_cover_both_sides()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let r = match parse() { n: int => n }\n}");

        Assert.Equal(["NIR3021 7:19"], diagnostics);
        Assert.Equal("match on 'ParseError!int' does not cover 'ParseError'", bag.Single().Message);
    }

    [Fact]
    public void A_result_with_no_value_cannot_be_matched()
    {
        var (_, bag, diagnostics) = Bind(Errors + "fn none() -> ParseError! {\n}\nfn main() {\n    match none() {\n        e: ParseError => print(e.message)\n    }\n}");

        Assert.Single(diagnostics);
        Assert.StartsWith("NIR3018", diagnostics[0]);
        Assert.Contains("use 'catch' instead", bag.Single().Message);
    }

    [Fact]
    public void A_result_cannot_be_compared_or_printed()
    {
        var (_, _, a) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    let same = parse() == parse()\n}");
        var (_, _, b) = Bind(Errors + "fn parse() -> ParseError!int {\n    return 1\n}\nfn main() {\n    print(parse())\n}");

        Assert.StartsWith("NIR3003", Assert.Single(a));
        Assert.StartsWith("NIR3006", Assert.Single(b));
    }
}
