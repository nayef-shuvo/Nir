using Nir.Compiler.Clr;

namespace Nir.Compiler.Semantics;

/// <summary>A Nir type and the CLR type it compiles to.</summary>
public class TypeSymbol
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
        _clrType = clrType;
    }

    /// <summary>For types whose CLR type does not exist until Emit creates it (structs).</summary>
    private protected TypeSymbol(string name)
    {
        Name = name;
    }

    private readonly Type? _clrType;

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

    /// <summary>The CLR type this compiles to. A struct has none until Emit defines it, so asking is a bug.</summary>
    public virtual Type ClrType =>
        _clrType ?? throw new InvalidOperationException($"'{Name}' has no CLR type of its own; the emitter maps it.");

    public override string ToString() => Name;

    // Types are the same type when they are the same symbol, except tuples, which are the same when their elements are.
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public static bool operator ==(TypeSymbol? left, TypeSymbol? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(TypeSymbol? left, TypeSymbol? right) => !(left == right);
}
