using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;

namespace Nir.Tests.Support;

/// <summary>Lexes a string with the real lexer and prints the tokens the parser would see.</summary>
internal static class TokenFormatter
{
    public static (IReadOnlyList<string> Tokens, DiagnosticBag Diagnostics) Lex(string text)
    {
        var bag = new DiagnosticBag();
        var lexer = new NirLexer(new AntlrInputStream(text));
        lexer.Attach(new SourceText("test.nir", text), bag);

        var tokens = new List<string>();
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Type == TokenConstants.EOF)
            {
                tokens.Add("EOF");
                break;
            }

            tokens.Add($"{NirLexer.DefaultVocabulary.GetSymbolicName(token.Type)} '{token.Text.Replace("\n", "\\n", StringComparison.Ordinal)}'");
        }

        return (tokens, bag);
    }

    /// <summary>The token names only, separated by spaces: <c>IDENTIFIER SEMI EOF</c>.</summary>
    public static string Names(string text) =>
        string.Join(' ', Lex(text).Tokens.Select(t => t.Split(' ')[0]));
}
