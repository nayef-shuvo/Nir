using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public abstract record Expression(TextSpan Span) : SyntaxNode(Span);

public sealed record CallExpression(Identifier Callee, ImmutableArray<Expression> Arguments, TextSpan Span) : Expression(Span);

/// <summary>A string literal. <paramref name="Value"/> is the raw text between the quotes; escapes are not decoded yet.</summary>
public sealed record StringLiteralExpression(string Value, TextSpan Span) : Expression(Span);
