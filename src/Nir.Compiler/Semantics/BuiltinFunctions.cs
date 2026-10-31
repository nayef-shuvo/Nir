using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>
/// Functions the compiler provides without any import. A stand-in for the <c>std</c> prelude until <c>std</c> exists.
/// </summary>
internal static class BuiltinFunctions
{
    private static readonly Dictionary<string, BuiltinFunctionSymbol> _functions = new()
    {
        ["print"] = new BuiltinFunctionSymbol(
            "print",
            typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!, // exists on every supported runtime
            TypeSymbol.UnitType,
            [TypeSymbol.StringType]),
    };

    public static bool TryGet(string name, out BuiltinFunctionSymbol symbol) =>
        _functions.TryGetValue(name, out symbol!);
}
