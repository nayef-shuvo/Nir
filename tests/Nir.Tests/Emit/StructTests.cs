using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class StructTests
{
    private const string Point = "type Point = struct { x: int, y: int }";

    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void A_literal_and_field_reads()
    {
        Assert.Equal("1\n2\n3\n", Run(
            Point,
            "fn main() {",
            "    let p = Point { x = 1, y = 2 }",
            "    print(p.x)",
            "    print(p.y)",
            "    print(p.x + p.y)",
            "}"));
    }

    [Fact]
    public void Fields_can_be_given_in_any_order()
    {
        Assert.Equal("1 2\n", Run(
            Point,
            "fn main() {",
            "    let p = Point { y = 2, x = 1 }",
            "    print($\"{p.x} {p.y}\")",
            "}"));
    }

    [Fact]
    public void Assigning_a_struct_copies_it()
    {
        Assert.Equal("5 9\n1 2\n", Run(
            Point,
            "fn main() {",
            "    let mut p = Point { x = 1, y = 2 }",
            "    let q = p",
            "    p.x = 5",
            "    p.y = 9",
            "    print($\"{p.x} {p.y}\")",
            "    print($\"{q.x} {q.y}\")",
            "}"));
    }

    [Fact]
    public void Passing_and_returning_a_struct_copies_it()
    {
        Assert.Equal("11 2\n1 2\n", Run(
            Point,
            "fn shifted(p: Point, dx: int) -> Point {",
            "    let mut q = p",
            "    q.x = q.x + dx",
            "    return q",
            "}",
            "fn main() {",
            "    let a = Point { x = 1, y = 2 }",
            "    let b = shifted(a, 10)",
            "    print($\"{b.x} {b.y}\")",
            "    print($\"{a.x} {a.y}\")",
            "}"));
    }

    [Fact]
    public void Nested_structs_read_and_write()
    {
        Assert.Equal("1 4\n30 2\n", Run(
            Point,
            "type Line = struct {",
            "    from: Point",
            "    to: Point",
            "}",
            "fn main() {",
            "    let mut line = Line { from = Point { x = 1, y = 2 }, to = Point { x = 3, y = 4 } }",
            "    print($\"{line.from.x} {line.to.y}\")",
            "    line.to.x = 30",
            "    print($\"{line.to.x} {line.from.y}\")",
            "}"));
    }

    [Fact]
    public void A_nested_struct_is_copied_with_its_parent()
    {
        Assert.Equal("1\n99\n", Run(
            Point,
            "type Line = struct { from: Point, to: Point }",
            "fn main() {",
            "    let mut a = Line { from = Point { x = 1, y = 0 }, to = Point { x = 0, y = 0 } }",
            "    let b = a",
            "    a.from.x = 99",
            "    print(b.from.x)",
            "    print(a.from.x)",
            "}"));
    }

    [Fact]
    public void Fields_of_every_type()
    {
        Assert.Equal("7|2.5|True|text\n8|3.5|False|more\n", Run(
            "type Mixed = struct {",
            "    i: int",
            "    d: double",
            "    b: bool",
            "    s: string",
            "}",
            "fn main() {",
            "    let mut m = Mixed { i = 7, d = 2.5, b = true, s = \"text\" }",
            "    print($\"{m.i}|{m.d}|{m.b}|{m.s}\")",
            "    m.i = m.i + 1",
            "    m.d = m.d + 1.0",
            "    m.b = !m.b",
            "    m.s = \"more\"",
            "    print($\"{m.i}|{m.d}|{m.b}|{m.s}\")",
            "}"));
    }

    [Fact]
    public void A_field_can_be_read_from_a_call_result()
    {
        Assert.Equal("14\n7\n", Run(
            Point,
            "fn make(a: int) -> Point {",
            "    return Point { x = a, y = a * 2 }",
            "}",
            "fn main() {",
            "    print(make(7).y)",
            "    print(make(7).x)",
            "}"));
    }

    [Fact]
    public void Field_initializers_run_in_the_order_written()
    {
        Assert.Equal("first\nsecond\n3 1\n", Run(
            Point,
            "fn one() -> int {",
            "    print(\"first\")",
            "    return 1",
            "}",
            "fn two() -> int {",
            "    print(\"second\")",
            "    return 3",
            "}",
            "fn main() {",
            "    let p = Point { y = one(), x = two() }",
            "    print($\"{p.x} {p.y}\")",
            "}"));
    }

    [Fact]
    public void An_empty_struct_works()
    {
        Assert.Equal("ok\n", Run(
            "type Nothing = struct {}",
            "fn make() -> Nothing {",
            "    return Nothing {}",
            "}",
            "fn main() {",
            "    let n = make()",
            "    let m = n",
            "    print(\"ok\")",
            "}"));
    }

    [Fact]
    public void A_struct_may_be_declared_after_its_use()
    {
        Assert.Equal("6\n", Run(
            "fn main() {",
            "    let l = Line { a = Pt { v = 2 }, b = Pt { v = 4 } }",
            "    print(l.a.v + l.b.v)",
            "}",
            "type Line = struct { a: Pt, b: Pt }",
            "type Pt = struct { v: int }"));
    }

    [Fact]
    public void Structs_in_loops_accumulate()
    {
        Assert.Equal("45 90\n", Run(
            Point,
            "fn main() {",
            "    let mut total = Point { x = 0, y = 0 }",
            "    for i in 0..10 {",
            "        total.x = total.x + i",
            "        total.y = total.y + i * 2",
            "    }",
            "    print($\"{total.x} {total.y}\")",
            "}"));
    }

    [Fact]
    public void A_struct_made_inside_a_loop_is_fresh_each_time()
    {
        Assert.Equal("0\n1\n2\n", Run(
            Point,
            "fn main() {",
            "    for i in 0..3 {",
            "        let p = Point { x = i, y = 0 }",
            "        print(p.x)",
            "    }",
            "}"));
    }

    [Fact]
    public void A_parameter_struct_is_read_through_its_fields()
    {
        Assert.Equal("5\n", Run(
            Point,
            "fn sum(p: Point) -> int {",
            "    return p.x + p.y",
            "}",
            "fn main() {",
            "    print(sum(Point { x = 2, y = 3 }))",
            "}"));
    }

    [Fact]
    public void Struct_values_work_as_conditions_arguments_and_expressions()
    {
        Assert.Equal("big\n", Run(
            Point,
            "fn larger(a: Point, b: Point) -> Point {",
            "    if a.x > b.x {",
            "        return a",
            "    }",
            "    return b",
            "}",
            "fn main() {",
            "    let winner = larger(Point { x = 1, y = 0 }, Point { x = 8, y = 0 })",
            "    if winner.x > 5 {",
            "        print(\"big\")",
            "    }",
            "}"));
    }

    [Fact]
    public void The_struct_literal_rule_in_heads_works_when_run()
    {
        Assert.Equal("3\n", Run(
            Point,
            "fn main() {",
            "    let p = Point { x = 3, y = 0 }",
            "    let mut n = 0",
            "    let limit = p.x",
            "    while n != limit { n = n + 1 }",
            "    print(n)",
            "}"));
    }

    [Fact]
    public void The_compiled_struct_is_a_real_clr_value_type_with_public_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nir-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = TestHost.Compile(Point + "\nfn main() {\n}\n");
            Nir.Compiler.NirCompiler.Emit(result, "program", directory);

            using var stream = File.OpenRead(Path.Combine(directory, "program.dll"));
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var type = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t => reader.GetString(t.Name) == "Point");

            Assert.True((type.Attributes & System.Reflection.TypeAttributes.Sealed) != 0);
            Assert.True((type.Attributes & System.Reflection.TypeAttributes.Public) != 0);
            var baseType = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)type.BaseType);
            Assert.Equal("ValueType", reader.GetString(baseType.Name));
            Assert.Equal(["x", "y"], type.GetFields().Select(f => reader.GetString(reader.GetFieldDefinition(f).Name)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
