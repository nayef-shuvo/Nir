using System.Collections.Immutable;
using System.Globalization;

namespace Nir.Compiler.Semantics;

/// <summary>
/// A tuple type: <c>(int, double)</c> or <c>(x: int, y: int)</c>, two or more elements. It compiles to
/// <c>System.ValueTuple</c> (nested through <c>Rest</c> past seven elements). Two tuple types are the same type when their element types are the same; the names are only
/// a way to reach an element and are not part of the type.
/// </summary>
public sealed class TupleTypeSymbol : TypeSymbol
{
    /// <summary>Names the elements, or all <c>null</c> for a tuple whose elements are only reached by index.</summary>
    internal TupleTypeSymbol(ImmutableArray<TypeSymbol> elementTypes, ImmutableArray<string?> names)
        : base(Describe(elementTypes, names))
    {
        IsNamed = names.Any(name => name is not null);
        Elements = [.. elementTypes.Select((type, i) => new FieldSymbol(names[i] ?? i.ToString(CultureInfo.InvariantCulture), type, this))];
    }

    /// <summary>The elements in order. A tuple element is a <see cref="FieldSymbol"/> so reading and writing it works like a struct field.</summary>
    public ImmutableArray<FieldSymbol> Elements { get; }

    public bool IsNamed { get; }

    /// <summary>The element reached by <c>.0</c>, <c>.1</c>... or, for a named tuple, by its name.</summary>
    public FieldSymbol? FindElement(string text)
    {
        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < Elements.Length ? Elements[index] : null;
        }

        return IsNamed ? Elements.FirstOrDefault(element => element.Name == text) : null;
    }

    private static string Describe(ImmutableArray<TypeSymbol> types, ImmutableArray<string?> names) =>
        "(" + string.Join(", ", types.Select((type, i) => names[i] is { } name ? $"{name}: {type}" : type.Name)) + ")";

    public override bool Equals(object? obj) =>
        obj is TupleTypeSymbol other && Elements.Length == other.Elements.Length && Elements.Zip(other.Elements).All(pair => pair.First.Type == pair.Second.Type);

    public override int GetHashCode() => Elements.Aggregate(Elements.Length, (hash, element) => HashCode.Combine(hash, element.Type.GetHashCode()));
}
