namespace Nir.Cli;

internal static class TerminalColor
{
    /// <summary>
    /// Whether to colour what is written to the error stream: on for a terminal, off when redirected to a file or pipe.
    /// <c>NO_COLOR</c> turns it off and <c>CLICOLOR_FORCE</c> turns it on regardless (see no-color.org, bixense.com/clicolors).
    /// </summary>
    public static bool ForError()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return false;
        }

        var force = Environment.GetEnvironmentVariable("CLICOLOR_FORCE");
        if (!string.IsNullOrEmpty(force) && force != "0")
        {
            return true;
        }

        return !Console.IsErrorRedirected;
    }
}
