using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public abstract record Expression(TextSpan Span) : SyntaxNode(Span);

public sealed record CallExpression(QualifiedName Callee, ImmutableArray<Expression> Arguments, TextSpan Span) : Expression(Span);

/// <summary><c>receiver.Method(arguments)</c>: a method called on a value.</summary>
public sealed record MemberCallExpression(Expression Receiver, Identifier Method, ImmutableArray<Expression> Arguments, TextSpan Span)
    : Expression(Span);

/// <summary><c>Name { field = value, ... }</c>.</summary>
public sealed record StructLiteralExpression(Identifier TypeName, ImmutableArray<FieldInitializer> Fields, TextSpan Span) : Expression(Span);

public sealed record FieldInitializer(Identifier Name, Expression Value, TextSpan Span) : SyntaxNode(Span);

/// <summary><c>(1, 2.5)</c>: two or more values.</summary>
public sealed record TupleLiteralExpression(ImmutableArray<Expression> Elements, TextSpan Span) : Expression(Span);

/// <summary><c>receiver.field</c>. For a tuple the "field" is an index such as <c>0</c>, or the name of an element.</summary>
public sealed record FieldAccessExpression(Expression Receiver, Identifier Field, TextSpan Span) : Expression(Span);

/// <summary>The receiver inside a method.</summary>
public sealed record SelfExpression(TextSpan Span) : Expression(Span);

/// <summary><c>&amp;operand</c> or <c>&amp;mut operand</c>: a reference to a variable.</summary>
public sealed record ReferenceExpression(Expression Operand, bool IsMutable, TextSpan Span) : Expression(Span);

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

/// <summary><c>$"text {expression} text"</c>. Text parts are decoded; every hole holds an expression.</summary>
public sealed record InterpolatedStringExpression(ImmutableArray<InterpolatedStringPart> Parts, TextSpan Span) : Expression(Span);

public abstract record InterpolatedStringPart(TextSpan Span) : SyntaxNode(Span);

public sealed record InterpolatedText(string Text, TextSpan Span) : InterpolatedStringPart(Span);

public sealed record InterpolatedHole(Expression Expression, TextSpan Span) : InterpolatedStringPart(Span);

/// <summary><c>operand as type</c>: a conversion between <c>int</c> and <c>double</c>. The type is a plain identifier.</summary>
public sealed record CastExpression(Expression Operand, Identifier TargetType, TextSpan Span) : Expression(Span);

/// <summary>A string literal. <paramref name="Value"/> is the text between the quotes with its escapes decoded.</summary>
public sealed record StringLiteralExpression(string Value, TextSpan Span) : Expression(Span);
