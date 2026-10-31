namespace Nir.Compiler.Diagnostics;

/// <summary>Every diagnostic code. Codes are never reused or renumbered; see docs/architecture.md section 4.</summary>
public static class DiagnosticCodes
{
    /// <summary>A character that cannot start any token.</summary>
    public const string UnexpectedCharacter = "NIR0001";

    /// <summary>A backslash in a string followed by a character that is not an escape.</summary>
    public const string UnknownEscape = "NIR0003";

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

    /// <summary>A name declared twice in the same block.</summary>
    public const string AlreadyDeclared = "NIR2005";

    /// <summary>A type annotation that names no type.</summary>
    public const string UnknownType = "NIR2006";

    /// <summary><c>main</c> declared with parameters or a return type.</summary>
    public const string InvalidEntryPoint = "NIR2007";

    /// <summary>An import that does not start with <c>clr::cs</c> or names nothing below it.</summary>
    public const string InvalidImport = "NIR2008";

    /// <summary>An import of a namespace or type that does not exist.</summary>
    public const string ImportNotFound = "NIR2009";

    /// <summary>The same local name imported twice.</summary>
    public const string DuplicateImport = "NIR2010";

    /// <summary>A name after <c>::</c> or <c>.</c> that the namespace, type or value does not have.</summary>
    public const string NoSuchMember = "NIR2011";

    /// <summary>A value of one type used where another type is required.</summary>
    public const string TypeMismatch = "NIR3001";

    /// <summary>Assignment to a variable declared without <c>mut</c>.</summary>
    public const string NotMutable = "NIR3002";

    /// <summary>An operator used with operand types it does not support.</summary>
    public const string InvalidOperator = "NIR3003";

    /// <summary>A literal whose value does not fit its type.</summary>
    public const string LiteralOutOfRange = "NIR3004";

    /// <summary>A variable whose initializer has no value, so it would have type <c>()</c>.</summary>
    public const string UnitVariable = "NIR3005";

    /// <summary>A function that must return a value but does not end with a return.</summary>
    public const string MissingReturn = "NIR3007";

    /// <summary><c>return value</c> in a function that returns <c>()</c>.</summary>
    public const string ReturnValueInUnitFunction = "NIR3008";

    /// <summary>A bare <c>return</c> in a function that must return a value.</summary>
    public const string MissingReturnValue = "NIR3009";

    /// <summary><c>break</c> or <c>continue</c> outside a loop.</summary>
    public const string JumpOutsideLoop = "NIR3010";

    /// <summary>A value of type <c>()</c> used inside <c>{ }</c> of an interpolated string.</summary>
    public const string UnitInInterpolation = "NIR3012";

    /// <summary>A .NET method whose return type Nir cannot use yet.</summary>
    public const string UnsupportedClrType = "NIR3011";

    /// <summary>No overload of a function accepts the given argument types.</summary>
    public const string NoMatchingOverload = "NIR3006";
}
