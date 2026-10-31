using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

/// <summary>Base of every syntax tree node. <paramref name="Span"/> covers the node's full source text.</summary>
public abstract record SyntaxNode(TextSpan Span);

/// <summary>A name that may have several parts, such as <c>IO::File::ReadAllText</c>; <c>Text</c> joins them with <c>::</c>.</summary>
public sealed record QualifiedName(ImmutableArray<Identifier> Parts, TextSpan Span) : SyntaxNode(Span)
{
    public string Text => string.Join("::", Parts.Select(part => part.Text));
}

/// <summary>A name in the source.</summary>
public sealed record Identifier(string Text, TextSpan Span) : SyntaxNode(Span);
