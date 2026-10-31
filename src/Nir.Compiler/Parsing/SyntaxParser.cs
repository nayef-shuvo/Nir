using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Parsing;

/// <summary>The only entry point other code uses to turn source text into a syntax tree.</summary>
public static class SyntaxParser
{
    /// <summary>
    /// Parses <paramref name="source"/>. Always returns a tree, even for broken input (possibly with fewer members);
    /// problems are reported to <paramref name="diagnostics"/>. Never throws for bad input.
    /// </summary>
    public static CompilationUnit Parse(SourceText source, DiagnosticBag diagnostics)
    {
        var lexer = new NirLexer(new AntlrInputStream(source.Text));
        lexer.Attach(source, diagnostics);

        var parser = new NirParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ParserErrorListener(source, diagnostics));

        return new AstBuilder(source).Build(parser.compilationUnit());
    }
}
