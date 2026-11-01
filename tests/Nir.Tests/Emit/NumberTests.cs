using Nir.Tests.Support;

namespace Nir.Tests.Emit;

/// <summary>Slice 9c end to end: every number type and char compiled, run and compared with the output the language promises.</summary>
public class NumberTests
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

    // A program that must crash with an OverflowException.
    private static void AssertOverflows(params string[] body)
    {
        var (_, stderr, exitCode) = TestHost.Run(string.Join("\n", ["fn main() {", .. body.Select(line => "    " + line), "}"]) + "\n");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("OverflowException", stderr, StringComparison.Ordinal);
    }

    // ---- values and printing ----

    [Fact]
    public void Every_type_holds_and_prints_its_smallest_and_largest_value()
    {
        Assert.Equal(
            "0 255\n-128 127\n-32768 32767\n0 65535\n-2147483648 2147483647\n0 4294967295\n" +
            "-9223372036854775808 9223372036854775807\n0 18446744073709551615\n",
            InMain(
                "print($\"{0_byte} {255_byte}\")",
                "print($\"{-128_sbyte} {127_sbyte}\")",
                "print($\"{-32768_short} {32767_short}\")",
                "print($\"{0_ushort} {65535_ushort}\")",
                "print($\"{-2147483648} {2147483647}\")",
                "print($\"{0_uint} {4294967295_uint}\")",
                "print($\"{-9223372036854775808_long} {9223372036854775807_long}\")",
                "print($\"{0_ulong} {18446744073709551615_ulong}\")"));
    }

    [Fact]
    public void Print_works_for_every_type_not_only_inside_interpolation()
    {
        Assert.Equal("200\n-5\n-300\n65535\n7\n4000000000\n-9\n18446744073709551615\n2.5\n2.5\n2.50\nx\nTrue\nhi\n", InMain(
            "print(200_byte)",
            "print(-5_sbyte)",
            "print(-300_short)",
            "print(65535_ushort)",
            "print(7)",
            "print(4000000000_uint)",
            "print(-9_long)",
            "print(18446744073709551615_ulong)",
            "print(2.5_float)",
            "print(2.5)",
            "print(2.50_decimal)",
            "print('x')",
            "print(true)",
            "print(\"hi\")"));
    }

    [Fact]
    public void Literals_take_the_type_of_their_context_at_run_time_too()
    {
        Assert.Equal("200 -128 4000000000 3000000000 2.5 2.5 0.1\n", InMain(
            "let a: byte = 200",
            "let b: sbyte = -128",
            "let c: uint = 4000000000",
            "let d: long = 3000000000",
            "let e: float = 2.5",
            "let f: decimal = 2.5",
            "let g: decimal = 0.1",
            "print($\"{a} {b} {c} {d} {e} {f} {g}\")"));
    }

    // ---- arithmetic ----

    [Fact]
    public void Arithmetic_gives_the_operand_type_for_every_integer_type()
    {
        Assert.Equal(
            "13 7 30 3 1\n-3 -13 -30 -3 -1\n130 70 3000 3 10\n" +
            "60000 65534 142 535 123\n3000000000 1000000000 3 1\n9000000000 1 3 2\n",
            InMain(
                "print($\"{10_byte + 3_byte} {10_byte - 3_byte} {10_byte * 3_byte} {10_byte / 3_byte} {10_byte % 3_byte}\")",
                "print($\"{-10_sbyte + 7_sbyte} {-10_sbyte - 3_sbyte} {-10_sbyte * 3_sbyte} {-10_sbyte / 3_sbyte} {-10_sbyte % 3_sbyte}\")",
                "print($\"{100_short + 30_short} {100_short - 30_short} {100_short * 30_short} {100_short / 30_short} {100_short % 30_short}\")",
                "print($\"{300_ushort * 200_ushort} {65535_ushort - 1_ushort} {1000_ushort / 7_ushort} {65535_ushort % 1000_ushort} {100_ushort + 23_ushort}\")",
                "print($\"{2000000000_uint + 1000000000_uint} {4000000000_uint - 3000000000_uint} {4000000000_uint / 1000000000_uint - 1_uint} {4000000000_uint % 3_uint}\")",
                "print($\"{3000000000_long * 3_long} {10_ulong - 9_ulong} {10_ulong / 3_ulong} {20_ulong % 6_ulong}\")"));
    }

    [Fact]
    public void Float_and_double_follow_ieee()
    {
        Assert.Equal("Infinity -Infinity True False 0.1 0.1 3.3000002\n", InMain(
            "let zero = 0.0",
            "let nan = zero / zero",
            "print($\"{1.0 / zero} {-1.0 / zero} {nan != nan} {nan <= nan} {0.1_float} {0.1_double} {1.1_float * 3.0}\")"));
    }

    [Fact]
    public void Decimal_is_exact_where_double_is_not()
    {
        Assert.Equal("True False 0.3 3.5 -2.5 1.0 True\n", InMain(
            "print($\"{0.1_decimal + 0.2_decimal == 0.3_decimal} {0.1 + 0.2 == 0.3} {0.1_decimal + 0.2_decimal} {7_decimal / 2.0} {-2.5_decimal} {7.5_decimal % 2.0 - 0.5} {1.5_decimal > 1.25}\")"));
    }

    [Fact]
    public void Unsigned_numbers_compare_and_divide_as_unsigned()
    {
        Assert.Equal("True True 147483648 1 True False 9223372036854775808 True\n", InMain(
            "let big: uint = 4000000000",
            "let small: uint = 5",
            "let huge: ulong = 18446744073709551615",
            "print($\"{big > small} {small < big} {big / 2 - 1852516352} {big % 3} {huge >= 9223372036854775808} {huge <= 5} {huge / 2 + 1} {big != small}\")"));
    }

    [Fact]
    public void Negation_works_for_signed_numbers()
    {
        Assert.Equal("-5 5 -5 5 -2.5 2.5 -2.5 -128\n", InMain(
            "let a: sbyte = 5",
            "let b: short = -5",
            "let c: long = 5",
            "let d: float = 2.5",
            "let e: decimal = 2.5",
            "print($\"{-a} {-b} {-c} {-(-c)} {-d} {-(-d)} {-e} {-127_sbyte - 1_sbyte}\")"));
    }

    // ---- overflow ----

    [Theory]
    [InlineData("print(250_byte + 10_byte)")]
    [InlineData("print(0_byte - 1_byte)")]
    [InlineData("print(16_byte * 16_byte)")]
    [InlineData("print(127_sbyte + 1_sbyte)")]
    [InlineData("print(-128_sbyte - 1_sbyte)")]
    [InlineData("let m: sbyte = -128\nprint(m / -1_sbyte)")]
    [InlineData("let m: sbyte = -128\nprint(-m)")]
    [InlineData("print(32767_short + 1_short)")]
    [InlineData("print(65535_ushort * 65535_ushort)")]
    [InlineData("print(0_ushort - 1_ushort)")]
    [InlineData("print(2147483647 + 1)")]
    [InlineData("print(0_uint - 1_uint)")]
    [InlineData("print(4294967295_uint + 1_uint)")]
    [InlineData("print(9223372036854775807_long + 1_long)")]
    [InlineData("print(0_ulong - 1_ulong)")]
    [InlineData("print(18446744073709551615_ulong * 2_ulong)")]
    [InlineData("let m: long = -9223372036854775808\nprint(-m)")]
    [InlineData("print(79228162514264337593543950335_decimal + 1_decimal)")]
    public void Integer_arithmetic_that_does_not_fit_throws(string statement) => AssertOverflows(statement.Split('\n'));

    // ---- conversions ----

    [Fact]
    public void As_converts_between_number_types()
    {
        Assert.Equal(
            "200 -56 5 3 -3 3.5 2.5 7 255 4000000000 -1 65 97 65\n",
            InMain(
                "let b: byte = 200",
                "let l = 5_long",
                "print($\"{b as int} {(b as sbyte catch -56)} {l as int} {3.99 as int} {-3.99 as int} {7_decimal / 2.0 as double} {2.5_float as double} {7.9_decimal as int} {-1 as int as short as sbyte as long as byte catch 255} {4000000000_uint as ulong} {-1_long as int} {65 as char as int} {'a' as byte} {65_ulong as char as int}\")"));
    }

    [Fact]
    public void Unsigned_and_wide_values_convert_exactly()
    {
        Assert.Equal("4000000000 4000000000 1.8446744073709552E+19 16777216 9223372036854775807 100 0.5\n", InMain(
            "let u: uint = 4000000000",
            "let big: ulong = 18446744073709551615",
            "print($\"{u as long} {u as double} {big as double} {16777217_int as float} {(big / 2) as long} {100_decimal as int} {0.5 as decimal}\")"));
    }

    [Theory]
    [InlineData("print(300 as byte)")]
    [InlineData("print(-1 as byte)")]
    [InlineData("print(128 as sbyte)")]
    [InlineData("print(-1 as ulong)")]
    [InlineData("print(-1 as uint)")]
    [InlineData("print(70000 as short)")]
    [InlineData("print(-1 as ushort)")]
    [InlineData("print(3000000000_uint as int)")]
    [InlineData("print(5000000000_long as int)")]
    [InlineData("print(18446744073709551615_ulong as long)")]
    [InlineData("print(3000000000.0 as int)")]
    [InlineData("print(-1.5 as uint)")]
    [InlineData("let x = 0.0\nprint((x / x) as int)")]
    [InlineData("let x = 1.0\nprint((x / 0.0) as long)")]
    [InlineData("print(-1 as char)")]
    [InlineData("print(70000 as char)")]
    [InlineData("print(99999999999999999999999999999.0 as decimal)")]
    [InlineData("print(79228162514264337593543950335_decimal as long)")]
    public void As_throws_when_the_value_does_not_fit(string statement) => AssertOverflows(statement.Split('\n'));

    // ---- catch and try on as ----

    [Fact]
    public void Catch_handles_a_failed_conversion()
    {
        Assert.Equal("0 200 255 -1 5 98 99 True\n", InMain(
            "let big = 300",
            "let ok = 200",
            "let a = big as byte catch 0",
            "let b = ok as byte catch 0",
            "let c = big as byte catch e => 255",
            "let d = 3000000000_long as int catch -1",
            "let f = 5_ulong as int catch 0",
            "print($\"{a} {b} {c} {d} {f} {big as byte catch e => 98} {-1 as byte catch 99} {(big as byte catch e => 0) == 0}\")"));
    }

    [Fact]
    public void Try_returns_the_conversion_error_from_the_function()
    {
        Assert.Equal("7\nfailed\n", Run(
            "fn narrow(x: int) -> anyerror!byte {",
            "    let b = try (x as byte)",
            "    return b",
            "}",
            "fn main() {",
            "    print(narrow(7) catch 0)",
            "    let r = narrow(700) catch e => 0",
            "    if r == 0 { print(\"failed\") }",
            "}"));
    }

    [Fact]
    public void The_message_of_a_failed_conversion_is_the_dotnet_message()
    {
        Assert.Equal("Arithmetic operation resulted in an overflow.\n", Run(
            "fn narrow(x: int) -> anyerror!byte {",
            "    let b = try (x as byte)",
            "    return b",
            "}",
            "fn main() -> anyerror! {",
            "    let r = narrow(700)",
            "    match r {",
            "        e: anyerror => print(e.message),",
            "        v: byte => print(v),",
            "    }",
            "}"));
    }

    // ---- ranges ----

    [Fact]
    public void A_range_runs_over_other_integer_types()
    {
        Assert.Equal("10 6 3 3 5 0\n", InMain(
            "let mut a: long = 0",
            "for i in 0..5_long { a = a + i }",
            "let mut b = 0",
            "for i in 250..=255_byte { b = b + 1 }",
            "let mut c = 0",
            "for i in 4294967293..=4294967295_uint { c = c + 1 }",
            "let mut d = 0",
            "for i in 253..=255_byte { d = d + 1 }",
            "let mut e = 0",
            "for i in 120..=127_sbyte { if i > 122 { e = e + 1 } }",
            "let mut f = 0",
            "for i in 5..3_short { f = f + 1 }",
            "print($\"{a} {b} {c} {d} {e} {f}\")"));
    }

    [Fact]
    public void A_range_over_chars_gives_each_char()
    {
        Assert.Equal("abcde|abcd|xyz\n", InMain(
            "let mut s = \"\"",
            "for c in 'a'..='e' { s = s + $\"{c}\" }",
            "let mut t = \"\"",
            "for c in 'a'..'e' { t = t + $\"{c}\" }",
            "let mut u = \"\"",
            "for c in 'x'..='z' { if c != 'q' { u = u + $\"{c}\" } }",
            "print($\"{s}|{t}|{u}\")"));
    }

    [Fact]
    public void An_inclusive_range_ending_at_the_largest_value_stops()
    {
        Assert.Equal("256 1\n", InMain(
            "let mut n = 0",
            "for i in 0..=255_byte { n = n + 1 }",
            "let mut m = 0",
            "for i in 18446744073709551615..=18446744073709551615_ulong { m = m + 1 }",
            "print($\"{n} {m}\")"));
    }

    [Fact]
    public void Break_and_continue_work_in_ranges_of_other_types()
    {
        Assert.Equal("a c ", InMain(
            "let mut s = \"\"",
            "for c in 'a'..='z' {",
            "    if c == 'b' { continue }",
            "    if c == 'd' { break }",
            "    s = s + $\"{c} \"",
            "}",
            "print(s)").TrimEnd('\n'));
    }

    // ---- characters ----

    [Fact]
    public void Characters_compare_and_convert()
    {
        Assert.Equal("True False True b 98 a 0 3\n", InMain(
            "let c = 'a'",
            "let next = ((c as int) + 1) as char",
            "print($\"{c < next} {c == next} {c != next} {next} {next as int} {'a'} {'\\0' as int} {'3' as int - '0' as int}\")"));
    }

    [Fact]
    public void Character_escapes_give_the_right_characters()
    {
        Assert.Equal("10 9 13 92 39 34 0 123\n", InMain(
            "print($\"{'\\n' as int} {'\\t' as int} {'\\r' as int} {'\\\\' as int} {'\\'' as int} {'\"' as int} {'\\0' as int} {'\\{' as int}\")"));
    }

    [Fact]
    public void A_char_travels_through_functions_structs_tuples_and_unions()
    {
        Assert.Equal("Z z Z 1 x\n", Run(
            "type Letter = struct { c: char, n: byte }",
            "type Pair = (char, long)",
            "type Either = union { char, double }",
            "fn lower(c: char) -> char { return ((c as int) + 32) as char }",
            "fn show(e: Either) -> string {",
            "    return match e {",
            "        c: char => $\"{c}\",",
            "        d: double => $\"{d}\",",
            "    }",
            "}",
            "fn main() {",
            "    let l = Letter { c = 'Z', n = 1 }",
            "    let p: Pair = ('Z', 5)",
            "    print($\"{l.c} {lower(l.c)} {p.0} {l.n} {show('x')}\")",
            "}"));
    }

    // ---- structs, tuples, functions ----

    [Fact]
    public void Every_number_type_works_as_a_struct_field_and_a_tuple_element()
    {
        Assert.Equal("1 2 3 4 5 6 7 8 9.5 10.5 11.5 c\n1 2 3 4 5 6 7 8 9.5 10.5 11.5 c\n", Run(
            "type All = struct {",
            "    a: byte, b: sbyte, c: short, d: ushort, e: int, f: uint, g: long, h: ulong, i: float, j: double, k: decimal, l: char",
            "}",
            "fn main() {",
            "    let v = All { a = 1, b = 2, c = 3, d = 4, e = 5, f = 6, g = 7, h = 8, i = 9.5, j = 10.5, k = 11.5, l = 'c' }",
            "    print($\"{v.a} {v.b} {v.c} {v.d} {v.e} {v.f} {v.g} {v.h} {v.i} {v.j} {v.k} {v.l}\")",
            "    let t = (v.a, v.b, v.c, v.d, v.e, v.f, v.g, v.h, v.i, v.j, v.k, v.l)",
            "    print($\"{t.0} {t.1} {t.2} {t.3} {t.4} {t.5} {t.6} {t.7} {t.8} {t.9} {t.10} {t.11}\")",
            "}"));
    }

    [Fact]
    public void Functions_take_and_return_every_kind_of_number()
    {
        Assert.Equal("300 4000000001 -1 3.75 1.01 7 B\n", Run(
            "fn widen(x: byte) -> short { return (x as short) + 100 }",
            "fn bump(x: uint) -> uint { return x + 1 }",
            "fn neg(x: long) -> long { return -x }",
            "fn half(x: float) -> float { return x / 2.0 }",
            "fn cent(x: decimal) -> decimal { return x + 0.01 }",
            "fn count() -> ulong { return 7 }",
            "fn upper(c: char) -> char { return ((c as int) - 32) as char }",
            "fn main() {",
            "    print($\"{widen(200)} {bump(4000000000)} {neg(1)} {half(7.5)} {cent(1.00)} {count()} {upper('b')}\")",
            "}"));
    }

    // ---- C# interop ----

    [Fact]
    public void Dotnet_methods_with_other_number_types_can_be_called()
    {
        Assert.Equal("5 9 3 True False 7 4.5 12\n", Run(
            "using clr::cs::System::Math",
            "using clr::cs::System::Char",
            "fn main() {",
            "    let n = -5_long",
            "    print($\"{Math::Abs(n)} {Math::Max(n, 9)} {Math::Min(7_ulong, 3)} {Char::IsDigit('4')} {Char::IsDigit('x')} {Math::Abs(-7_short)} {Math::Max(2.5_float, 4.5)} {Math::Max(12_decimal, 3_decimal)}\")",
            "}"));
    }

    [Fact]
    public void Dotnet_instance_methods_work_on_the_new_types()
    {
        Assert.Equal("200 2.5 c C 10\n", InMain(
            "let b: byte = 200",
            "let f: float = 2.5",
            "let c = 'c'",
            "print($\"{b.ToString()} {f.ToString()} {c.ToString()} {c.ToString().ToUpper()} {10_ulong.ToString()}\")"));
    }
}
