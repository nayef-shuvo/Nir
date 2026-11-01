using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class TupleTests
{
    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void A_literal_and_element_reads_of_every_type()
    {
        Assert.Equal("1\n2.5\nTrue\nhi\n", Run(
            "fn main() {",
            "    let t = (1, 2.5, true, \"hi\")",
            "    print(t.0)",
            "    print(t.1)",
            "    print(t.2)",
            "    print(t.3)",
            "}"));
    }

    [Fact]
    public void Seven_elements()
    {
        Assert.Equal("28\n", Run(
            "fn main() {",
            "    let t = (1, 2, 3, 4, 5, 6, 7)",
            "    print(t.0 + t.1 + t.2 + t.3 + t.4 + t.5 + t.6)",
            "}"));
    }

    [Fact]
    public void Elements_are_evaluated_left_to_right()
    {
        Assert.Equal("a\nb\nc\n1 2 3\n", Run(
            "fn say(s: string, n: int) -> int {",
            "    print(s)",
            "    return n",
            "}",
            "fn main() {",
            "    let t = (say(\"a\", 1), say(\"b\", 2), say(\"c\", 3))",
            "    print($\"{t.0} {t.1} {t.2}\")",
            "}"));
    }

    [Fact]
    public void A_tuple_is_a_result_and_is_taken_apart()
    {
        Assert.Equal("3\n2\n", Run(
            "fn divmod(a: int, b: int) -> (int, int) {",
            "    return (a / b, a % b)",
            "}",
            "fn main() {",
            "    let (q, r) = divmod(17, 5)",
            "    print(q)",
            "    print(r)",
            "}"));
    }

    [Fact]
    public void A_tuple_is_a_parameter_and_is_copied()
    {
        Assert.Equal("30\n1\n", Run(
            "fn bump(p: (int, int)) -> int {",
            "    let mut q = p",
            "    q.0 = q.0 + 29",
            "    return q.0",
            "}",
            "fn main() {",
            "    let t = (1, 2)",
            "    print(bump(t))",
            "    print(t.0)",
            "}"));
    }

    [Fact]
    public void Assigning_a_tuple_copies_it()
    {
        Assert.Equal("1\n9\n", Run(
            "fn main() {",
            "    let mut a = (1, 2)",
            "    let b = a",
            "    a.0 = 9",
            "    print(b.0)",
            "    print(a.0)",
            "}"));
    }

    [Fact]
    public void A_whole_tuple_variable_can_be_replaced()
    {
        Assert.Equal("3\n4\n", Run(
            "fn main() {",
            "    let mut t = (1, 2)",
            "    t = (3, 4)",
            "    print(t.0)",
            "    print(t.1)",
            "}"));
    }

    [Fact]
    public void Nested_tuples_read_and_write()
    {
        Assert.Equal("2\n7\ns\n", Run(
            "fn main() {",
            "    let mut t = ((1, 2), \"s\")",
            "    print(t.0.1)",
            "    t.0.1 = 7",
            "    print(t.0.1)",
            "    print(t.1)",
            "}"));
    }

    [Fact]
    public void Named_tuples_by_name_and_index()
    {
        Assert.Equal("1\n2\n2\n", Run(
            "type Pair = (x: int, y: int)",
            "fn main() {",
            "    let mut p: Pair = (1, 2)",
            "    print(p.x)",
            "    print(p.y)",
            "    p.y = p.1",
            "    print(p.1)",
            "}"));
    }

    [Fact]
    public void A_named_result_type_written_inline()
    {
        Assert.Equal("3 1\n", Run(
            "fn divmod(a: int, b: int) -> (quotient: int, remainder: int) {",
            "    return (a / b, a % b)",
            "}",
            "fn main() {",
            "    let r = divmod(7, 2)",
            "    print($\"{r.quotient} {r.remainder}\")",
            "}"));
    }

    [Fact]
    public void A_struct_with_a_tuple_field()
    {
        Assert.Equal("11\n5\n", Run(
            "type S = struct { pos: (int, int), name: string }",
            "S impl {",
            "    fn shift(&mut self) {",
            "        self.pos.0 = self.pos.0 + 10",
            "    }",
            "}",
            "fn main() {",
            "    let mut s = S { pos = (1, 5), name = \"n\" }",
            "    s.shift()",
            "    print(s.pos.0)",
            "    print(s.pos.1)",
            "}"));
    }

    [Fact]
    public void A_tuple_holding_structs()
    {
        Assert.Equal("1\n20\n", Run(
            "type P = struct { n: int }",
            "fn main() {",
            "    let mut t = (P { n = 1 }, P { n = 2 })",
            "    print(t.0.n)",
            "    t.1.n = 20",
            "    print(t.1.n)",
            "}"));
    }

    [Fact]
    public void A_tuple_of_structs_as_a_parameter_and_result()
    {
        Assert.Equal("2 1\n", Run(
            "type P = struct { n: int }",
            "fn swap(t: (P, P)) -> (P, P) {",
            "    return (t.1, t.0)",
            "}",
            "fn main() {",
            "    let (a, b) = swap((P { n = 1 }, P { n = 2 }))",
            "    print($\"{a.n} {b.n}\")",
            "}"));
    }

    [Fact]
    public void A_method_can_return_and_take_a_tuple()
    {
        Assert.Equal("5\n6\n", Run(
            "type P = struct { x: int, y: int }",
            "P impl {",
            "    fn pair(&self) -> (int, int) {",
            "        return (self.x, self.y)",
            "    }",
            "    fn shifted(&self, by: (int, int)) -> Self {",
            "        return Self { x = self.x + by.0, y = self.y + by.1 }",
            "    }",
            "}",
            "fn main() {",
            "    let p = P { x = 1, y = 2 }",
            "    let q = p.shifted((4, 4))",
            "    let (a, b) = q.pair()",
            "    print(a)",
            "    print(b)",
            "}"));
    }

    [Fact]
    public void Tuples_in_a_loop()
    {
        Assert.Equal("0 0\n1 1\n2 4\n", Run(
            "fn main() {",
            "    for i in 0..3 {",
            "        let t = (i, i * i)",
            "        print($\"{t.0} {t.1}\")",
            "    }",
            "}"));
    }

    [Fact]
    public void Destructuring_with_mut_and_discard()
    {
        Assert.Equal("11\n", Run(
            "fn main() {",
            "    let (mut a, _, c) = (1, 2, 3)",
            "    a = a + 7",
            "    print(a + c - 0)",
            "}"));
    }

    [Fact]
    public void Destructuring_a_nested_tuple_in_two_steps()
    {
        Assert.Equal("1 2 s\n", Run(
            "fn main() {",
            "    let (inner, s) = ((1, 2), \"s\")",
            "    let (a, b) = inner",
            "    print($\"{a} {b} {s}\")",
            "}"));
    }

    [Fact]
    public void A_range_can_use_tuple_elements()
    {
        Assert.Equal("2\n3\n", Run(
            "fn main() {",
            "    let t = (2, 4)",
            "    for i in t.0..t.1 {",
            "        print(i)",
            "    }",
            "}"));
    }

    [Fact]
    public void A_tuple_element_can_be_passed_to_a_dotnet_method()
    {
        Assert.Equal("HI\n", Run(
            "fn main() {",
            "    let t = (\"hi\", 1)",
            "    print(t.0.ToUpper())",
            "}"));
    }

    [Fact]
    public void A_borrow_parameter_still_works_next_to_tuples()
    {
        Assert.Equal("2\n", Run(
            "type C = struct { n: int }",
            "fn bump(c: &mut C, by: (int, int)) {",
            "    c.n = c.n + by.0 + by.1",
            "}",
            "fn main() {",
            "    let mut c = C { n = 0 }",
            "    bump(&mut c, (1, 1))",
            "    print(c.n)",
            "}"));
    }

    [Fact]
    public void An_inline_tuple_index_chain_on_a_temporary()
    {
        Assert.Equal("2\n", Run(
            "fn main() {",
            "    print(((1, 2), 3).0.1)",
            "}"));
    }

    [Fact]
    public void A_condition_can_hold_a_parenthesised_tuple_literal()
    {
        Assert.Equal("yes\n", Run(
            "fn main() {",
            "    if (1, 2).1 == 2 {",
            "        print(\"yes\")",
            "    }",
            "}"));
    }

    [Fact]
    public void Tuples_and_structs_nest_in_every_order()
    {
        Assert.Equal("5\n9\nz\n", Run(
            "type P = struct { n: int }",
            "type Mixed = struct { t: ((int, int), P, string) }",
            "fn make() -> ((P, int), string) {",
            "    return ((P { n = 4 }, 5), \"z\")",
            "}",
            "fn main() {",
            "    let m = Mixed { t = ((1, 2), P { n = 3 }, \"s\") }",
            "    print(m.t.0.1 + m.t.1.n)",
            "    let (inner, s) = make()",
            "    let (p, k) = inner",
            "    print(p.n + k)",
            "    print(s)",
            "}"));
    }

    [Fact]
    public void A_tuple_past_seven_elements_is_nested_and_every_element_is_reachable()
    {
        Assert.Equal("1\n8\n9\n15\n", Run(
            "fn main() {",
            "    let t = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)",
            "    print(t.0)",
            "    print(t.7)",
            "    print(t.8)",
            "    print(t.14)",
            "}"));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(30)]
    public void Every_element_of_every_size_reads_back_what_was_written(int size)
    {
        var values = string.Join(", ", Enumerable.Range(0, size).Select(i => (i * 3).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var reads = Enumerable.Range(0, size).Select(i => $"    print(t.{i})");
        var expected = string.Concat(Enumerable.Range(0, size).Select(i => $"{i * 3}\n"));

        Assert.Equal(expected, Run(["fn main() {", $"    let t = ({values})", .. reads, "}"]));
    }

    [Fact]
    public void Elements_past_the_seventh_can_be_assigned_and_the_tuple_replaced()
    {
        Assert.Equal("80\n90\n1\n2\n", Run(
            "fn main() {",
            "    let mut t = (1, 2, 3, 4, 5, 6, 7, 8, 9)",
            "    t.7 = 80",
            "    t.8 = t.7 + 10",
            "    print(t.7)",
            "    print(t.8)",
            "    t = (1, 2, 3, 4, 5, 6, 7, 8, 9)",
            "    print(t.0)",
            "    print(t.1)",
            "}"));
    }

    [Fact]
    public void A_long_tuple_is_copied_whole()
    {
        Assert.Equal("8\n800\n", Run(
            "fn main() {",
            "    let mut a = (1, 2, 3, 4, 5, 6, 7, 8)",
            "    let b = a",
            "    a.7 = 800",
            "    print(b.7)",
            "    print(a.7)",
            "}"));
    }

    [Fact]
    public void A_long_tuple_is_a_parameter_and_a_result_and_can_be_taken_apart()
    {
        Assert.Equal("1 8 9\n", Run(
            "fn tail(t: (int, int, int, int, int, int, int, int, int)) -> (int, int, int, int, int, int, int, int, int) {",
            "    return (t.8, t.7, t.6, t.5, t.4, t.3, t.2, t.1, t.0)",
            "}",
            "fn main() {",
            "    let (a, _, _, _, _, _, _, h, i) = tail((9, 8, 7, 6, 5, 4, 3, 2, 1))",
            "    print($\"{a} {h} {i}\")",
            "}"));
    }

    [Fact]
    public void A_long_named_tuple_is_reached_by_name_past_the_seventh()
    {
        Assert.Equal("10\n9\n", Run(
            "type Big = (a: int, b: int, c: int, d: int, e: int, f: int, g: int, h: int, i: int, j: int)",
            "fn main() {",
            "    let big: Big = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)",
            "    print(big.j)",
            "    print(big.i)",
            "}"));
    }

    [Fact]
    public void Structs_and_other_tuples_inside_the_nested_part()
    {
        Assert.Equal("88\ntail\n3.5\n4\n", Run(
            "type P = struct { n: int }",
            "type S = struct { big: (int, int, int, int, int, int, int, P, string, double) }",
            "fn main() {",
            "    let mut s = S { big = (1, 2, 3, 4, 5, 6, 7, P { n = 8 }, \"tail\", 3.5) }",
            "    s.big.7.n = 88",
            "    print(s.big.7.n)",
            "    print(s.big.8)",
            "    print(s.big.9)",
            "    let nested = ((1, 2, 3, 4, 5, 6, 7, 8), 9)",
            "    print(nested.0.3)",
            "}"));
    }

    [Fact]
    public void Elements_of_a_long_tuple_run_left_to_right()
    {
        Assert.Equal("1\n2\n3\n4\n5\n6\n7\n8\n9\n", Run(
            "fn say(n: int) -> int {",
            "    print(n)",
            "    return n",
            "}",
            "fn main() {",
            "    let t = (say(1), say(2), say(3), say(4), say(5), say(6), say(7), say(8), say(9))",
            "}"));
    }
}
