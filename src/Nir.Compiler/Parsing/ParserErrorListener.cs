using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Parsing;

/// <summary>Turns every ANTLR syntax error into an <c>NIR1001</c> diagnostic at the offending token.</summary>
internal sealed class ParserErrorListener(SourceText source, DiagnosticBag diagnostics) : IAntlrErrorListener<IToken>
{
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        var start = Math.Clamp(offendingSymbol.StartIndex, 0, source.Text.Length);
        var length = Math.Max(0, offendingSymbol.StopIndex - offendingSymbol.StartIndex + 1);
        length = Math.Min(length, source.Text.Length - start);

        diagnostics.Report(
            DiagnosticCodes.SyntaxError,
            Severity.Error,
            LowercaseFirst(msg),
            source,
            new TextSpan(start, length));
    }

    private static string LowercaseFirst(string message) =>
        message.Length == 0 ? message : char.ToLowerInvariant(message[0]) + message[1..];
}
