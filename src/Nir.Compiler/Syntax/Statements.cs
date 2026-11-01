using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record Block(ImmutableArray<Statement> Statements, TextSpan Span) : SyntaxNode(Span);

public abstract record Statement(TextSpan Span) : SyntaxNode(Span);

public sealed record ExpressionStatement(Expression Expression, TextSpan Span) : Statement(Span);

/// <summary><c>let name = value</c>, <c>let mut name = value</c>, optionally with a type annotation.</summary>
public sealed record VariableDeclaration(Identifier Name, bool IsMutable, TypeSyntax? DeclaredType, Expression Initializer, TextSpan Span)
    : Statement(Span);

/// <summary><c>let (a, mut b) = pair</c>: each name takes one element of a tuple.</summary>
public sealed record DestructuringDeclaration(ImmutableArray<DestructuredName> Names, Expression Initializer, TextSpan Span) : Statement(Span);

public sealed record DestructuredName(Identifier Name, bool IsMutable, TextSpan Span) : SyntaxNode(Span);

/// <summary><c>return</c> or <c>return value</c>. <paramref name="Value"/> is null for a bare return.</summary>
public sealed record ReturnStatement(Expression? Value, TextSpan Span) : Statement(Span);

/// <summary><c>else if</c> is <paramref name="ElseIf"/>; a plain <c>else</c> is <paramref name="ElseBlock"/>; at most one is set.</summary>
public sealed record IfStatement(Expression Condition, Block Then, Block? ElseBlock, IfStatement? ElseIf, TextSpan Span) : Statement(Span);

public sealed record WhileStatement(Expression Condition, Block Body, TextSpan Span) : Statement(Span);

/// <summary><c>for variable in start..end { }</c>, or <c>start..=end</c> when <paramref name="IsInclusive"/>.</summary>
public sealed record ForStatement(Identifier Variable, Expression Start, Expression End, bool IsInclusive, Block Body, TextSpan Span) : Statement(Span);

public sealed record BreakStatement(TextSpan Span) : Statement(Span);

public sealed record ContinueStatement(TextSpan Span) : Statement(Span);

/// <summary><paramref name="Target"/> is a name, <c>self</c>, or a chain of field accesses on one of them.</summary>
public sealed record AssignmentStatement(Expression Target, Expression Value, TextSpan Span) : Statement(Span);
