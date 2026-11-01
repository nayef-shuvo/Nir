namespace Nir.Compiler.Language;

/// <summary>The names of the built-in types, as they are written in Nir source and shown in messages.</summary>
public static class TypeNames
{
    /// <summary>The type of "nothing": a function with no result returns it.</summary>
    public const string UnitType = "()";

    public const string IntType = "int";

    public const string DoubleType = "double";

    public const string BoolType = "bool";

    public const string StringType = "string";

    /// <summary>The type of any error.</summary>
    public const string AnyErrorType = "anyerror";

    /// <summary>The type of an expression that already had an error (never written in source).</summary>
    public const string UnknownType = "?";
}
