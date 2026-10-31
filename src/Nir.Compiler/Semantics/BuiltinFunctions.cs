using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>
/// Functions the compiler provides without any import. A stand-in for the <c>std</c> prelude until <c>std</c> exists.
/// A name can have several overloads, chosen by exact argument types.
/// </summary>
internal static class BuiltinFunctions
{
    private static readonly Dictionary<string, ImmutableArray<BuiltinFunctionSymbol>> _functions = new()
    {
        ["print"] =
        [
            Print(TypeSymbol.IntType),
            Print(TypeSymbol.DoubleType),
            Print(TypeSymbol.BoolType),
            Print(TypeSymbol.StringType),
        ],
    };

    /// <summary>All overloads of <paramref name="name"/>, or an empty list if there is no such built-in.</summary>
    public static ImmutableArray<BuiltinFunctionSymbol> GetOverloads(string name) =>
        _functions.TryGetValue(name, out var overloads) ? overloads : [];

    // Console.WriteLine has an overload for each of these types, on every supported runtime.
    private static BuiltinFunctionSymbol Print(TypeSymbol parameterType) =>
        new("print", typeof(Console).GetMethod(nameof(Console.WriteLine), [parameterType.ClrType])!, TypeSymbol.UnitType, [parameterType]);
}
