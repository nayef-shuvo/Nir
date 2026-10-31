using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record CompilationUnit(ImmutableArray<FunctionDeclaration> Functions, TextSpan Span) : SyntaxNode(Span);

/// <summary><paramref name="ReturnType"/> is null when no <c>-> type</c> is written, meaning the function returns <c>()</c>.</summary>
public sealed record FunctionDeclaration(
    Identifier Name,
    ImmutableArray<Parameter> Parameters,
    Identifier? ReturnType,
    Block Body,
    TextSpan Span) : SyntaxNode(Span);

public sealed record Parameter(Identifier Name, Identifier Type, TextSpan Span) : SyntaxNode(Span);
