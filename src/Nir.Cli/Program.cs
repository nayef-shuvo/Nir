namespace Nir.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (!CommandLineArguments.TryParse(args, out var commandLine, out var error))
            {
                Console.Error.WriteLine($"nir: {error}");
                Console.Error.WriteLine(CommandLineArguments.Usage);
                return ExitCodes.Usage;
            }

            return commandLine!.Command == CommandNames.Build ? Commands.Build(commandLine) : Commands.Run(commandLine);
        }
#pragma warning disable CA1031 // The top-level handler turns any compiler bug into exit code 3 instead of a raw crash.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"internal compiler error: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
            return ExitCodes.InternalError;
        }
    }
}
