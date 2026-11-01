using Nir.Compiler.Diagnostics;
using Nir.Compiler.Semantics;
using Nir.Tests.Support;

namespace Nir.Tests.Semantics;

/// <summary>Slice 9c: the number types, literal typing, operators, conversions and characters (design.md "Numbers" and "Characters").</summary>
public class NumberBinderTests
{
    private const string MainHeader = "fn main() {\n";

    // Binds the statements as the body of main, each on its own line indented by four spaces.
    private static (BoundProgram? Program, IReadOnlyList<string> Diagnostics) BindMain(params string[] lines)
    {
        var (program, _, diagnostics) = BinderHarness.Bind(MainHeader + string.Concat(lines.Select(l => "    " + l + "\n")) + "}");
        return (program, diagnostics);
    }

    // The diagnostic "CODE line:column" for something found in the single statement on line 2: `at` is the text it is reported at.
    private static string At(string code, string statement, string at) => $"{code} 2:{5 + statement.IndexOf(at, StringComparison.Ordinal)}";

    private static BoundNumberLiteral FirstInitializer(BoundProgram program) =>
        (BoundNumberLiteral)((BoundVariableDeclaration)program.Functions[0].Body.Statements[0]).Initializer;

    // ---- the types exist ----

    [Theory]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("int")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    [InlineData("char")]
    public void Every_primitive_type_can_be_written_as_a_parameter_type(string type)
    {
        var (_, _, diagnostics) = BinderHarness.Bind($"fn f(x: {type}) -> {type} {{\n    return x\n}}\nfn main() {{\n}}\n");

        Assert.Empty(diagnostics);
    }

    // ---- literals take the type of their context ----

    [Theory]
    [InlineData("let a: byte = 255")]
    [InlineData("let a: sbyte = -128")]
    [InlineData("let a: sbyte = 127")]
    [InlineData("let a: short = -32768")]
    [InlineData("let a: ushort = 65535")]
    [InlineData("let a: int = -2147483648")]
    [InlineData("let a: uint = 4294967295")]
    [InlineData("let a: long = 3000000000")]
    [InlineData("let a: long = -9223372036854775808")]
    [InlineData("let a: ulong = 18446744073709551615")]
    [InlineData("let a: float = 2.5")]
    [InlineData("let a: double = 2.5")]
    [InlineData("let a: decimal = 2.5")]
    public void A_literal_that_fits_takes_the_annotated_type(string statement)
    {
        var (program, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        Assert.Equal(statement[(statement.IndexOf(':', StringComparison.Ordinal) + 2)..statement.IndexOf(' ', 7)], FirstInitializer(program!).Type.Name);
    }

    [Fact]
    public void An_unannotated_literal_is_int_or_double()
    {
        var (program, diagnostics) = BindMain("let a = 5", "let b = 2.5");

        Assert.Empty(diagnostics);
        var statements = program!.Functions[0].Body.Statements;
        Assert.Equal(TypeSymbol.IntType, ((BoundVariableDeclaration)statements[0]).Variable.Type);
        Assert.Equal(TypeSymbol.DoubleType, ((BoundVariableDeclaration)statements[1]).Variable.Type);
    }

    [Theory]
    [InlineData("let a = 200_byte", "byte", (byte)200)]
    [InlineData("let a = 5_long", "long", 5L)]
    [InlineData("let a = -5_sbyte", "sbyte", (sbyte)-5)]
    [InlineData("let a = 7_ulong", "ulong", 7UL)]
    [InlineData("let a = 5_double", "double", 5.0)]
    [InlineData("let a = 5_float", "float", 5.0f)]
    [InlineData("let a = 5_decimal", "decimal", 5)]
    [InlineData("let a = 2.5_float", "float", 2.5f)]
    [InlineData("let a = 2.5_decimal", "decimal", 2.5)]
    public void A_suffix_gives_the_literal_its_type(string statement, string type, object expected)
    {
        var (program, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        var literal = FirstInitializer(program!);
        Assert.Equal(type, literal.Type.Name);
        Assert.Equal(System.Convert.ChangeType(expected, literal.Value!.GetType(), System.Globalization.CultureInfo.InvariantCulture), literal.Value);
        Assert.False(literal.CanAdapt);
    }

    [Fact]
    public void A_suffix_wins_over_the_context()
    {
        var statement = "let a: long = 5_int";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3001", statement, "5_int")], diagnostics);
    }

    [Theory]
    [InlineData("let a: byte = 300", "300")]
    [InlineData("let a: byte = 256", "256")]
    [InlineData("let a: byte = -1", "-1")]
    [InlineData("let a: sbyte = 128", "128")]
    [InlineData("let a: sbyte = -129", "-129")]
    [InlineData("let a: ushort = 65536", "65536")]
    [InlineData("let a: uint = 4294967296", "4294967296")]
    [InlineData("let a: uint = -1", "-1")]
    [InlineData("let a: ulong = 18446744073709551616", "18446744073709551616")]
    [InlineData("let a = 128_sbyte", "128_sbyte")]
    [InlineData("let a = 300_byte", "300_byte")]
    [InlineData("let a = -5_uint", "-5_uint")]
    [InlineData("let a: float = 99999999999999999999999999999999999999999.5", "99999999999999999999999999999999999999999.5")]
    public void A_literal_that_does_not_fit_is_a_compile_error_at_the_literal(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3004", statement, at)], diagnostics);
    }

    [Theory]
    [InlineData("let a = 3000000000", "3000000000")]
    [InlineData("let a = -2147483649", "-2147483649")]
    [InlineData("let a: int = 3000000000", "3000000000")]
    [InlineData("let a = 99999999999999999999999", "99999999999999999999999")]
    [InlineData("print(3000000000)", "3000000000")]
    public void A_whole_number_too_big_for_int_with_no_wider_context_is_an_error(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3004", statement, at)], diagnostics);
    }

    [Fact]
    public void A_whole_number_literal_does_not_become_a_fraction_type()
    {
        var statement = "let a: double = 1";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3001", statement, "1")], diagnostics);
    }

    [Theory]
    [InlineData("let a: double = 1", "write '1.0' or '1_double'")]
    [InlineData("let a: decimal = 12", "write '12.0' or '12_decimal'")]
    public void The_error_for_a_whole_literal_says_how_to_write_a_fraction(string statement, string hint)
    {
        var (_, bag, _) = BinderHarness.Bind(MainHeader + "    " + statement + "\n}");

        Assert.Contains(hint, Assert.Single(bag).Message);
    }

    [Theory]
    [InlineData("let a: byte = 1.5")]
    [InlineData("let a: long = 2.0")]
    public void A_literal_with_a_fraction_does_not_become_an_integer_type(string statement)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3001", statement, statement[(statement.IndexOf('=', StringComparison.Ordinal) + 2)..])], diagnostics);
    }

    [Fact]
    public void A_fraction_with_an_integer_suffix_is_an_error()
    {
        var statement = "let a = 2.5_int";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3029", statement, "2.5_int")], diagnostics);
    }

    // ---- where a literal takes its type from ----

    [Theory]
    [InlineData("let n = 5_long", "let a = n + 1")]
    [InlineData("let n = 5_long", "let a = 1 + n")]
    [InlineData("let n = 5_long", "let a = n * 2 - 1")]
    [InlineData("let n = 5_byte", "let a = n < 200")]
    [InlineData("let n = 2.5_float", "let a = n * 2.0")]
    [InlineData("let n = 2.5_decimal", "let a = 1.5 + n")]
    [InlineData("let n = 5_ulong", "let a = n == 18446744073709551615")]
    public void A_literal_next_to_a_typed_operand_takes_its_type(string declaration, string use)
    {
        var (_, diagnostics) = BindMain(declaration, use);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_literal_next_to_a_typed_operand_must_fit_its_type()
    {
        var use = "let a = n + 300";

        var (_, diagnostics) = BindMain("let n = 5_byte", use);

        Assert.Equal(["NIR3004 3:" + (5 + use.IndexOf("300", StringComparison.Ordinal))], diagnostics);
    }

    [Fact]
    public void Two_different_literals_do_not_adapt_to_each_other()
    {
        var statement = "let a = 1 + 2.5";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3003", statement, "1 + 2.5")], diagnostics);
    }

    [Theory]
    [InlineData("fn f(x: byte) -> byte { return x }\nfn main() { print(f(200)) }")]
    [InlineData("fn f() -> long { return 3000000000 }\nfn main() { print(f()) }")]
    [InlineData("type P = struct { a: byte, b: float }\nfn main() { let p = P { a = 7, b = 2.5 }\nprint(p.a) }")]
    [InlineData("fn main() {\nlet mut a: short = 1\na = 300\nprint(a) }")]
    [InlineData("fn f() -> anyerror!long { return 5 }\nfn main() { print(f() catch 0) }")]
    public void A_literal_takes_the_type_of_an_argument_a_return_a_field_and_an_assignment(string program)
    {
        var (_, _, diagnostics) = BinderHarness.Bind(program);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_argument_literal_that_does_not_fit_the_parameter_is_reported_at_the_literal()
    {
        var (_, _, diagnostics) = BinderHarness.Bind("fn f(x: byte) { }\nfn main() {\n    f(300)\n}");

        Assert.Equal(["NIR3004 3:7"], diagnostics);
    }

    [Theory]
    [InlineData("print(Math::Max(n, 1))", "long")]
    [InlineData("print(Math::Abs(-5_long))", "long")]
    [InlineData("print(Math::Max(2.5_float, 1.0))", "float")]
    public void A_dotnet_call_picks_the_overload_that_the_literals_fit(string call, string _)
    {
        var (_, _, diagnostics) = BinderHarness.Bind("using clr::cs::System::Math\nfn main() {\n    let n = 5_long\n    " + call + "\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_dotnet_call_with_a_literal_that_fits_no_overload_is_still_an_error()
    {
        var (_, _, diagnostics) = BinderHarness.Bind("using clr::cs::System::Math\nfn main() {\n    print(Math::Pow(2, 3))\n}");

        Assert.Equal(["NIR3006 3:11"], diagnostics);
    }

    // ---- operators need the same type ----

    [Theory]
    [InlineData("let a = 1_int + 2_long", "1_int + 2_long")]
    [InlineData("let a = 1_byte * 2_sbyte", "1_byte * 2_sbyte")]
    [InlineData("let a = 1_float - 1_double", "1_float - 1_double")]
    [InlineData("let a = 1_decimal / 1_double", "1_decimal / 1_double")]
    [InlineData("let a = 5_decimal < 6_float", "5_decimal < 6_float")]
    [InlineData("let a = 1_uint == 1_int", "1_uint == 1_int")]
    public void Different_number_types_cannot_be_mixed(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3003", statement, at)], diagnostics);
    }

    [Theory]
    [InlineData("let a = 1_byte + 2_byte")]
    [InlineData("let a = 7_ulong % 2_ulong")]
    [InlineData("let a = 1_float / 2_float")]
    [InlineData("let a = 1_decimal * 2_decimal")]
    [InlineData("let a = 'a' < 'b'")]
    [InlineData("let a = 'a' == 'b'")]
    [InlineData("let a = 'a' >= 'b'")]
    [InlineData("let a = 1_ushort != 2_ushort")]
    public void The_same_number_type_works_with_every_operator(string statement)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("let a = 1_byte + 2_byte", "byte")]
    [InlineData("let a = 1_decimal * 2_decimal", "decimal")]
    [InlineData("let a = 1_ulong - 1_ulong", "ulong")]
    [InlineData("let a = 1_float < 2_float", "bool")]
    public void The_result_of_arithmetic_has_the_operand_type_and_of_a_comparison_bool(string statement, string type)
    {
        var (program, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
        Assert.Equal(type, ((BoundVariableDeclaration)program!.Functions[0].Body.Statements[0]).Variable.Type.Name);
    }

    [Theory]
    [InlineData("let a = 'a' + 'b'", "'a' + 'b'")]
    [InlineData("let a = 'a' - 'b'", "'a' - 'b'")]
    [InlineData("let a = 'a' * 2", "'a' * 2")]
    public void Characters_have_no_arithmetic(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3003", statement, at)], diagnostics);
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    public void An_unsigned_number_cannot_be_negated(string type)
    {
        var statement = "let b = -x";

        var (_, diagnostics) = BindMain($"let x: {type} = 1", statement);

        Assert.Equal(["NIR3003 3:" + (5 + statement.IndexOf("-x", StringComparison.Ordinal))], diagnostics);
    }

    [Theory]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    public void A_signed_number_can_be_negated(string type)
    {
        var literal = type is "float" or "double" or "decimal" ? "1.5" : "1";

        var (_, diagnostics) = BindMain($"let x: {type} = {literal}", "let b = -x");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_minus_in_front_of_a_literal_is_part_of_the_literal()
    {
        var (program, diagnostics) = BindMain("let a = -128_sbyte");

        Assert.Empty(diagnostics);
        Assert.Equal((sbyte)-128, FirstInitializer(program!).Value);
    }

    // ---- conversions with as ----

    private static readonly string[] _numberTypes = ["byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal"];

    public static TheoryData<string, string> NumberPairs
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var from in _numberTypes)
            {
                foreach (var to in _numberTypes)
                {
                    data.Add(from, to);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NumberPairs))]
    public void Any_number_converts_to_any_other_number(string from, string to)
    {
        var (_, _, diagnostics) = BinderHarness.Bind($"fn f(x: {from}) -> {to} {{\n    return x as {to}\n}}\nfn main() {{\n}}\n");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("int")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void A_char_and_an_integer_convert_both_ways(string integerType)
    {
        var (_, _, diagnostics) = BinderHarness.Bind($"fn f(c: char) -> {integerType} {{\n    return c as {integerType}\n}}\nfn g(x: {integerType}) -> char {{\n    return x as char\n}}\nfn main() {{\n}}\n");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("let a = true as int", "true as int")]
    [InlineData("let a = 1 as bool", "1 as bool")]
    [InlineData("let a = \"1\" as int", "\"1\" as int")]
    [InlineData("let a = 1 as string", "1 as string")]
    [InlineData("let a = 'a' as double", "'a' as double")]
    [InlineData("let a = 'a' as float", "'a' as float")]
    [InlineData("let a = 'a' as decimal", "'a' as decimal")]
    [InlineData("let a = 1.5 as char", "1.5 as char")]
    [InlineData("let a = 1_decimal as char", "1_decimal as char")]
    public void Only_numbers_and_chars_convert_with_as(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3001", statement, at)], diagnostics);
    }

    [Fact]
    public void Converting_to_the_same_type_does_nothing()
    {
        var (program, diagnostics) = BindMain("let a = 5_long as long");

        Assert.Empty(diagnostics);
        Assert.IsType<BoundNumberLiteral>(((BoundVariableDeclaration)program!.Functions[0].Body.Statements[0]).Initializer);
    }

    [Fact]
    public void A_cast_does_not_give_its_operand_a_type()
    {
        var statement = "let a = 3000000000 as long";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3004", statement, "3000000000")], diagnostics);
    }

    // ---- catch and try on as ----

    [Theory]
    [InlineData("let a = 300 as byte catch 0")]
    [InlineData("let a = 300 as byte catch e => 255")]
    [InlineData("let a = 3.5 as int catch -1")]
    [InlineData("let a = 7_ulong as long catch 0")]
    public void A_conversion_can_be_handled_with_catch(string statement)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_fallback_of_a_conversion_must_have_the_converted_type()
    {
        var statement = "let a = 300 as byte catch \"x\"";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3001", statement, "\"x\"")], diagnostics);
    }

    [Fact]
    public void The_fallback_of_a_conversion_is_a_literal_of_the_converted_type()
    {
        var statement = "let a = 300 as byte catch 256";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3004", statement, "256")], diagnostics);
    }

    [Fact]
    public void A_conversion_to_the_same_type_cannot_fail_so_catch_is_not_allowed()
    {
        var statement = "let a = 5 as int catch 0";

        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3025", statement, "5 as int")], diagnostics);
    }

    [Fact]
    public void Try_on_a_conversion_returns_the_error_from_the_function()
    {
        var (_, _, diagnostics) = BinderHarness.Bind("fn f(x: int) -> anyerror!byte {\n    let b = try (x as byte)\n    return b\n}\nfn main() {\n}\n");

        Assert.Empty(diagnostics);
    }

    // ---- ranges ----

    [Theory]
    [InlineData("for i in 0..5_long { }")]
    [InlineData("for i in 0_byte..=255 { }")]
    [InlineData("for i in 0..=255_ushort { }")]
    [InlineData("for c in 'a'..='z' { }")]
    [InlineData("let n = 5_ulong\nfor i in 0..n { }")]
    public void A_range_runs_over_any_integer_type_or_char(string statement)
    {
        var (_, diagnostics) = BindMain(statement.Split('\n'));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("for i in 0..2.5 { }", "2.5")]
    [InlineData("for i in 0.0..1.0 { }", "0.0")]
    [InlineData("for i in 0_int..5_long { }", "5_long")]
    [InlineData("for i in 'a'..5 { }", "5")]
    [InlineData("for i in 0..=256_byte { }", "256_byte")]
    [InlineData("for i in true..false { }", "true")]
    public void Both_bounds_must_be_the_same_integer_type_or_char(string statement, string at)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.StartsWith(At(statement.Contains("256", StringComparison.Ordinal) ? "NIR3004" : "NIR3001", statement, at), diagnostics[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_loop_variable_has_the_type_of_the_bounds()
    {
        var (program, diagnostics) = BindMain("for i in 0..5_long { }", "for c in 'a'..='c' { }");

        Assert.Empty(diagnostics);
        var statements = program!.Functions[0].Body.Statements;
        Assert.Equal(TypeSymbol.LongType, ((BoundForStatement)statements[0]).Variable.Type);
        Assert.Equal(TypeSymbol.CharType, ((BoundForStatement)statements[1]).Variable.Type);
    }

    // ---- characters ----

    [Fact]
    public void A_character_literal_is_a_char()
    {
        var (program, diagnostics) = BindMain("let c = 'a'");

        Assert.Empty(diagnostics);
        var initializer = ((BoundVariableDeclaration)program!.Functions[0].Body.Statements[0]).Initializer;
        Assert.Equal('a', Assert.IsType<BoundCharLiteral>(initializer).Value);
    }

    [Theory]
    [InlineData("let c = ''")]
    [InlineData("let c = 'ab'")]
    [InlineData("let c = '😀'")]
    public void A_character_literal_holds_exactly_one_utf16_code_unit(string statement)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Equal([At("NIR3030", statement, statement[(statement.IndexOf('=', StringComparison.Ordinal) + 2)..])], diagnostics);
    }

    [Fact]
    public void A_char_is_not_a_string_or_an_int()
    {
        var statement = "let a: string = 'a'";

        var (_, diagnostics) = BindMain(statement, "let b: int = 'a'");

        Assert.Equal([At("NIR3001", statement, "'a'"), "NIR3001 3:18"], diagnostics);
    }

    // ---- printing and interpolation ----

    [Theory]
    [InlineData("print(5_byte)")]
    [InlineData("print(5_sbyte)")]
    [InlineData("print(5_short)")]
    [InlineData("print(5_ushort)")]
    [InlineData("print(5_uint)")]
    [InlineData("print(5_long)")]
    [InlineData("print(5_ulong)")]
    [InlineData("print(2.5_float)")]
    [InlineData("print(2.5_decimal)")]
    [InlineData("print('a')")]
    [InlineData("print($\"{5_byte} {2.5_decimal} {'a'} {7_ulong}\")")]
    public void Every_number_type_and_char_can_be_printed_and_interpolated(string statement)
    {
        var (_, diagnostics) = BindMain(statement);

        Assert.Empty(diagnostics);
    }
}
