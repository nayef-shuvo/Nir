namespace Nir.Cli;

/// <summary>The words of the <c>nir</c> command line.</summary>
internal static class CommandNames
{
    public const string Build = "build";

    public const string Run = "run";

    /// <summary>The option that names the output directory of <c>build</c>.</summary>
    public const string OutputOption = "-o";

    /// <summary>Ends the options of <c>run</c>; what follows is passed to the program.</summary>
    public const string ProgramArgumentsSeparator = "--";
}
