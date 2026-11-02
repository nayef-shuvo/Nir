using System.Collections.Immutable;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>A struct declared with <c>type Name = struct { ... }</c>: a value type with named fields.</summary>
public sealed class StructTypeSymbol : TypeSymbol
{
    internal StructTypeSymbol(StructDeclaration declaration, string? clrName = null)
        : base(declaration.Name.Text)
    {
        Declaration = declaration;
        ClrName = clrName ?? declaration.Name.Text;
    }

    /// <summary>The name of the type in the compiled program: the Nir name, except for the built-in <c>TypeInfo</c>.</summary>
    public string ClrName { get; }

    public StructDeclaration Declaration { get; }

    /// <summary>True for <c>type E = error { ... }</c>: a class whose values cannot change, instead of a value type.</summary>
    public bool IsError => Declaration.IsError;

    /// <summary>The fields in declaration order. Filled in once every struct name is known, because fields may use other structs.</summary>
    public ImmutableArray<FieldSymbol> Fields { get; internal set; } = [];

    public FieldSymbol? FindField(string name) => Fields.FirstOrDefault(field => field.Name == name);

    /// <summary>Methods and associated functions, in the order they were declared.</summary>
    public List<UserFunctionSymbol> Methods { get; } = [];

    public UserFunctionSymbol? FindMethod(string name) => Methods.FirstOrDefault(method => method.Name == name);

    private BorrowTypeSymbol? _sharedBorrow;
    private BorrowTypeSymbol? _mutableBorrow;

    /// <summary>The type of <c>&amp;Self</c> (or <c>&amp;mut Self</c>): created once so borrow types compare by reference.</summary>
    public BorrowTypeSymbol BorrowOf(bool isMutable) =>
        isMutable ? _mutableBorrow ??= new BorrowTypeSymbol(this, true) : _sharedBorrow ??= new BorrowTypeSymbol(this, false);
}

/// <summary>A field of a struct, or an element of a tuple.</summary>
public sealed class FieldSymbol(string name, TypeSymbol type, TypeSymbol owner, FieldDeclaration? declaration = null)
{
    public string Name { get; } = name;

    public TypeSymbol Type { get; } = type;

    public TypeSymbol Owner { get; } = owner;

    /// <summary>Where the field was declared, for messages about it.</summary>
    public FieldDeclaration? Declaration { get; } = declaration;
}
