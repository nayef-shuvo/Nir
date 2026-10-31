using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record CompilationUnit(ImmutableArray<UsingDirective> Usings, ImmutableArray<FunctionDeclaration> Functions, TextSpan Span)
    : SyntaxNode(Span);

/// <summary>
/// <c>using a::b::C</c> (<paramref name="Members"/> empty) or <c>using a::b::{C, D}</c>
/// (<paramref name="Path"/> is <c>a::b</c>, <paramref name="Members"/> is <c>C, D</c>).
/// </summary>
public sealed record UsingDirective(ImmutableArray<Identifier> Path, ImmutableArray<Identifier> Members, TextSpan Span) : SyntaxNode(Span);

/// <summary><paramref name="ReturnType"/> is null when no <c>-> type</c> is written, meaning the function returns <c>()</c>.</summary>
public sealed record FunctionDeclaration(
    Identifier Name,
    ImmutableArray<Parameter> Parameters,
    Identifier? ReturnType,
    Block Body,
    TextSpan Span) : SyntaxNode(Span);

public sealed record Parameter(Identifier Name, Identifier Type, TextSpan Span) : SyntaxNode(Span);
