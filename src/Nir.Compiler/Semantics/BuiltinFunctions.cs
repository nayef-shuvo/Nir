using System.Collections.Immutable;
using Nir.Compiler.Clr;

namespace Nir.Compiler.Semantics;

/// <summary>
/// Functions the compiler provides without any import. A stand-in for the <c>std</c> prelude until <c>std</c> exists.
/// A name can have several overloads, chosen by exact argument types.
/// </summary>
internal static class BuiltinFunctions
{
    private static readonly Dictionary<string, ImmutableArray<ClrMethodSymbol>> _functions = new()
    {
        [ReservedNames.Print] =
        [
            Print(TypeSymbol.ByteType),
            Print(TypeSymbol.SByteType),
            Print(TypeSymbol.ShortType),
            Print(TypeSymbol.UShortType),
            Print(TypeSymbol.IntType),
            Print(TypeSymbol.UIntType),
            Print(TypeSymbol.LongType),
            Print(TypeSymbol.ULongType),
            Print(TypeSymbol.FloatType),
            Print(TypeSymbol.DoubleType),
            Print(TypeSymbol.DecimalType),
            Print(TypeSymbol.CharType),
            Print(TypeSymbol.BoolType),
            Print(TypeSymbol.StringType),
        ],
    };

    /// <summary>All overloads of <paramref name="name"/>, or an empty list if there is no such built-in.</summary>
    public static ImmutableArray<ClrMethodSymbol> GetOverloads(string name) =>
        _functions.TryGetValue(name, out var overloads) ? overloads : [];

    // Console.WriteLine has an overload for each of these types except byte, sbyte, short and ushort. Those sit on the stack as
    // 32-bit integers anyway, so they use the int overload and print the same text.
    private static ClrMethodSymbol Print(TypeSymbol parameterType) =>
        new(
            ReservedNames.Print,
            ClrTypes.ConsoleWriteLine(NumberTypes.IsSmallInteger(parameterType) ? ClrTypes.Int32 : parameterType.ClrType),
            TypeSymbol.UnitType,
            [parameterType]);
}
