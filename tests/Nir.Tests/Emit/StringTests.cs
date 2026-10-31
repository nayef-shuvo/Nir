using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class StringTests
{
    private static string Run(params string[] lines)
    {
        var (stdout, stderr, exitCode) = TestHost.Run(string.Join("\n", lines) + "\n");
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        return stdout;
    }

    private static string InMain(params string[] body) =>
        Run(["fn main() {", .. body.Select(line => "    " + line), "}"]);

    // ---- + and == ----

    [Fact]
    public void Plus_joins_strings()
    {
        Assert.Equal("Hello, Nir!\nabcde\nabc\n", InMain(
            "let name = \"Nir\"",
            "print(\"Hello, \" + name + \"!\")",
            "print(\"a\" + \"b\" + \"c\" + \"d\" + \"e\")",
            "print(\"\" + \"abc\" + \"\")"));
    }

    [Fact]
    public void Plus_works_in_functions_and_loops()
    {
        Assert.Equal("Hello, Ada!\n0,1,2,\n", Run(
            "fn greet(name: string) -> string {",
            "    return \"Hello, \" + name + \"!\"",
            "}",
            "fn main() {",
            "    print(greet(\"Ada\"))",
            "    let mut s = \"\"",
            "    for i in 0..3 {",
            "        s = s + $\"{i},\"",
            "    }",
            "    print(s)",
            "}"));
    }

    [Fact]
    public void Equality_compares_the_characters_not_the_object()
    {
        Assert.Equal("True\nTrue\nFalse\nTrue\nFalse\nTrue\nTrue\n", InMain(
            "print(\"a\" == \"a\")",
            "print((\"ab\" + \"c\") == (\"a\" + \"bc\"))",   // two different objects with the same characters
            "print(\"a\" == \"A\")",                             // case matters
            "print(\"a\" != \"b\")",
            "print(\"a\" != \"a\")",
            "print(\"\" == \"\")",
            "print(\"x\" + \"y\" != \"xz\")"));
    }

    [Fact]
    public void String_comparisons_work_in_conditions()
    {
        Assert.Equal("match\nno match\n", InMain(
            "let word = \"nir\"",
            "if word == \"nir\" {",
            "    print(\"match\")",
            "}",
            "if word != \"nir\" || word == \"other\" {",
            "    print(\"wrong\")",
            "} else {",
            "    print(\"no match\")",
            "}"));
    }

    // ---- interpolation ----

    [Fact]
    public void Interpolation_formats_every_type()
    {
        Assert.Equal("n = 42, ratio = 7.5, even = True, name = Nir\n", InMain(
            "let n = 42",
            "let ratio = 7.5",
            "let name = \"Nir\"",
            "print($\"n = {n}, ratio = {ratio}, even = {n % 2 == 0}, name = {name}\")"));
    }

    [Fact]
    public void Interpolation_handles_any_number_of_parts()
    {
        Assert.Equal("\nx\n12\n123\n1234\n12345\n123456\n", InMain(
            "print($\"\")",
            "print($\"x\")",
            "print($\"{1}{2}\")",
            "print($\"{1}{2}{3}\")",
            "print($\"{1}{2}{3}{4}\")",
            "print($\"{1}{2}{3}{4}{5}\")",
            "print($\"{1}{2}{3}{4}{5}{6}\")"));
    }

    [Fact]
    public void Interpolation_numbers_and_booleans_use_the_invariant_form()
    {
        Assert.Equal("-5|0.1|10|True|False|2147483647\n", InMain(
            "print($\"{-5}|{0.1}|{10.0}|{true}|{1 > 2}|{2147483647}\")"));
    }

    [Fact]
    public void Interpolation_holes_can_hold_calls_method_calls_and_nested_strings()
    {
        Assert.Equal("ABC 9 <12> [x!]\n", Run(
            "fn square(n: int) -> int {",
            "    return n * n",
            "}",
            "fn main() {",
            "    print($\"{\"abc\".ToUpper()} {square(3)} <{$\"{1}{2}\"}> [{\"x\" + \"!\"}]\")",
            "}"));
    }

    [Fact]
    public void Doubled_braces_and_escapes_in_interpolated_strings()
    {
        Assert.Equal("{literal} 42\ntab\t42\nquote \" 42\n", InMain(
            "let n = 42",
            "print($\"{{literal}} {n}\")",
            "print($\"tab\\t{n}\")",
            "print($\"quote \\\" {n}\")"));
    }

    [Fact]
    public void Plain_string_escapes_are_decoded()
    {
        Assert.Equal("a\tb\nline1\nline2\nsay \"hi\"\nC:\\temp\n{ok}\n", InMain(
            "print(\"a\\tb\")",
            "print(\"line1\\nline2\")",
            "print(\"say \\\"hi\\\"\")",
            "print(\"C:\\\\temp\")",
            "print(\"\\{ok}\")"));
    }

    [Fact]
    public void Holes_are_evaluated_left_to_right()
    {
        Assert.Equal("first\nsecond\n12\n", Run(
            "fn a() -> int {",
            "    print(\"first\")",
            "    return 1",
            "}",
            "fn b() -> int {",
            "    print(\"second\")",
            "    return 2",
            "}",
            "fn main() {",
            "    print($\"{a()}{b()}\")",
            "}"));
    }

    [Fact]
    public void A_null_string_from_dotnet_becomes_empty_text()
    {
        Assert.Equal("[]\n\n[ok]\n", Run(
            "using clr::cs::System::Environment",
            "fn main() {",
            "    let missing = Environment::GetEnvironmentVariable(\"NIR_SURELY_NOT_SET_9f3a1\")",
            "    print($\"[{missing}]\")",
            "    print($\"{missing}\")",
            "    print($\"[{\"ok\"}]\")",
            "}"));
    }

    // ---- conversions ----

    [Fact]
    public void Int_converts_to_double_exactly()
    {
        Assert.Equal("5\n3.5\n2147483647\n3.5\n0.5\n", InMain(
            "print(5 as double)",
            "print((7 as double) / 2.0)",
            "print(2147483647 as double)",
            "print(3 as double + 0.5)",
            "print(1 as double / 2.0)"));
    }

    [Fact]
    public void Double_converts_to_int_by_cutting_off_the_fraction()
    {
        Assert.Equal("7\n-7\n-2\n0\n2147483647\n-2147483648\n", InMain(
            "print(7.9 as int)",
            "print(-7.9 as int)",
            "print(-2.7 as int)",
            "print(0.99 as int)",
            "print(2147483647.0 as int)",
            "print(-2147483648.0 as int)"));
    }

    [Fact]
    public void Conversions_chain_and_follow_precedence()
    {
        Assert.Equal("2\n7\n9\n", InMain(
            "print(2.5 as int as double)",          // 2.5 -> 2 -> 2.0, printed as 2
            "let n = 3",
            "print(n as double * 2.0 + 1.0)",       // (3.0 * 2.0) + 1.0 = 7
            "print(n * n as int)"));                // `as int` on an int does nothing: 3 * 3 = 9
    }

    [Theory]
    [InlineData("2147483648.0 as int")]
    [InlineData("-2147483649.0 as int")]
    [InlineData("(0.0 / 0.0) as int")]
    [InlineData("(1.0 / 0.0) as int")]
    public void A_double_that_does_not_fit_an_int_crashes(string expression)
    {
        var (stdout, stderr, exitCode) = TestHost.Run($"fn main() {{\n    print(1)\n    print({expression})\n}}\n");

        Assert.Equal("1\n", stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("OverflowException", stderr, StringComparison.Ordinal);
    }
}
