using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class UnionTests
{
    private const string Shapes =
        "type Circle = struct { r: double }\n" +
        "type Square = struct { side: double }\n" +
        "type Shape = union { Circle, Square }";

    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void A_match_picks_the_arm_of_the_current_member()
    {
        Assert.Equal("12\n9\n", Run(
            Shapes,
            "fn area(s: Shape) -> double {",
            "    return match s {",
            "        c: Circle => 3.0 * c.r * c.r,",
            "        q: Square => q.side * q.side,",
            "    }",
            "}",
            "fn main() {",
            "    print(area(Circle { r = 2.0 }))",
            "    print(area(Square { side = 3.0 }))",
            "}"));
    }

    [Fact]
    public void Primitives_are_members_without_boxing_errors()
    {
        Assert.Equal("int 5\nstring hi\nbool True\ndouble 2.5\n", Run(
            "type V = union { int, string, bool, double }",
            "fn show(v: V) -> string {",
            "    return match v {",
            "        n: int => $\"int {n}\",",
            "        s: string => \"string \" + s,",
            "        b: bool => $\"bool {b}\",",
            "        d: double => $\"double {d}\",",
            "    }",
            "}",
            "fn main() {",
            "    print(show(5))",
            "    print(show(\"hi\"))",
            "    print(show(true))",
            "    print(show(2.5))",
            "}"));
    }

    [Fact]
    public void A_tuple_is_a_member()
    {
        Assert.Equal("3\n7\n", Run(
            "type V = union { (int, int), int }",
            "fn total(v: V) -> int {",
            "    return match v { p: (int, int) => p.0 + p.1, n: int => n }",
            "}",
            "fn main() {",
            "    print(total((1, 2)))",
            "    print(total(7))",
            "}"));
    }

    [Fact]
    public void Arms_may_be_in_any_order()
    {
        Assert.Equal("c\ns\n", Run(
            Shapes,
            "fn name(s: Shape) -> string {",
            "    return match s { q: Square => \"s\", c: Circle => \"c\" }",
            "}",
            "fn main() {",
            "    print(name(Circle { r = 1.0 }))",
            "    print(name(Square { side = 1.0 }))",
            "}"));
    }

    [Fact]
    public void A_statement_match_with_blocks_and_mixed_arms()
    {
        Assert.Equal("circle 2\nsquare\n", Run(
            Shapes,
            "fn say(s: Shape) {",
            "    match s {",
            "        c: Circle => {",
            "            let twice = c.r * 2.0",
            "            print($\"circle {c.r}\")",
            "        }",
            "        _: Square => print(\"square\")",
            "    }",
            "}",
            "fn main() {",
            "    say(Circle { r = 2.0 })",
            "    say(Square { side = 1.0 })",
            "}"));
    }

    [Fact]
    public void A_function_can_end_in_a_match_whose_arms_all_return()
    {
        Assert.Equal("10\n-1\n", Run(
            "type V = union { int, string }",
            "fn f(v: V) -> int {",
            "    match v {",
            "        n: int => { return n * 2 }",
            "        _: string => { return 0 - 1 }",
            "    }",
            "}",
            "fn main() {",
            "    print(f(5))",
            "    print(f(\"x\"))",
            "}"));
    }

    [Fact]
    public void A_union_is_assigned_returned_and_stored_in_a_struct_field()
    {
        Assert.Equal("1\n2\n3\n4\n", Run(
            "type V = union { int, string }",
            "type Box = struct { v: V }",
            "fn num(v: V) -> int {",
            "    return match v { n: int => n, s: string => 0 }",
            "}",
            "fn make() -> V {",
            "    return 3",
            "}",
            "fn main() {",
            "    let mut v: V = 1",
            "    print(num(v))",
            "    v = 2",
            "    print(num(v))",
            "    print(num(make()))",
            "    let mut b = Box { v = \"s\" }",
            "    b.v = 4",
            "    print(num(b.v))",
            "}"));
    }

    [Fact]
    public void A_union_is_copied_like_a_struct()
    {
        Assert.Equal("1\n2\n", Run(
            "type V = union { int, string }",
            "fn num(v: V) -> int {",
            "    return match v { n: int => n, s: string => 0 }",
            "}",
            "fn main() {",
            "    let mut a: V = 1",
            "    let b = a",
            "    a = 2",
            "    print(num(b))",
            "    print(num(a))",
            "}"));
    }

    [Fact]
    public void A_match_on_a_member_that_holds_a_struct_reads_its_fields()
    {
        Assert.Equal("5\n", Run(
            "type P = struct { x: int, y: int }",
            "type V = union { P, string }",
            "fn main() {",
            "    let v: V = P { x = 2, y = 3 }",
            "    match v {",
            "        p: P => print(p.x + p.y)",
            "        s: string => print(s)",
            "    }",
            "}"));
    }

    [Fact]
    public void A_match_can_sit_inside_loops_with_break_and_continue()
    {
        Assert.Equal("1\n3\n", Run(
            "type V = union { int, string }",
            "fn pick(i: int) -> V {",
            "    if i == 2 {",
            "        return \"skip\"",
            "    }",
            "    return i",
            "}",
            "fn main() {",
            "    for i in 1..6 {",
            "        match pick(i) {",
            "            n: int => {",
            "                if n > 3 {",
            "                    break",
            "                }",
            "                print(n)",
            "            }",
            "            _: string => { continue }",
            "        }",
            "    }",
            "}"));
    }

    [Fact]
    public void A_value_match_inside_an_expression_with_things_on_the_stack()
    {
        Assert.Equal("111\n", Run(
            "type V = union { int, string }",
            "fn main() {",
            "    let v: V = 10",
            "    let n = 1 + (match v { i: int => i, s: string => 0 }) * 11",
            "    print(n)",
            "}"));
    }

    [Fact]
    public void Matches_nest()
    {
        Assert.Equal("a1\nb\n", Run(
            "type A = union { int, string }",
            "type B = union { A2, bool }",
            "type A2 = struct { a: A }",
            "fn show(b: B) -> string {",
            "    return match b {",
            "        x: A2 => match x.a { n: int => $\"a{n}\", s: string => s },",
            "        _: bool => \"b\",",
            "    }",
            "}",
            "fn main() {",
            "    print(show(A2 { a = 1 }))",
            "    print(show(true))",
            "}"));
    }

    [Fact]
    public void A_union_inside_a_tuple_and_a_tuple_of_unions_as_a_result()
    {
        Assert.Equal("1 x\n", Run(
            "type V = union { int, string }",
            "fn pair() -> (V, V) {",
            "    let a: V = 1",
            "    let b: V = \"x\"",
            "    return (a, b)",
            "}",
            "fn text(v: V) -> string {",
            "    return match v { n: int => $\"{n}\", s: string => s }",
            "}",
            "fn main() {",
            "    let (a, b) = pair()",
            "    print($\"{text(a)} {text(b)}\")",
            "}"));
    }

    [Fact]
    public void A_union_member_called_on_a_method_receiver()
    {
        Assert.Equal("7\n", Run(
            "type V = union { int, string }",
            "type S = struct { n: int }",
            "S impl {",
            "    fn plus(&self, v: V) -> int {",
            "        return self.n + match v { i: int => i, s: string => 0 }",
            "    }",
            "}",
            "fn main() {",
            "    let s = S { n = 3 }",
            "    print(s.plus(4))",
            "}"));
    }

    [Fact]
    public void The_dropped_variable_still_runs_the_arm()
    {
        Assert.Equal("int\n", Run(
            "type V = union { int, string }",
            "fn main() {",
            "    let v: V = 1",
            "    match v {",
            "        _: int => print(\"int\")",
            "        _: string => print(\"string\")",
            "    }",
            "}"));
    }
}
