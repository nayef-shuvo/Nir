using Nir.Tests.Support;

namespace Nir.Tests.Cli;

public sealed class CliTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nir-cli-tests-" + Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static (string Stdout, string Stderr, int ExitCode) Nir(params string[] arguments) =>
        ProcessRunner.Run("dotnet", [Path.Combine(AppContext.BaseDirectory, "nir.dll"), .. arguments]);

    private string WriteSource(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Build_writes_dll_and_runtimeconfig()
    {
        var source = WriteSource("hello.nir", "fn main() {\n    print(\"hello\")\n}");
        var output = Path.Combine(_directory, "bin");

        var (_, stderr, exitCode) = Nir("build", source, "-o", output);

        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(output, "hello.dll")));
        Assert.True(File.Exists(Path.Combine(output, "hello.runtimeconfig.json")));
    }

    [Fact]
    public void Run_prints_program_output()
    {
        var source = WriteSource("hello.nir", "fn main() {\n    print(\"hello\")\n    print(\"world\")\n}");

        var (stdout, stderr, exitCode) = Nir("run", source);

        Assert.Equal("hello\nworld\n", stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Compile_error_prints_formatted_diagnostic_and_exits_1()
    {
        var source = WriteSource("bad.nir", "fn main() {\n    prnt(\"x\")\n}");

        var (stdout, stderr, exitCode) = Nir("run", source);

        Assert.Equal(string.Empty, stdout);
        Assert.Equal(
            $"error[NIR2001]: 'prnt' is not defined\n" +
            $" --> {source}:2:5\n" +
            "  |\n" +
            "2 |     prnt(\"x\")\n" +
            "  |     ^^^^ 'prnt' is not defined\n" +
            "\n" +
            "error: aborting due to 1 previous error\n",
            stderr);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Missing_file_exits_2()
    {
        var (_, stderr, exitCode) = Nir("run", Path.Combine(_directory, "nope.nir"));

        Assert.Contains("file not found", stderr, StringComparison.Ordinal);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Unknown_command_exits_2_and_prints_usage()
    {
        var (_, stderr, exitCode) = Nir("frobnicate");

        Assert.Contains("unknown command 'frobnicate'", stderr, StringComparison.Ordinal);
        Assert.Contains("usage:", stderr, StringComparison.Ordinal);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void No_arguments_exits_2_and_prints_usage()
    {
        var (_, stderr, exitCode) = Nir();

        Assert.Contains("usage:", stderr, StringComparison.Ordinal);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void File_name_with_invalid_characters_exits_2()
    {
        var source = WriteSource("bad name.nir", "fn main() {\n}");

        var (_, stderr, exitCode) = Nir("run", source);

        Assert.Contains("may only contain", stderr, StringComparison.Ordinal);
        Assert.Equal(2, exitCode);
    }
}
