using Nir.Tests.Support;

namespace Nir.Tests.Emit;

public class EmitterTests
{
    [Fact]
    public void Hello_prints_text_and_newline()
    {
        var (stdout, stderr, exitCode) = TestHost.Run("fn main() {\n    print(\"hello\")\n}");

        Assert.Equal("hello\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Statements_run_in_order()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(\"a\")\n    print(\"b\")\n    print(\"c\")\n}");

        Assert.Equal("a\nb\nc\n", stdout);
    }

    [Fact]
    public void Function_call_runs_callee()
    {
        var (stdout, _, _) = TestHost.Run("fn greet() {\n    print(\"hi\")\n}\nfn main() {\n    greet()\n    greet()\n}");

        Assert.Equal("hi\nhi\n", stdout);
    }

    [Fact]
    public void Forward_call_works()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    later()\n}\nfn later() {\n    print(\"late\")\n}");

        Assert.Equal("late\n", stdout);
    }

    [Fact]
    public void Empty_main_prints_nothing()
    {
        var (stdout, _, exitCode) = TestHost.Run("fn main() {\n}");

        Assert.Equal(string.Empty, stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Two_semicolons_on_one_line()
    {
        var (stdout, _, _) = TestHost.Run("fn main() { print(\"a\"); print(\"b\") }");

        Assert.Equal("a\nb\n", stdout);
    }

    [Fact]
    public void Runtimeconfig_requests_invariant_globalization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nir-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = TestHost.Compile("fn main() {\n}");
            Nir.Compiler.NirCompiler.Emit(result, "program", directory);

            var json = File.ReadAllText(Path.Combine(directory, "program.runtimeconfig.json"));

            Assert.Contains("\"System.Globalization.Invariant\":true", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Escapes_in_strings_are_decoded()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(\"a\\nb\")\n}");

        Assert.Equal("a\nb\n", stdout);
    }

    [Fact]
    public void Prints_int()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(10)\n    print(2147483647)\n}");

        Assert.Equal("10\n2147483647\n", stdout);
    }

    [Fact]
    public void Prints_double()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(2.5)\n    print(10.0)\n}");

        Assert.Equal("2.5\n10\n", stdout);
    }

    [Fact]
    public void Prints_bool()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    print(true)\n    print(false)\n}");

        Assert.Equal("True\nFalse\n", stdout);
    }

    private static readonly (string Expression, string Expected)[] _operations =
    [
        ("1 + 2", "3"), ("7 - 10", "-3"), ("6 * 7", "42"), ("10 / 3", "3"), ("-7 / 2", "-3"), ("-7 % 3", "-1"), ("10 % 3", "1"),
        ("-5", "-5"), ("- -5", "5"), ("2 + 3 * 4", "14"), ("(2 + 3) * 4", "20"), ("10 - 2 - 3", "5"),
        ("-2147483647 - 1", "-2147483648"), ("2147483647 - 2147483647", "0"),
        ("1.5 + 2.25", "3.75"), ("10.0 / 4.0", "2.5"), ("7.5 % 2.0", "1.5"), ("-2.5", "-2.5"), ("0.5 * 3.0", "1.5"),
        ("1.0 / 0.0", "Infinity"), ("-1.0 / 0.0", "-Infinity"), ("0.0 / 0.0", "NaN"),
        ("1 < 2", "True"), ("2 < 2", "False"), ("2 <= 2", "True"), ("3 <= 2", "False"),
        ("3 > 2", "True"), ("2 > 2", "False"), ("2 >= 2", "True"), ("2 >= 3", "False"),
        ("1 == 1", "True"), ("1 == 2", "False"), ("1 != 1", "False"), ("1 != 2", "True"),
        ("1.0 == 1.0", "True"), ("1.5 < 2.5", "True"), ("2.5 <= 2.5", "True"), ("2.5 >= 2.6", "False"),
        ("true == false", "False"), ("true != false", "True"), ("true == true", "True"),
        ("0.0 / 0.0 == 0.0 / 0.0", "False"), ("0.0 / 0.0 != 0.0 / 0.0", "True"),
        ("0.0 / 0.0 <= 1.0", "False"), ("0.0 / 0.0 >= 1.0", "False"), ("0.0 / 0.0 < 1.0", "False"), ("0.0 / 0.0 > 1.0", "False"),
        ("1 + 2 < 4", "True"), ("1 < 2 == true", "True"),
    ];

    [Fact]
    public void Operators_compute_the_expected_values()
    {
        var program = "fn main() {\n" + string.Join("\n", _operations.Select(o => $"    print({o.Expression})")) + "\n}";

        var (stdout, stderr, exitCode) = TestHost.Run(program);

        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        // Compare line by line so a failure names the expression that went wrong.
        var lines = stdout.TrimEnd('\n').Split('\n');
        for (var i = 0; i < _operations.Length; i++)
        {
            Assert.True(_operations[i].Expected == lines[i], $"{_operations[i].Expression}: expected {_operations[i].Expected} but got {lines[i]}");
        }
    }

    [Theory]
    [InlineData("2147483647 + 1", "OverflowException")]
    [InlineData("-2147483647 - 2", "OverflowException")]
    [InlineData("65536 * 65536", "OverflowException")]
    [InlineData("1 / 0", "DivideByZeroException")]
    [InlineData("1 % 0", "DivideByZeroException")]
    public void Integer_failures_crash_the_program(string expression, string exception)
    {
        var (stdout, stderr, exitCode) = TestHost.Run($"fn main() {{\n    print(1)\n    print({expression})\n}}");

        Assert.Equal("1\n", stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains(exception, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Variable_holds_its_value()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    let x = 10\n    print(x)\n}");

        Assert.Equal("10\n", stdout);
    }

    [Fact]
    public void Mutable_variable_can_be_reassigned()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    let mut x = 1\n    print(x)\n    x = x + 1\n    x = x * 10\n    print(x)\n}");

        Assert.Equal("1\n20\n", stdout);
    }

    [Fact]
    public void Variables_of_every_type()
    {
        var (stdout, _, _) = TestHost.Run(
            "fn main() {\n    let i = 7\n    let d: double = 2.5\n    let b = 1 < 2\n    let s = \"text\"\n    print(i)\n    print(d)\n    print(b)\n    print(s)\n}");

        Assert.Equal("7\n2.5\nTrue\ntext\n", stdout);
    }

    [Fact]
    public void Variables_combine_in_expressions()
    {
        var (stdout, _, _) = TestHost.Run("fn main() {\n    let a = 6\n    let b = 7\n    let mut c = a * b\n    c = c - a\n    print(c)\n    print(c > a)\n}");

        Assert.Equal("36\nTrue\n", stdout);
    }

    [Fact]
    public void Each_function_has_its_own_variables()
    {
        var (stdout, _, _) = TestHost.Run(
            "fn other() {\n    let x = 1\n    print(x)\n}\nfn main() {\n    let x = 2\n    other()\n    print(x)\n}");

        Assert.Equal("1\n2\n", stdout);
    }

    [Fact]
    public void Failing_arithmetic_in_an_initializer_crashes()
    {
        var (stdout, stderr, exitCode) = TestHost.Run("fn main() {\n    let x = 2147483647\n    let y = x + 1\n    print(y)\n}");

        Assert.Equal(string.Empty, stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("OverflowException", stderr, StringComparison.Ordinal);
    }

    private static string Run(params string[] lines) => TestHost.Run(string.Join("\n", lines) + "\n").Stdout;

    [Fact]
    public void Function_returns_its_result()
    {
        Assert.Equal("5\n", Run(
            "fn add(a: int, b: int) -> int {",
            "    return a + b",
            "}",
            "fn main() {",
            "    print(add(2, 3))",
            "}"));
    }

    [Fact]
    public void Every_type_can_be_returned()
    {
        Assert.Equal("7\n2.5\nTrue\ntext\n", Run(
            "fn i() -> int {\n    return 7\n}",
            "fn d() -> double {\n    return 2.5\n}",
            "fn b() -> bool {\n    return true\n}",
            "fn s() -> string {\n    return \"text\"\n}",
            "fn main() {\n    print(i())\n    print(d())\n    print(b())\n    print(s())\n}"));
    }

    [Fact]
    public void Calls_can_be_nested()
    {
        Assert.Equal("10\n", Run(
            "fn add(a: int, b: int) -> int {\n    return a + b\n}",
            "fn main() {\n    print(add(add(1, 2), add(3, 4)))\n}"));
    }

    [Fact]
    public void Arguments_are_evaluated_left_to_right()
    {
        Assert.Equal("first\nsecond\n3\n", Run(
            "fn add(a: int, b: int) -> int {\n    return a + b\n}",
            "fn first() -> int {\n    print(\"first\")\n    return 1\n}",
            "fn second() -> int {\n    print(\"second\")\n    return 2\n}",
            "fn main() {\n    print(add(first(), second()))\n}"));
    }

    [Fact]
    public void Parameters_of_different_types()
    {
        Assert.Equal("label\n42\nTrue\n", Run(
            "fn show(label: string, n: int, flag: bool) {\n    print(label)\n    print(n)\n    print(flag)\n}",
            "fn main() {\n    show(\"label\", 42, true)\n}"));
    }

    [Fact]
    public void Parameters_work_in_expressions_and_variables()
    {
        Assert.Equal("6\n12\n", Run(
            "fn area(w: double, h: double) -> double {\n    let result = w * h\n    return result\n}",
            "fn twice(n: int) -> int {\n    let mut total = n\n    total = total + n\n    return total\n}",
            "fn main() {\n    print(area(2.0, 3.0))\n    print(twice(6))\n}"));
    }

    [Fact]
    public void Arguments_are_copies()
    {
        Assert.Equal("1\n2\n", Run(
            "fn show(x: int) {\n    print(x)\n}",
            "fn main() {\n    let mut v = 1\n    show(v)\n    v = 2\n    show(v)\n}"));
    }

    [Fact]
    public void Functions_call_each_other_in_any_order()
    {
        Assert.Equal("a\nb\nc\n", Run(
            "fn main() {\n    a()\n}",
            "fn c() {\n    print(\"c\")\n}",
            "fn a() {\n    print(\"a\")\n    b()\n}",
            "fn b() {\n    print(\"b\")\n    c()\n}"));
    }

    [Fact]
    public void Bare_return_leaves_a_unit_function_early()
    {
        Assert.Equal("1\n", Run(
            "fn f() {\n    print(1)\n    return\n    print(2)\n}",
            "fn main() {\n    f()\n}"));
    }

    [Fact]
    public void Code_after_a_value_return_does_not_run()
    {
        Assert.Equal("1\n", Run(
            "fn f() -> int {\n    return 1\n    print(99)\n    return 2\n}",
            "fn main() {\n    print(f())\n}"));
    }

    [Fact]
    public void Returning_a_unit_call_from_a_unit_function()
    {
        Assert.Equal("hi\n", Run(
            "fn g() {\n    print(\"hi\")\n}",
            "fn f() {\n    return g()\n}",
            "fn main() {\n    f()\n}"));
    }

    [Fact]
    public void Result_can_be_discarded_or_stored()
    {
        Assert.Equal("noisy\nnoisy\n5\n", Run(
            "fn noisy() -> int {\n    print(\"noisy\")\n    return 5\n}",
            "fn main() {\n    noisy()\n    let x = noisy()\n    print(x)\n}"));
    }

    [Fact]
    public void Same_variable_name_in_different_functions()
    {
        Assert.Equal("1\n2\n", Run(
            "fn f(x: int) -> int {\n    let y = x\n    return y\n}",
            "fn main() {\n    let x = 2\n    let y = f(1)\n    print(y)\n    print(x)\n}"));
    }

    [Fact]
    public void Recursive_function_compiles_and_runs_when_the_recursion_is_not_reached()
    {
        // Real recursion needs `if` (slice 5); this only proves a function can contain a call to itself.
        Assert.Equal("done\n", Run(
            "fn loop_forever(n: int) -> int {\n    return loop_forever(n + 1)\n}",
            "fn main() {\n    print(\"done\")\n}"));
    }
}

