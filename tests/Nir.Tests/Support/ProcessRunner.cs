using System.Diagnostics;

namespace Nir.Tests.Support;

internal static class ProcessRunner
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>Runs a process to completion and returns its output (line endings normalized to <c>\n</c>) and exit code.</summary>
    public static (string Stdout, string Stderr, int ExitCode) Run(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(_timeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"'{fileName}' did not finish within {_timeout.TotalSeconds} seconds.");
        }

        return (Normalize(stdout.Result), Normalize(stderr.Result), process.ExitCode);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
