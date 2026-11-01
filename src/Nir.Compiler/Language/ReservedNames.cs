namespace Nir.Compiler.Language;

/// <summary>Names that mean something special to the compiler, though they are not all keywords of the grammar.</summary>
public static class ReservedNames
{
    /// <summary>The receiver of a method.</summary>
    public const string SelfValue = "self";

    /// <summary>Inside an <c>impl</c>, the type being implemented.</summary>
    public const string SelfType = "Self";

    /// <summary>The function a program starts in.</summary>
    public const string EntryPoint = "main";

    /// <summary>The field that gives an error its message.</summary>
    public const string MessageField = "message";

    /// <summary>The class that holds the compiled functions; no type may have this name.</summary>
    public const string ProgramClass = "Program";

    /// <summary>A name that takes a value and drops it: <c>let (_, b) = t</c>, <c>_: Square =&gt; ...</c>.</summary>
    public const string Discard = "_";

    /// <summary>The built-in function that writes a line.</summary>
    public const string Print = "print";
}
