using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public abstract record Expression(TextSpan Span) : SyntaxNode(Span);

public sealed record CallExpression(QualifiedName Callee, ImmutableArray<Expression> Arguments, TextSpan Span) : Expression(Span);

/// <summary><c>receiver.Method(arguments)</c>: a method called on a value.</summary>
public sealed record MemberCallExpression(Expression Receiver, Identifier Method, ImmutableArray<Expression> Arguments, TextSpan Span)
    : Expression(Span);

/// <summary>A use of a variable by name.</summary>
public sealed record NameExpression(Identifier Name, TextSpan Span) : Expression(Span);

public sealed record BinaryExpression(Expression Left, BinaryOperator Operator, Expression Right, TextSpan Span) : Expression(Span);

public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand, TextSpan Span) : Expression(Span);

/// <summary>An expression in parentheses. Kept so spans stay faithful; the binder looks straight through it.</summary>
public sealed record ParenthesizedExpression(Expression Inner, TextSpan Span) : Expression(Span);

/// <summary>An integer literal. <paramref name="Text"/> is the digits as written; the binder converts it and checks the range.</summary>
public sealed record IntLiteralExpression(string Text, TextSpan Span) : Expression(Span);

/// <summary>A floating-point literal such as <c>2.5</c>. <paramref name="Text"/> is as written; the binder converts it.</summary>
public sealed record DoubleLiteralExpression(string Text, TextSpan Span) : Expression(Span);

public sealed record BoolLiteralExpression(bool Value, TextSpan Span) : Expression(Span);

/// <summary>A string literal. <paramref name="Value"/> is the raw text between the quotes; escapes are not decoded yet.</summary>
public sealed record StringLiteralExpression(string Value, TextSpan Span) : Expression(Span);
