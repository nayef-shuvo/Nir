namespace Nir.Compiler.Diagnostics;

/// <summary>Every diagnostic code. Codes are never reused or renumbered; see docs/architecture.md section 4.</summary>
public static class DiagnosticCodes
{
    /// <summary>A character that cannot start any token.</summary>
    public const string UnexpectedCharacter = "NIR0001";

    /// <summary>A string literal reached a newline or the end of the file before its closing quote.</summary>
    public const string UnterminatedString = "NIR0002";

    /// <summary>Any error reported by the parser.</summary>
    public const string SyntaxError = "NIR1001";

    /// <summary>A name that is not declared.</summary>
    public const string UndefinedName = "NIR2001";

    /// <summary>The program has no <c>main</c> function.</summary>
    public const string MissingEntryPoint = "NIR2002";

    /// <summary>Two functions with the same name.</summary>
    public const string DuplicateFunction = "NIR2003";

    /// <summary>A call with the wrong number of arguments.</summary>
    public const string WrongArgumentCount = "NIR2004";

    /// <summary>A value of one type used where another type is required.</summary>
    public const string TypeMismatch = "NIR3001";
}
