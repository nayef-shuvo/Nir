using Nir.Compiler;
using Nir.Compiler.Diagnostics;

namespace Nir.Tests.Support;

/// <summary>Compiles Nir source and runs the result as a real .NET process.</summary>
internal static class TestHost
{
    public static CompilationResult Compile(string source, string name = "test.nir") =>
        NirCompiler.Compile(new SourceText(name, source));

    /// <summary>Compiles, emits to a temp directory, runs with <c>dotnet</c>, and returns the output. Fails if compilation fails.</summary>
    public static (string Stdout, string Stderr, int ExitCode) Run(string source)
    {
        var result = Compile(source);
        Assert.True(result.Succeeded, "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));

        var directory = Path.Combine(Path.GetTempPath(), "nir-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            NirCompiler.Emit(result, "program", directory);
            return ProcessRunner.Run("dotnet", [Path.Combine(directory, "program.dll")]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
