using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Parsing;

/// <summary>The only entry point other code uses to turn source text into a syntax tree.</summary>
public static class SyntaxParser
{
    /// <summary>
    /// Parses <paramref name="source"/>. Always returns a tree, even for broken input (possibly with fewer members).
    /// Never throws for bad input. Only the first problem in the file is reported to <paramref name="diagnostics"/>:
    /// the parser's recovery guesses what the user meant, and every guess after the first mistake is usually wrong, so
    /// the rest would only be noise. Fix it, run again, and the next one shows.
    /// </summary>
    public static CompilationUnit Parse(SourceText source, DiagnosticBag diagnostics)
    {
        var found = new DiagnosticBag();

        var lexer = new NirLexer(new AntlrInputStream(source.Text));
        lexer.Attach(source, found);

        var parser = new NirParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ParserErrorListener(source, found));

        var unit = new AstBuilder(source, found).Build(parser.compilationUnit());

        // The lexer runs a little ahead of the parser, so the order they report in is not quite the order in the file.
        if (found.OrderBy(d => d.Span.Start).FirstOrDefault() is { } first)
        {
            diagnostics.Report(first.Code, first.Severity, first.Message, first.Source, first.Span);
        }

        return unit;
    }
}
