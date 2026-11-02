using Nir.Tests.Support;

namespace Nir.Tests.Emit;

/// <summary>Slice 9d end to end: <c>typeof</c> compiled and run.</summary>
public class TypeofTests
{
    private static string Run(string setup, params string[] body)
    {
        var source = (setup.Length == 0 ? string.Empty : setup + "\n") + string.Join("\n", ["fn main() {", .. body.Select(line => "    " + line), "}"]) + "\n";
        var (stdout, stderr, exitCode) = TestHost.Run(source);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    [Fact]
    public void It_prints_the_name_of_every_primitive_type()
    {
        var output = Run(
            string.Empty,
            "print(typeof(1_byte).name)",
            "print(typeof(1_sbyte).name)",
            "print(typeof(1_short).name)",
            "print(typeof(1_ushort).name)",
            "print(typeof(1).name)",
            "print(typeof(1_uint).name)",
            "print(typeof(1_long).name)",
            "print(typeof(1_ulong).name)",
            "print(typeof(1_float).name)",
            "print(typeof(1.5).name)",
            "print(typeof(1.5_decimal).name)",
            "print(typeof('c').name)",
            "print(typeof(true).name)",
            "print(typeof(\"s\").name)");

        Assert.Equal("byte\nsbyte\nshort\nushort\nint\nuint\nlong\nulong\nfloat\ndouble\ndecimal\nchar\nbool\nstring\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void It_works_on_variables_and_the_result_can_be_kept_and_passed_around()
    {
        var output = Run(
            "using std::types::TypeInfo\nfn describe(t: TypeInfo) -> string { return $\"is {t.name}\" }",
            "let total = 5_long",
            "let info = typeof(total)",
            "print(info.name)",
            "print(describe(info))",
            "print(describe(typeof(\"x\")))");

        Assert.Equal("long\nis long\nis string\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void It_does_not_run_its_operand()
    {
        var output = Run(
            "fn side() -> int {\n    print(\"ran\")\n    return 1\n}",
            "let t = typeof(side())",
            "print(t.name)",
            "print(typeof(side() + 1).name)");

        Assert.Equal("int\nint\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void It_names_structs_tuples_unions_results_and_errors()
    {
        var output = Run(
            "type Point = struct { x: int, y: int }\ntype Shape = union { int, string }\ntype Failure = error { message: string }",
            "let p = Point { x = 1, y = 2 }",
            "let pair = (1, \"a\")",
            "let s: Shape = 5",
            "let r: Failure!int = 5",
            "let e: anyerror = error \"boom\"",
            "print(typeof(p).name)",
            "print(typeof(pair).name)",
            "print(typeof(s).name)",
            "print(typeof(r).name)",
            "print(typeof(e).name)",
            "print(typeof(Failure).name)");

        Assert.Equal("Point\n(int, string)\nShape\nFailure!int\nanyerror\nFailure\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_union_variable_reports_the_declared_type_not_the_member()
    {
        var output = Run(
            "type Shape = union { int, string }",
            "let a: Shape = 5",
            "let b: Shape = \"x\"",
            "print(typeof(a).name)",
            "print(typeof(b).name)");

        Assert.Equal("Shape\nShape\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void It_can_be_used_in_a_condition_a_struct_field_and_an_interpolated_string()
    {
        var output = Run(
            "using std::types::TypeInfo\ntype Label = struct { info: TypeInfo }",
            "let n = 7_short",
            "if typeof(n).name == \"short\" { print(\"short\") }",
            "let label = Label { info = typeof(n) }",
            "print($\"{label.info.name} and {typeof(n).name}\")");

        Assert.Equal("short\nshort and short\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Inside_a_method_self_is_a_borrow()
    {
        var output = Run(
            "type P = struct { x: int }\nP impl {\n    fn kind(&self) -> string { return typeof(self).name }\n}",
            "print(P { x = 1 }.kind())");

        Assert.Equal("&P\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_program_can_have_its_own_TypeInfo_next_to_the_built_in_one()
    {
        var output = Run(
            "type TypeInfo = struct { a: int }",
            "let mine = TypeInfo { a = 7 }",
            "print(mine.a)",
            "print(typeof(mine).name)",
            "print(typeof(1).name)");

        Assert.Equal("7\nTypeInfo\nint\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void An_import_that_is_never_used_is_harmless()
    {
        var output = Run("using std::types::TypeInfo", "print(\"ok\")");

        Assert.Equal("ok\n", output.Replace("\r\n", "\n"));
    }
}
