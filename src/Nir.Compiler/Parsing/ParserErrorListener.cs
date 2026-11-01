using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Parsing;

/// <summary>Turns every ANTLR syntax error into an <c>NIR1001</c> diagnostic at the offending token.</summary>
internal sealed partial class ParserErrorListener(SourceText source, DiagnosticBag diagnostics) : IAntlrErrorListener<IToken>
{
    // ANTLR names the tokens it expects with the grammar's own names; these are the words a user would use.
    private static readonly Dictionary<string, string> _friendlyNames = new(StringComparer.Ordinal)
    {
        ["STRING_START"] = "a string",
        ["STRING_TEXT"] = "string text",
        ["STRING_END"] = "a closing quote",
        ["INT_LITERAL"] = "a number",
        ["DOUBLE_LITERAL"] = "a number",
        ["CHAR_LITERAL"] = "a character",
        ["IDENTIFIER"] = "a name",
        ["SEMI"] = "the end of a statement",
    };

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
            Friendly(msg),
            source,
            new TextSpan(start, length));
    }

    /// <summary>Rewrites an ANTLR message in plain words: no token names, no repeats, a lowercase start.</summary>
    internal static string Friendly(string message)
    {
        message = message.Replace("'<EOF>'", "the end of the file", StringComparison.Ordinal);
        message = ExpectedSet().Replace(message, match =>
        {
            var items = match.Groups[1].Value.Split(", ")
                .Select(item => _friendlyNames.GetValueOrDefault(item, item))
                .Distinct()
                .ToList();
            return items.Count == 1 ? $"expecting {items[0]}" : $"expecting one of {string.Join(", ", items)}";
        });

        foreach (var (token, words) in _friendlyNames)
        {
            message = Regex.Replace(message, $@"\b{token}\b", words);
        }

        return message.Length == 0 ? message : char.ToLowerInvariant(message[0]) + message[1..];
    }

    [GeneratedRegex(@"expecting \{(.*)\}")]
    private static partial Regex ExpectedSet();
}
