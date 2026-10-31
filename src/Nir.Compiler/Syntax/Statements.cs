using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record Block(ImmutableArray<Statement> Statements, TextSpan Span) : SyntaxNode(Span);

public abstract record Statement(TextSpan Span) : SyntaxNode(Span);

public sealed record ExpressionStatement(Expression Expression, TextSpan Span) : Statement(Span);

/// <summary><c>let name = value</c>, <c>let mut name = value</c>, optionally with a type annotation.</summary>
public sealed record VariableDeclaration(Identifier Name, bool IsMutable, Identifier? DeclaredType, Expression Initializer, TextSpan Span)
    : Statement(Span);

/// <summary><c>return</c> or <c>return value</c>. <paramref name="Value"/> is null for a bare return.</summary>
public sealed record ReturnStatement(Expression? Value, TextSpan Span) : Statement(Span);

public sealed record AssignmentStatement(Identifier Target, Expression Value, TextSpan Span) : Statement(Span);
