namespace Nir.Compiler.Language;

/// <summary>The start of every import: <c>using clr::cs::System::IO</c> means "the C# view of the .NET library", <c>using std::...</c> Nir's own library.</summary>
public static class ImportRoot
{
    public const string Clr = "clr";

    public const string CSharp = "cs";

    /// <summary><c>clr::cs</c>.</summary>
    public const string Prefix = Clr + Punctuation.PathSeparator + CSharp;

    /// <summary>The start of an import from Nir's own library: <c>using std::types::TypeInfo</c>.</summary>
    public const string Std = "std";

    /// <summary>The <c>std</c> namespace that holds <c>TypeInfo</c>.</summary>
    public const string StdTypes = "types";
}
