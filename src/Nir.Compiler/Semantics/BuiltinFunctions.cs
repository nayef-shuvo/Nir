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
            Print(TypeSymbol.IntType),
            Print(TypeSymbol.DoubleType),
            Print(TypeSymbol.BoolType),
            Print(TypeSymbol.StringType),
        ],
    };

    /// <summary>All overloads of <paramref name="name"/>, or an empty list if there is no such built-in.</summary>
    public static ImmutableArray<ClrMethodSymbol> GetOverloads(string name) =>
        _functions.TryGetValue(name, out var overloads) ? overloads : [];

    // Console.WriteLine has an overload for each of these types, on every supported runtime.
    private static ClrMethodSymbol Print(TypeSymbol parameterType) =>
        new(ReservedNames.Print, ClrTypes.ConsoleWriteLine(parameterType.ClrType), TypeSymbol.UnitType, [parameterType]);
}
