namespace Nir.Cli;

/// <summary>A parsed command line: which command, which source file, and the options that go with it.</summary>
internal sealed record CommandLine(string Command, string File, string OutputDirectory, IReadOnlyList<string> ProgramArguments);

internal static class CommandLineArguments
{
    public const string Usage = """
        usage:
          nir build <file.nir> [-o <dir>]     compile to <dir> (default: ./out)
          nir run <file.nir> [-- args...]     compile and run
        """;

    /// <summary>Parses <paramref name="args"/>. On failure returns <c>false</c> and sets <paramref name="error"/>.</summary>
    public static bool TryParse(string[] args, out CommandLine? commandLine, out string error)
    {
        commandLine = null;
        error = string.Empty;

        if (args.Length == 0)
        {
            error = "no command given";
            return false;
        }

        return args[0] switch
        {
            "build" => TryParseBuild(args, out commandLine, out error),
            "run" => TryParseRun(args, out commandLine, out error),
            _ => Fail($"unknown command '{args[0]}'", out error),
        };
    }

    private static bool TryParseBuild(string[] args, out CommandLine? commandLine, out string error)
    {
        commandLine = null;
        error = string.Empty;

        string? file = null;
        var output = "out";

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o")
            {
                if (i + 1 >= args.Length)
                {
                    return Fail("option '-o' needs a directory", out error);
                }

                output = args[++i];
            }
            else if (args[i].StartsWith('-'))
            {
                return Fail($"unknown option '{args[i]}'", out error);
            }
            else if (file is null)
            {
                file = args[i];
            }
            else
            {
                return Fail($"unexpected argument '{args[i]}'", out error);
            }
        }

        if (file is null)
        {
            return Fail("no source file given", out error);
        }

        commandLine = new CommandLine("build", file, output, []);
        return true;
    }

    private static bool TryParseRun(string[] args, out CommandLine? commandLine, out string error)
    {
        commandLine = null;
        error = string.Empty;

        if (args.Length < 2 || args[1].StartsWith('-'))
        {
            return Fail("no source file given", out error);
        }

        var rest = args[2..];
        if (rest.Length > 0 && rest[0] != "--")
        {
            return Fail($"unexpected argument '{rest[0]}'", out error);
        }

        commandLine = new CommandLine("run", args[1], string.Empty, rest.Length > 0 ? rest[1..] : []);
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
