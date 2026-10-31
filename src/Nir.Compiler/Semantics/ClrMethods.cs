using System.Collections.Immutable;
using System.Reflection;

namespace Nir.Compiler.Semantics;

/// <summary>
/// A .NET method Nir could call. <paramref name="ReturnType"/> is null when the method returns a type Nir cannot use yet;
/// such a method is still a candidate, so the binder can say so instead of pretending it does not exist.
/// </summary>
internal sealed record ClrCandidate(MethodInfo Method, ImmutableArray<TypeSymbol> ParameterTypes, TypeSymbol? ReturnType)
{
    public static ClrCandidate From(ClrMethodSymbol symbol) => new(symbol.Method, symbol.ParameterTypes, symbol.ReturnType);
}

/// <summary>Finds the public methods of a .NET type that Nir can call: every parameter is an <c>int</c>, <c>double</c>, <c>bool</c> or <c>string</c>.</summary>
internal static class ClrMethods
{
    /// <summary>True if the type has any public method with this name, whether or not Nir can call it.</summary>
    public static bool HasMethodNamed(Type type, string name, bool isStatic) =>
        type.GetMethods(Flags(isStatic)).Any(m => m.Name == name);

    public static ImmutableArray<ClrCandidate> FindCandidates(Type type, string name, bool isStatic)
    {
        var candidates = ImmutableArray.CreateBuilder<ClrCandidate>();
        foreach (var method in type.GetMethods(Flags(isStatic)).Where(m => m.Name == name && !m.IsGenericMethodDefinition))
        {
            var parameters = method.GetParameters().Select(p => TypeSymbol.FromClr(p.ParameterType)).ToList();
            if (parameters.Any(p => p is null || p == TypeSymbol.UnitType))
            {
                continue; // a parameter Nir cannot pass: not an overload we can choose
            }

            candidates.Add(new ClrCandidate(method, [.. parameters.Select(p => p!)], TypeSymbol.FromClr(method.ReturnType)));
        }

        return candidates.ToImmutable();
    }

    private static BindingFlags Flags(bool isStatic) =>
        BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
}
