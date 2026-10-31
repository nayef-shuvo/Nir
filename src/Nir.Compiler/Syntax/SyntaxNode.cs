using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

/// <summary>Base of every syntax tree node. <paramref name="Span"/> covers the node's full source text.</summary>
public abstract record SyntaxNode(TextSpan Span);

/// <summary>A name in the source.</summary>
public sealed record Identifier(string Text, TextSpan Span) : SyntaxNode(Span);
