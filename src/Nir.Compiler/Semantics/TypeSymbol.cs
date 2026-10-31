using Nir.Compiler.Clr;

namespace Nir.Compiler.Semantics;

/// <summary>A Nir type and the CLR type it compiles to.</summary>
public sealed class TypeSymbol
{
    /// <summary>The "nothing" type <c>()</c>. Compiles to <c>void</c>.</summary>
    public static readonly TypeSymbol UnitType = new("()", ClrUniverse.CoreType("System.Void"));

    public static readonly TypeSymbol IntType = new("int", ClrUniverse.CoreType("System.Int32"));

    public static readonly TypeSymbol DoubleType = new("double", ClrUniverse.CoreType("System.Double"));

    public static readonly TypeSymbol BoolType = new("bool", ClrUniverse.CoreType("System.Boolean"));

    public static readonly TypeSymbol StringType = new("string", ClrUniverse.CoreType("System.String"));

    /// <summary>
    /// The type of an expression that already had an error. Operations on it report nothing more, so one mistake
    /// produces one diagnostic. It never reaches Emit, because a program with errors is not emitted.
    /// </summary>
    public static readonly TypeSymbol ErrorType = new("?", ClrUniverse.CoreType("System.Object"));

    private TypeSymbol(string name, Type clrType)
    {
        Name = name;
        ClrType = clrType;
    }

    /// <summary>The Nir type for a CLR type, or <c>null</c> if Nir cannot use that type yet.</summary>
    internal static TypeSymbol? FromClr(Type clrType) => clrType.FullName switch
    {
        "System.Void" => UnitType,
        "System.Int32" => IntType,
        "System.Double" => DoubleType,
        "System.Boolean" => BoolType,
        "System.String" => StringType,
        _ => null,
    };

    public string Name { get; }

    public Type ClrType { get; }

    public override string ToString() => Name;
}
