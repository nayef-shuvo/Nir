using System.Collections.Immutable;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>
/// A union declared with <c>type Shape = union { Circle, Square }</c>: a value that is exactly one of its members. It is a type
/// of its own (two unions with the same members are different types). It compiles to a tagged value type.
/// </summary>
public class UnionTypeSymbol : TypeSymbol
{
    internal UnionTypeSymbol(UnionDeclaration declaration)
        : base(declaration.Name.Text)
    {
        Declaration = declaration;
    }

    /// <summary>For the result types, which are unions the compiler makes itself (see <see cref="ResultTypeSymbol"/>).</summary>
    private protected UnionTypeSymbol(string name)
        : base(name)
    {
    }

    /// <summary>Where the union was declared; null for a result type.</summary>
    public UnionDeclaration? Declaration { get; }

    /// <summary>The name of the CLR type Emit defines for it.</summary>
    public virtual string ClrName => Name;

    /// <summary>The member types in declaration order; a member's position is its tag.</summary>
    public ImmutableArray<TypeSymbol> Members { get; internal set; } = [];

    /// <summary>The tag of <paramref name="member"/>, or -1 if the union has no such member.</summary>
    public int IndexOf(TypeSymbol member)
    {
        for (var i = 0; i < Members.Length; i++)
        {
            if (Members[i] == member)
            {
                return i;
            }
        }

        return -1;
    }
}
