using System.Diagnostics;
using Nir.Compiler;
using Nir.Compiler.Diagnostics;

namespace Nir.Cli;

internal static class Commands
{
    public static int Build(CommandLine commandLine)
    {
        if (!TryCompile(commandLine.File, out var result, out var name, out var exitCode))
        {
            return exitCode;
        }

        NirCompiler.Emit(result!, name!, commandLine.OutputDirectory);
        return ExitCodes.Success;
    }

    public static int Run(CommandLine commandLine)
    {
        if (!TryCompile(commandLine.File, out var result, out var name, out var exitCode))
        {
            return exitCode;
        }

        var directory = Path.Combine(Path.GetTempPath(), "nir-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            NirCompiler.Emit(result!, name!, directory);

            var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            startInfo.ArgumentList.Add(Path.Combine(directory, name + ".dll"));
            foreach (var argument in commandLine.ProgramArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start 'dotnet'.");
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Reads and compiles the file. On failure prints the problem and returns the exit code to use.
    private static bool TryCompile(string path, out CompilationResult? result, out string? assemblyName, out int exitCode)
    {
        result = null;
        assemblyName = Path.GetFileNameWithoutExtension(path);

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"nir: file not found: {path}");
            exitCode = ExitCodes.Usage;
            return false;
        }

        if (assemblyName.Length == 0 || !assemblyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            Console.Error.WriteLine($"nir: file name '{assemblyName}' may only contain letters, digits, '_' and '-'");
            exitCode = ExitCodes.Usage;
            return false;
        }

        result = NirCompiler.Compile(new SourceText(path, File.ReadAllText(path)));
        DiagnosticPrinter.Print(Console.Error, [.. result.Diagnostics], TerminalColor.ForError());

        exitCode = result.Succeeded ? ExitCodes.Success : ExitCodes.CompileError;
        return result.Succeeded;
    }
}
