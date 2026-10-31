using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Syntax;

public sealed record CompilationUnit(ImmutableArray<FunctionDeclaration> Functions, TextSpan Span) : SyntaxNode(Span);

public sealed record FunctionDeclaration(Identifier Name, Block Body, TextSpan Span) : SyntaxNode(Span);
