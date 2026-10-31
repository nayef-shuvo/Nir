using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record Block(ImmutableArray<Statement> Statements, TextSpan Span) : SyntaxNode(Span);

public abstract record Statement(TextSpan Span) : SyntaxNode(Span);

public sealed record ExpressionStatement(Expression Expression, TextSpan Span) : Statement(Span);
