using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class MethodTests
{
    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    private const string Counter =
        "type Counter = struct { n: int }\n" +
        "Counter impl {\n" +
        "    fn new() -> Self {\n        return Self { n = 0 }\n    }\n" +
        "    fn inc(&mut self) {\n        self.n = self.n + 1\n    }\n" +
        "    fn get(&self) -> int {\n        return self.n\n    }\n" +
        "}";

    [Fact]
    public void The_example_from_the_design()
    {
        Assert.Equal("32\n6\n6\n2\n", Run(
            "type Point = struct {",
            "    x: int,",
            "    y: int,",
            "}",
            "Point impl {",
            "    fn new(x: int, y: int) -> Self {",
            "        return Self { x = x, y = y }",
            "    }",
            "    fn length_squared(&self) -> int {",
            "        return self.x * self.x + self.y * self.y",
            "    }",
            "    fn move_right(&mut self) {",
            "        self.x = self.x + 1",
            "    }",
            "    fn twice(&mut self) {",
            "        self.move_right()",
            "        self.move_right()",
            "    }",
            "    fn copy_of(&self) -> Self {",
            "        return self",
            "    }",
            "}",
            "fn main() {",
            "    let mut p = Point::new(3, 4)",
            "    p.move_right()",
            "    print(p.length_squared())",
            "    p.twice()",
            "    print(p.x)",
            "    let q = p.copy_of()",
            "    p.x = 100",
            "    print(q.x)",
            "    print(Point::new(1, 1).length_squared())",
            "}"));
    }

    [Fact]
    public void A_mutating_method_changes_the_callers_variable_in_place()
    {
        Assert.Equal("0\n3\n", Run(
            Counter,
            "fn main() {",
            "    let mut c = Counter::new()",
            "    print(c.get())",
            "    c.inc()",
            "    c.inc()",
            "    c.inc()",
            "    print(c.get())",
            "}"));
    }

    [Fact]
    public void A_copy_of_a_struct_does_not_share_the_changes_of_later_method_calls()
    {
        Assert.Equal("0\n2\n", Run(
            Counter,
            "fn main() {",
            "    let mut a = Counter::new()",
            "    let b = a",
            "    a.inc()",
            "    a.inc()",
            "    print(b.get())",
            "    print(a.get())",
            "}"));
    }

    [Fact]
    public void Methods_work_in_loops()
    {
        Assert.Equal("10\n", Run(
            Counter,
            "fn main() {",
            "    let mut c = Counter::new()",
            "    for i in 0..10 {",
            "        c.inc()",
            "    }",
            "    print(c.get())",
            "}"));
    }

    [Fact]
    public void A_method_on_a_nested_field_changes_that_field_only()
    {
        Assert.Equal("1 0\n", Run(
            Counter,
            "type Pair = struct { a: Counter, b: Counter }",
            "fn main() {",
            "    let mut p = Pair { a = Counter::new(), b = Counter::new() }",
            "    p.a.inc()",
            "    print($\"{p.a.get()} {p.b.get()}\")",
            "}"));
    }

    [Fact]
    public void A_reading_method_works_on_a_temporary_and_a_chain_of_calls()
    {
        Assert.Equal("2\n2\n", Run(
            "type P = struct { x: int, y: int }",
            "P impl {",
            "    fn new(x: int, y: int) -> Self {",
            "        return Self { x = x, y = y }",
            "    }",
            "    fn sum(&self) -> int {",
            "        return self.x + self.y",
            "    }",
            "    fn swapped(&self) -> Self {",
            "        return Self { x = self.y, y = self.x }",
            "    }",
            "}",
            "fn main() {",
            "    print(P::new(1, 1).sum())",
            "    print(P::new(1, 1).swapped().swapped().sum())",
            "}"));
    }

    [Fact]
    public void Methods_take_parameters_of_every_type()
    {
        Assert.Equal("x=3,y=2.5,flag=True,name=ok\n", Run(
            "type Box = struct { n: int }",
            "Box impl {",
            "    fn describe(&self, y: double, flag: bool, name: string) -> string {",
            "        return $\"x={self.n},y={y},flag={flag},name={name}\"",
            "    }",
            "}",
            "fn main() {",
            "    let b = Box { n = 3 }",
            "    print(b.describe(2.5, true, \"ok\"))",
            "}"));
    }

    [Fact]
    public void A_method_can_take_and_return_other_structs()
    {
        Assert.Equal("4 6\n", Run(
            "type V = struct { x: int, y: int }",
            "V impl {",
            "    fn plus(&self, other: V) -> V {",
            "        return V { x = self.x + other.x, y = self.y + other.y }",
            "    }",
            "}",
            "fn main() {",
            "    let a = V { x = 1, y = 2 }",
            "    let b = V { x = 3, y = 4 }",
            "    let c = a.plus(b)",
            "    print($\"{c.x} {c.y}\")",
            "}"));
    }

    [Fact]
    public void Methods_call_each_other_and_free_functions()
    {
        Assert.Equal("14\n", Run(
            "fn twice(n: int) -> int {",
            "    return n * 2",
            "}",
            "type P = struct { n: int }",
            "P impl {",
            "    fn base(&self) -> int {",
            "        return twice(self.n)",
            "    }",
            "    fn total(&self) -> int {",
            "        return self.base() + 4",
            "    }",
            "}",
            "fn main() {",
            "    let p = P { n = 5 }",
            "    print(p.total())",
            "}"));
    }

    [Fact]
    public void Methods_on_a_by_value_parameter_and_on_a_returned_struct()
    {
        Assert.Equal("5\n5\n", Run(
            Counter,
            "fn count_of(c: Counter) -> int {",
            "    return c.get()",
            "}",
            "fn make(n: int) -> Counter {",
            "    let mut c = Counter::new()",
            "    for i in 0..n {",
            "        c.inc()",
            "    }",
            "    return c",
            "}",
            "fn main() {",
            "    print(count_of(make(5)))",
            "    print(make(5).get())",
            "}"));
    }

    [Fact]
    public void Self_is_the_struct_type_inside_the_impl_and_works_in_literals_and_parameters()
    {
        Assert.Equal("3\n", Run(
            "type N = struct { v: int }",
            "N impl {",
            "    fn of(v: int) -> Self {",
            "        return Self { v = v }",
            "    }",
            "    fn merge(a: Self, b: Self) -> Self {",
            "        return Self::of(a.v + b.v)",
            "    }",
            "}",
            "fn main() {",
            "    print(N::merge(N::of(1), N::of(2)).v)",
            "}"));
    }

    [Fact]
    public void A_method_may_share_its_name_with_a_field()
    {
        Assert.Equal("1\n1\n", Run(
            "type P = struct { n: int }",
            "P impl {",
            "    fn n(&self) -> int {",
            "        return self.n",
            "    }",
            "}",
            "fn main() {",
            "    let p = P { n = 1 }",
            "    print(p.n)",
            "    print(p.n())",
            "}"));
    }

    [Fact]
    public void Early_return_and_loops_inside_a_method()
    {
        Assert.Equal("False\nTrue\n", Run(
            "type Range = struct { lo: int, hi: int }",
            "Range impl {",
            "    fn contains(&self, n: int) -> bool {",
            "        for i in self.lo..=self.hi {",
            "            if i == n {",
            "                return true",
            "            }",
            "        }",
            "        return false",
            "    }",
            "}",
            "fn main() {",
            "    let r = Range { lo = 2, hi = 5 }",
            "    print(r.contains(9))",
            "    print(r.contains(4))",
            "}"));
    }

    [Fact]
    public void Methods_are_real_clr_methods_on_the_struct_type()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nir-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = TestHost.Compile(Counter + "\nfn main() {\n}\n");
            Nir.Compiler.NirCompiler.Emit(result, "program", directory);

            using var stream = File.OpenRead(Path.Combine(directory, "program.dll"));
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var type = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t => reader.GetString(t.Name) == "Counter");
            var methods = type.GetMethods()
                .Select(reader.GetMethodDefinition)
                .ToDictionary(m => reader.GetString(m.Name), m => (m.Attributes & System.Reflection.MethodAttributes.Static) != 0);

            Assert.False(methods["inc"]);   // instance
            Assert.False(methods["get"]);   // instance
            Assert.True(methods["new"]);    // associated function: static
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
