namespace Nir.Compiler.Language;

/// <summary>The start of every import: <c>using clr::cs::System::IO</c> means "the C# view of the .NET library".</summary>
public static class ImportRoot
{
    public const string Clr = "clr";

    public const string CSharp = "cs";

    /// <summary><c>clr::cs</c>.</summary>
    public const string Prefix = Clr + Punctuation.PathSeparator + CSharp;
}
