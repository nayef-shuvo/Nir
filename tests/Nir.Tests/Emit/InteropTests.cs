using Nir.Tests.Support;

namespace Nir.Tests.Emit;

/// <summary>Programs that call real .NET code. The expected output was checked against the .NET documentation, not against Nir.</summary>
public class InteropTests
{
    private static (string Stdout, string Stderr, int ExitCode) Run(params string[] lines) =>
        TestHost.Run(string.Join("\n", lines) + "\n");

    [Fact]
    public void Math_functions_with_exact_types()
    {
        var (stdout, stderr, exitCode) = Run(
            "using clr::cs::System::Math",
            "fn main() {",
            "    print(Math::Sqrt(16.0))",
            "    print(Math::Max(3, 7))",
            "    print(Math::Max(2.5, 1.5))",
            "    print(Math::Pow(2.0, 10.0))",
            "    print(Math::Abs(-5))",
            "    print(Math::Abs(-2.5))",
            "}");

        Assert.Equal("4\n7\n2.5\n1024\n5\n2.5\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Static_methods_of_core_types()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System::{Int32, String}",
            "fn main() {",
            "    print(Int32::Parse(\"42\") + 1)",
            "    print(String::Concat(\"a\", \"b\"))",
            "    print(String::IsNullOrEmpty(\"\"))",
            "    print(String::IsNullOrEmpty(\"x\"))",
            "}");

        Assert.Equal("43\nab\nTrue\nFalse\n", stdout);
    }

    [Fact]
    public void Namespace_import_walks_to_a_type()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System::IO",
            "fn main() {",
            "    print(IO::Path::GetFileName(\"dir/file.txt\"))",
            "    print(IO::Path::GetExtension(\"dir/file.txt\"))",
            "}");

        Assert.Equal("file.txt\n.txt\n", stdout);
    }

    [Fact]
    public void Importing_System_allows_a_fully_qualified_call()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System",
            "fn main() {",
            "    print(System::Math::Sqrt(9.0))",
            "}");

        Assert.Equal("3\n", stdout);
    }

    [Fact]
    public void Group_import_and_console_output()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System::{Console, Math}",
            "fn main() {",
            "    Console::WriteLine(\"through Console\")",
            "    Console::WriteLine(5)",
            "    Console::WriteLine(Math::Sqrt(4.0))",
            "}");

        Assert.Equal("through Console\n5\n2\n", stdout);
    }

    [Fact]
    public void File_round_trip_through_a_temp_path()
    {
        var name = "nir-interop-" + Guid.NewGuid().ToString("N") + ".txt";
        var (stdout, stderr, exitCode) = Run(
            "using clr::cs::System::IO",
            "fn main() {",
            $"    let path = IO::Path::Combine(IO::Path::GetTempPath(), \"{name}\")",
            "    print(IO::File::Exists(path))",
            "    IO::File::WriteAllText(path, \"stored text\")",
            "    print(IO::File::Exists(path))",
            "    print(IO::File::ReadAllText(path))",
            "    IO::File::Delete(path)",
            "    print(IO::File::Exists(path))",
            "}");

        Assert.Equal("False\nTrue\nstored text\nFalse\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Results_of_dotnet_calls_work_in_expressions_and_variables()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System::{Math, Int32}",
            "fn hypotenuse(a: double, b: double) -> double {",
            "    return Math::Sqrt(a * a + b * b)",
            "}",
            "fn main() {",
            "    let n = Int32::Parse(\"7\")",
            "    print(n * n)",
            "    print(hypotenuse(3.0, 4.0))",
            "    print(Math::Max(n, 10) > 9)",
            "}");

        Assert.Equal("49\n5\nTrue\n", stdout);
    }

    [Fact]
    public void An_exception_from_dotnet_code_is_not_hidden()
    {
        var (stdout, stderr, exitCode) = Run(
            "using clr::cs::System::IO",
            "fn main() {",
            "    print(\"before\")",
            "    print(IO::File::ReadAllText(\"/nir-no-such-folder/missing.txt\"))",
            "}");

        Assert.Equal("before\n", stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("DirectoryNotFoundException", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_below_the_functions_apply_too()
    {
        var (stdout, _, _) = Run(
            "fn main() {",
            "    print(Math::Sqrt(25.0))",
            "}",
            "using clr::cs::System::Math");

        Assert.Equal("5\n", stdout);
    }

    [Fact]
    public void String_methods()
    {
        var (stdout, stderr, exitCode) = Run(
            "fn main() {",
            "    print(\"hello\".ToUpper())",
            "    print(\"  hi \".Trim().ToUpper())",
            "    print(\"abc\".Contains(\"b\"))",
            "    print(\"abc\".Contains(\"z\"))",
            "    print(\"abcdef\".Substring(2))",
            "    print(\"abcdef\".Substring(1, 3))",
            "    print(\"abc\".IndexOf(\"c\"))",
            "    print(\"a-b-c\".Replace(\"-\", \"+\"))",
            "    print(\"MiXed\".ToLower().ToUpper())",
            "}");

        Assert.Equal("HELLO\nHI\nTrue\nFalse\ncdef\nbcd\n2\na+b+c\nMIXED\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Methods_on_numbers_and_booleans()
    {
        var (stdout, _, _) = Run(
            "fn main() {",
            "    print(42.ToString())",
            "    print(2.5.ToString())",
            "    print(0.5.ToString())",
            "    print(true.ToString())",
            "    print((1 + 2).ToString())",
            "    print(10.CompareTo(5))",
            "    print(5.CompareTo(10))",
            "}");

        Assert.Equal("42\n2.5\n0.5\nTrue\n3\n1\n-1\n", stdout);
    }

    [Fact]
    public void Receivers_can_be_variables_parameters_and_call_results()
    {
        var (stdout, _, _) = Run(
            "using clr::cs::System::Int32",
            "fn shout(s: string) -> string {",
            "    return s.ToUpper()",
            "}",
            "fn main() {",
            "    let word = \"quiet\"",
            "    print(shout(word))",
            "    print(word.Trim().Substring(1))",
            "    print(Int32::Parse(\"123\").ToString().Substring(1))",
            "    let n = 77",
            "    print(n.ToString())",
            "}");

        Assert.Equal("QUIET\nuiet\n23\n77\n", stdout);
    }

    [Fact]
    public void Receiver_is_evaluated_before_the_arguments()
    {
        var (stdout, _, _) = Run(
            "fn receiver() -> string {",
            "    print(\"receiver\")",
            "    return \"abc\"",
            "}",
            "fn argument() -> string {",
            "    print(\"argument\")",
            "    return \"b\"",
            "}",
            "fn main() {",
            "    print(receiver().Contains(argument()))",
            "}");

        Assert.Equal("receiver\nargument\nTrue\n", stdout);
    }

    [Fact]
    public void Discarded_result_leaves_the_stack_balanced()
    {
        var (stdout, _, exitCode) = Run(
            "fn main() {",
            "    \"abc\".ToUpper()",
            "    42.ToString()",
            "    print(\"ok\")",
            "}");

        Assert.Equal("ok\n", stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void An_exception_from_an_instance_method_is_not_hidden()
    {
        var (stdout, stderr, exitCode) = Run(
            "fn main() {",
            "    print(\"before\")",
            "    print(\"abc\".Substring(10))",
            "}");

        Assert.Equal("before\n", stdout);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("ArgumentOutOfRangeException", stderr, StringComparison.Ordinal);
    }
}

