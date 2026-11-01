namespace Nir.Compiler.Clr;

/// <summary>Full names of the .NET types and settings the compiler refers to.</summary>
internal static class ClrNames
{
    public const string CoreLibrary = "System.Private.CoreLib";

    public const string Void = "System.Void";

    public const string Byte = "System.Byte";

    public const string SByte = "System.SByte";

    public const string Int16 = "System.Int16";

    public const string UInt16 = "System.UInt16";

    public const string Int32 = "System.Int32";

    public const string UInt32 = "System.UInt32";

    public const string Int64 = "System.Int64";

    public const string UInt64 = "System.UInt64";

    public const string Single = "System.Single";

    public const string Double = "System.Double";

    public const string Decimal = "System.Decimal";

    public const string Char = "System.Char";

    public const string Boolean = "System.Boolean";

    public const string String = "System.String";

    public const string Object = "System.Object";

    public const string ValueType = "System.ValueType";

    public const string Exception = "System.Exception";

    public const string Console = "System.Console";

    public const string TextWriter = "System.IO.TextWriter";

    /// <summary>The start of <c>System.ValueTuple`1</c> ... <c>`8</c>; the arity follows.</summary>
    public const string ValueTuplePrefix = "System.ValueTuple`";

    /// <summary>The runtime setting that turns culture data off, so output does not depend on the machine's locale.</summary>
    public const string InvariantGlobalization = "System.Globalization.Invariant";
}
