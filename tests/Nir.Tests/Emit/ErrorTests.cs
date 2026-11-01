using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class ErrorTests
{
    private const string Types =
        "type ParseError = error { message: string, line: int }\n" +
        "type Plain = error { code: int }";

    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void A_function_returns_a_value_or_an_error_and_catch_gives_a_fallback()
    {
        Assert.Equal("8080\n80\n", Run(
            Types,
            "fn parse_port(s: string) -> ParseError!int {",
            "    if s == \"\" {",
            "        return error ParseError { message = \"empty\", line = 1 }",
            "    }",
            "    return 8080",
            "}",
            "fn main() {",
            "    print(parse_port(\"x\") catch 80)",
            "    print(parse_port(\"\") catch 80)",
            "}"));
    }

    [Fact]
    public void The_handler_sees_the_error_and_its_fields()
    {
        Assert.Equal("empty at line 7\n", Run(
            Types,
            "fn parse() -> ParseError!int {",
            "    return error ParseError { message = \"empty\", line = 7 }",
            "}",
            "fn main() {",
            "    parse() catch e => {",
            "        print($\"{e.message} at line {e.line}\")",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void A_catch_block_can_return_from_the_function()
    {
        Assert.Equal("failed: empty\nafter\n", Run(
            Types,
            "fn parse() -> ParseError!int {",
            "    return error ParseError { message = \"empty\", line = 1 }",
            "}",
            "fn run() {",
            "    let n = parse() catch e => {",
            "        print($\"failed: {e.message}\")",
            "        return",
            "    }",
            "    print(\"not reached\")",
            "}",
            "fn main() {",
            "    run()",
            "    print(\"after\")",
            "}"));
    }

    [Fact]
    public void Try_passes_the_value_on_and_returns_the_error_otherwise()
    {
        Assert.Equal("9\nfailed: bad\n", Run(
            Types,
            "fn parse(ok: bool) -> ParseError!int {",
            "    if ok {",
            "        return 8",
            "    }",
            "    return error ParseError { message = \"bad\", line = 2 }",
            "}",
            "fn load(ok: bool) -> anyerror!int {",
            "    let n = try parse(ok)",
            "    return n + 1",
            "}",
            "fn main() {",
            "    print(load(true) catch 0)",
            "    load(false) catch e => {",
            "        print($\"failed: {e.message}\")",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void Try_keeps_the_same_error_type_when_the_function_returns_it()
    {
        Assert.Equal("line 5\n", Run(
            Types,
            "fn parse() -> ParseError!int {",
            "    return error ParseError { message = \"m\", line = 5 }",
            "}",
            "fn twice() -> ParseError!int {",
            "    let a = try parse()",
            "    return a * 2",
            "}",
            "fn main() {",
            "    match twice() {",
            "        n: int => print(n)",
            "        e: ParseError => print($\"line {e.line}\")",
            "    }",
            "}"));
    }

    [Fact]
    public void An_error_without_a_message_field_uses_its_type_name_as_the_anyerror_message()
    {
        Assert.Equal("Plain\n", Run(
            Types,
            "fn fail() -> anyerror!int {",
            "    return error Plain { code = 7 }",
            "}",
            "fn main() {",
            "    fail() catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void An_error_with_a_message_field_uses_it_as_the_anyerror_message()
    {
        Assert.Equal("empty\n", Run(
            Types,
            "fn fail() -> anyerror!int {",
            "    return error ParseError { message = \"empty\", line = 1 }",
            "}",
            "fn main() {",
            "    fail() catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void An_error_made_from_a_message()
    {
        Assert.Equal("path is empty\n", Run(
            "fn read(path: string) -> anyerror!string {",
            "    if path == \"\" {",
            "        return error \"path is empty\"",
            "    }",
            "    return path",
            "}",
            "fn main() {",
            "    read(\"\") catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void An_interpolated_message()
    {
        Assert.Equal("code 3\n", Run(
            "fn f(code: int) -> anyerror!int {",
            "    return error $\"code {code}\"",
            "}",
            "fn main() {",
            "    f(3) catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void A_function_with_no_value_succeeds_with_return_or_by_reaching_the_end()
    {
        Assert.Equal("a\nb\nc failed\n", Run(
            "fn a() -> anyerror! {",
            "    print(\"a\")",
            "}",
            "fn b(flag: bool) -> anyerror! {",
            "    if flag {",
            "        print(\"b\")",
            "        return",
            "    }",
            "    return error \"b failed\"",
            "}",
            "fn c() -> anyerror! {",
            "    return error \"c failed\"",
            "}",
            "fn main() {",
            "    a() catch e => print(e.message)",
            "    b(true) catch e => print(e.message)",
            "    c() catch e => print($\"c {e.message.Substring(2)}\")",
            "}"));
    }

    [Fact]
    public void Match_on_a_result_picks_the_side()
    {
        Assert.Equal("value 3\nerror x\n", Run(
            "fn f(ok: bool) -> anyerror!int {",
            "    if ok {",
            "        return 3",
            "    }",
            "    return error \"x\"",
            "}",
            "fn show(r: anyerror!int) {",
            "    match r {",
            "        n: int => print($\"value {n}\")",
            "        e: anyerror => print($\"error {e.message}\")",
            "    }",
            "}",
            "fn main() {",
            "    show(f(true))",
            "    show(f(false))",
            "}"));
    }

    [Fact]
    public void Results_work_inside_structs_tuples_and_unions()
    {
        Assert.Equal("5\nbad\n", Run(
            "type Holder = struct { r: anyerror!int }",
            "fn value(r: anyerror!int) -> int {",
            "    return r catch 0",
            "}",
            "fn main() {",
            "    let ok: anyerror!int = 5",
            "    let bad: anyerror!int = error \"bad\"",
            "    let h = Holder { r = ok }",
            "    print(value(h.r))",
            "    let pair = (bad, 1)",
            "    pair.0 catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void A_catch_value_can_be_used_inside_a_larger_expression()
    {
        Assert.Equal("111\n", Run(
            "fn f(ok: bool) -> anyerror!int {",
            "    if ok {",
            "        return 10",
            "    }",
            "    return error \"x\"",
            "}",
            "fn main() {",
            "    print(1 + (f(true) catch 0) * 11 + (f(false) catch e => 0))",
            "}"));
    }

    [Fact]
    public void Try_in_a_loop_returns_on_the_first_error()
    {
        Assert.Equal("1\n2\nstopped at 3\n", Run(
            "fn check(i: int) -> anyerror!int {",
            "    if i == 3 {",
            "        return error $\"stopped at {i}\"",
            "    }",
            "    return i",
            "}",
            "fn run() -> anyerror! {",
            "    for i in 1..10 {",
            "        let v = try check(i)",
            "        print(v)",
            "    }",
            "}",
            "fn main() {",
            "    run() catch e => print(e.message)",
            "}"));
    }

    [Fact]
    public void Try_works_inside_a_match_arm_and_in_a_method()
    {
        Assert.Equal("7\nbroken\n", Run(
            "type Reader = struct { fail: bool }",
            "Reader impl {",
            "    fn read(&self) -> anyerror!int {",
            "        if self.fail {",
            "            return error \"broken\"",
            "        }",
            "        return 7",
            "    }",
            "    fn twice(&self) -> anyerror!int {",
            "        let v = try self.read()",
            "        return v",
            "    }",
            "}",
            "fn main() {",
            "    print(Reader { fail = false }.twice() catch 0)",
            "    Reader { fail = true }.twice() catch e => {",
            "        print(e.message)",
            "        return",
            "    }",
            "}"));
    }

    [Fact]
    public void A_variable_assigned_from_try()
    {
        Assert.Equal("5\n", Run(
            "fn five() -> anyerror!int {",
            "    return 5",
            "}",
            "fn run() -> anyerror! {",
            "    let mut x = 0",
            "    x = try five()",
            "    print(x)",
            "}",
            "fn main() {",
            "    run() catch e => print(e.message)",
            "}"));
    }

    [Fact]
    public void Try_of_a_tuple_result_can_be_taken_apart()
    {
        Assert.Equal("3 4\n", Run(
            "fn pair() -> anyerror!(int, int) {",
            "    return (3, 4)",
            "}",
            "fn run() -> anyerror! {",
            "    let (a, b) = try pair()",
            "    print($\"{a} {b}\")",
            "}",
            "fn main() {",
            "    run() catch e => print(e.message)",
            "}"));
    }

    [Fact]
    public void A_main_that_returns_an_error_prints_it_to_stderr_and_exits_with_1()
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n",
            "fn fail() -> anyerror! {",
            "    return error \"it broke\"",
            "}",
            "fn main() -> anyerror! {",
            "    print(\"start\")",
            "    try fail()",
            "    print(\"never\")",
            "}") + "\n");

        Assert.Equal("start\n", stdout);
        Assert.Equal("error: it broke\n", stderr.ReplaceLineEndings("\n"));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void A_main_that_returns_a_declared_error_uses_its_message_field()
    {
        var (_, stderr, exitCode) = TestHost.Run(string.Join("\n",
            Types,
            "fn main() -> ParseError! {",
            "    return error ParseError { message = \"line too long\", line = 9 }",
            "}") + "\n");

        Assert.Equal("error: line too long\n", stderr.ReplaceLineEndings("\n"));
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void A_main_that_returns_success_exits_with_0()
    {
        var (stdout, stderr, exitCode) = TestHost.Run("fn main() -> anyerror! {\n    print(\"ok\")\n}\n");

        Assert.Equal("ok\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Error_values_are_shared_not_copied()
    {
        Assert.Equal("m\nm\n", Run(
            Types,
            "fn main() {",
            "    let a = error ParseError { message = \"m\", line = 1 }",
            "    let b = a",
            "    let r: anyerror!int = a",
            "    print(a.message)",
            "    print(b.message)",
            "}"));
    }

    [Fact]
    public void An_error_can_hold_a_struct_a_tuple_and_another_error()
    {
        Assert.Equal("2 5 x\n", Run(
            "type P = struct { x: int }",
            "type Inner = error { n: int }",
            "type Outer = error { message: string, p: P, t: (int, string), inner: Inner }",
            "fn make() -> Outer!int {",
            "    return error Outer { message = \"m\", p = P { x = 2 }, t = (5, \"x\"), inner = error Inner { n = 1 } }",
            "}",
            "fn main() {",
            "    make() catch e => {",
            "        print($\"{e.p.x} {e.t.0} {e.t.1}\")",
            "        return",
            "    }",
            "}"));
    }
}
