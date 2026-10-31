using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>What the emitter has defined so far, shared by all function bodies: methods, struct types and their fields.</summary>
internal sealed class EmitContext
{
    public Dictionary<UserFunctionSymbol, MethodBuilder> Methods { get; } = [];

    public Dictionary<StructTypeSymbol, TypeBuilder> Structs { get; } = [];

    public Dictionary<FieldSymbol, FieldBuilder> Fields { get; } = [];

    /// <summary>The CLR type for a Nir type. A struct's is the type the emitter defined for it, not something the symbol knows.</summary>
    public Type ClrOf(TypeSymbol type) => type switch
    {
        StructTypeSymbol structType => Structs[structType],
        BorrowTypeSymbol borrow => Structs[borrow.Element].MakeByRefType(), // a borrow is a CLR `ref`
        _ => type.ClrType,
    };
}
