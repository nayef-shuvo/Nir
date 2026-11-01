using Nir.Compiler.Clr;
using Nir.Compiler.Language;

namespace Nir.Compiler.Semantics;

/// <summary>A Nir type and the CLR type it compiles to.</summary>
public class TypeSymbol
{
    /// <summary>The "nothing" type <c>()</c>. Compiles to <c>void</c>.</summary>
    public static readonly TypeSymbol UnitType = new(TypeNames.UnitType, ClrTypes.Void);

    public static readonly TypeSymbol ByteType = new(TypeNames.ByteType, ClrTypes.Byte);

    public static readonly TypeSymbol SByteType = new(TypeNames.SByteType, ClrTypes.SByte);

    public static readonly TypeSymbol ShortType = new(TypeNames.ShortType, ClrTypes.Int16);

    public static readonly TypeSymbol UShortType = new(TypeNames.UShortType, ClrTypes.UInt16);

    public static readonly TypeSymbol IntType = new(TypeNames.IntType, ClrTypes.Int32);

    public static readonly TypeSymbol UIntType = new(TypeNames.UIntType, ClrTypes.UInt32);

    public static readonly TypeSymbol LongType = new(TypeNames.LongType, ClrTypes.Int64);

    public static readonly TypeSymbol ULongType = new(TypeNames.ULongType, ClrTypes.UInt64);

    public static readonly TypeSymbol FloatType = new(TypeNames.FloatType, ClrTypes.Single);

    public static readonly TypeSymbol DoubleType = new(TypeNames.DoubleType, ClrTypes.Double);

    public static readonly TypeSymbol DecimalType = new(TypeNames.DecimalType, ClrTypes.Decimal);

    public static readonly TypeSymbol CharType = new(TypeNames.CharType, ClrTypes.Char);

    public static readonly TypeSymbol BoolType = new(TypeNames.BoolType, ClrTypes.Boolean);

    public static readonly TypeSymbol StringType = new(TypeNames.StringType, ClrTypes.String);

    /// <summary>
    /// The type of an expression that already had an error. Operations on it report nothing more, so one mistake
    /// produces one diagnostic. It never reaches Emit, because a program with errors is not emitted.
    /// </summary>
    public static readonly TypeSymbol ErrorType = new(TypeNames.UnknownType, ClrTypes.Object);

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
        ClrNames.Void => UnitType,
        ClrNames.Byte => ByteType,
        ClrNames.SByte => SByteType,
        ClrNames.Int16 => ShortType,
        ClrNames.UInt16 => UShortType,
        ClrNames.Int32 => IntType,
        ClrNames.UInt32 => UIntType,
        ClrNames.Int64 => LongType,
        ClrNames.UInt64 => ULongType,
        ClrNames.Single => FloatType,
        ClrNames.Double => DoubleType,
        ClrNames.Decimal => DecimalType,
        ClrNames.Char => CharType,
        ClrNames.Boolean => BoolType,
        ClrNames.String => StringType,
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
