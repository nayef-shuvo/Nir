namespace Nir.Compiler.Semantics;

/// <summary>
/// A borrow of a struct: <c>&amp;Point</c> or <c>&amp;mut Point</c>. It is the type of <c>self</c> in a method and of a borrow
/// parameter. It compiles to a CLR managed pointer (<c>ref</c>), so nothing is copied or allocated, and it cannot be stored.
/// Use <see cref="StructTypeSymbol.BorrowOf"/> so each borrow type exists once.
/// </summary>
public sealed class BorrowTypeSymbol : TypeSymbol
{
    internal BorrowTypeSymbol(StructTypeSymbol element, bool isMutable)
        : base((isMutable ? "&mut " : "&") + element.Name)
    {
        Element = element;
        IsMutable = isMutable;
    }

    public StructTypeSymbol Element { get; }

    public bool IsMutable { get; }
}
