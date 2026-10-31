using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record CompilationUnit(
    ImmutableArray<UsingDirective> Usings,
    ImmutableArray<StructDeclaration> Structs,
    ImmutableArray<ImplDeclaration> Impls,
    ImmutableArray<FunctionDeclaration> Functions,
    TextSpan Span) : SyntaxNode(Span);

/// <summary><c>type Name = struct { field: type, ... }</c>.</summary>
public sealed record StructDeclaration(Identifier Name, ImmutableArray<FieldDeclaration> Fields, TextSpan Span) : SyntaxNode(Span);

public sealed record FieldDeclaration(Identifier Name, Identifier Type, TextSpan Span) : SyntaxNode(Span);

/// <summary><c>Name impl { fn ... }</c>: the methods and associated functions of a struct.</summary>
public sealed record ImplDeclaration(Identifier TypeName, ImmutableArray<FunctionDeclaration> Methods, TextSpan Span) : SyntaxNode(Span);

/// <summary>The first parameter of a method, <c>&amp;self</c> or <c>&amp;mut self</c>.</summary>
public sealed record Receiver(bool IsMutable, TextSpan Span) : SyntaxNode(Span);

/// <summary>
/// <c>using a::b::C</c> (<paramref name="Members"/> empty) or <c>using a::b::{C, D}</c>
/// (<paramref name="Path"/> is <c>a::b</c>, <paramref name="Members"/> is <c>C, D</c>).
/// </summary>
public sealed record UsingDirective(ImmutableArray<Identifier> Path, ImmutableArray<Identifier> Members, TextSpan Span) : SyntaxNode(Span);

/// <summary><paramref name="ReturnType"/> is null when no <c>-> type</c> is written, meaning the function returns <c>()</c>.</summary>
public sealed record FunctionDeclaration(
    Identifier Name,
    Receiver? Receiver,
    ImmutableArray<Parameter> Parameters,
    Identifier? ReturnType,
    Block Body,
    TextSpan Span) : SyntaxNode(Span);

public sealed record Parameter(Identifier Name, TypeReference Type, TextSpan Span) : SyntaxNode(Span);

public enum BorrowKind
{
    None,
    Shared,
    Mutable,
}

/// <summary>A type in a parameter: <c>Point</c>, <c>&amp;Point</c> or <c>&amp;mut Point</c>.</summary>
public sealed record TypeReference(Identifier Name, BorrowKind Borrow, TextSpan Span) : SyntaxNode(Span)
{
    /// <summary>The type as written, for messages.</summary>
    public string Text => Borrow switch { BorrowKind.Shared => "&" + Name.Text, BorrowKind.Mutable => "&mut " + Name.Text, _ => Name.Text };
}
