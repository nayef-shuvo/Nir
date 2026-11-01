using System.Globalization;

namespace Nir.Compiler.Semantics;

/// <summary>
/// What kind of number a primitive type is. This is the one place that knows which types are integers, which are signed and
/// how wide they are; the binder and the emitter ask here instead of listing types themselves.
/// </summary>
internal static class NumberTypes
{
    /// <summary>The integer types, narrowest first, signed before unsigned of the same width.</summary>
    public static readonly IReadOnlyList<TypeSymbol> Integers =
    [
        TypeSymbol.SByteType, TypeSymbol.ByteType, TypeSymbol.ShortType, TypeSymbol.UShortType,
        TypeSymbol.IntType, TypeSymbol.UIntType, TypeSymbol.LongType, TypeSymbol.ULongType,
    ];

    public static bool IsInteger(TypeSymbol type) => Integers.Contains(type);

    /// <summary>A signed integer.</summary>
    public static bool IsSignedInteger(TypeSymbol type) =>
        type == TypeSymbol.SByteType || type == TypeSymbol.ShortType || type == TypeSymbol.IntType || type == TypeSymbol.LongType;

    /// <summary>An unsigned integer, which does not include <c>char</c> (see <see cref="IsUnsignedOnStack"/>).</summary>
    public static bool IsUnsignedInteger(TypeSymbol type) => IsInteger(type) && !IsSignedInteger(type);

    /// <summary><c>float</c> or <c>double</c>.</summary>
    public static bool IsFloatingPoint(TypeSymbol type) => type == TypeSymbol.FloatType || type == TypeSymbol.DoubleType;

    public static bool IsDecimal(TypeSymbol type) => type == TypeSymbol.DecimalType;

    public static bool IsChar(TypeSymbol type) => type == TypeSymbol.CharType;

    /// <summary>Any type that has arithmetic: the integers, <c>float</c>, <c>double</c> and <c>decimal</c>. Not <c>char</c>.</summary>
    public static bool IsNumber(TypeSymbol type) => IsInteger(type) || IsFloatingPoint(type) || IsDecimal(type);

    /// <summary>Types a range <c>a..b</c> can run over: the integers and <c>char</c>.</summary>
    public static bool IsCountable(TypeSymbol type) => IsInteger(type) || IsChar(type);

    /// <summary>Types that can be ordered with <c>&lt;</c>, <c>&gt;</c>: every number and <c>char</c>.</summary>
    public static bool IsOrdered(TypeSymbol type) => IsNumber(type) || IsChar(type);

    /// <summary>The width in bits of an integer type or <c>char</c>.</summary>
    public static int Bits(TypeSymbol type) =>
        type == TypeSymbol.SByteType || type == TypeSymbol.ByteType ? 8
        : type == TypeSymbol.ShortType || type == TypeSymbol.UShortType || type == TypeSymbol.CharType ? 16
        : type == TypeSymbol.IntType || type == TypeSymbol.UIntType ? 32
        : 64;

    /// <summary>
    /// True when a value of this type is read as unsigned by the CLR's unsigned instructions (<c>div.un</c>, <c>clt.un</c>, ...):
    /// <c>uint</c> and <c>ulong</c>. The 8 and 16-bit unsigned types and <c>char</c> sit zero-extended on the stack, so the signed
    /// instructions are already right for them.
    /// </summary>
    public static bool IsUnsignedOnStack(TypeSymbol type) => type == TypeSymbol.UIntType || type == TypeSymbol.ULongType;

    /// <summary>True for the types narrower than 32 bits, whose arithmetic happens in 32 bits and must be narrowed again.</summary>
    public static bool IsSmallInteger(TypeSymbol type) => IsInteger(type) && Bits(type) < 32;

    /// <summary>
    /// Reads a literal's digits (optionally with a leading <c>-</c> and a fraction) as a value of <paramref name="type"/>.
    /// Null if the text does not fit the type. A whole-number text is never read as a fraction type here; the caller decides that.
    /// </summary>
    public static object? Parse(string text, TypeSymbol type)
    {
        var integer = NumberStyles.AllowLeadingSign;
        var fraction = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        var culture = CultureInfo.InvariantCulture;

        if (type == TypeSymbol.ByteType) { return byte.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.SByteType) { return sbyte.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.ShortType) { return short.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.UShortType) { return ushort.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.IntType) { return int.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.UIntType) { return uint.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.LongType) { return long.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.ULongType) { return ulong.TryParse(text, integer, culture, out var v) ? v : null; }
        if (type == TypeSymbol.FloatType) { return float.TryParse(text, fraction, culture, out var v) && float.IsFinite(v) ? v : null; }
        if (type == TypeSymbol.DoubleType) { return double.TryParse(text, fraction, culture, out var v) && double.IsFinite(v) ? v : null; }
        if (type == TypeSymbol.DecimalType) { return decimal.TryParse(text, fraction, culture, out var v) ? v : null; }
        return null;
    }
}
