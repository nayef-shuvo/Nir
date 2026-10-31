using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;

namespace Nir.Tests.Parsing;

public class SyntaxMessageTests
{
    private static IReadOnlyList<string> Messages(string text)
    {
        var diagnostics = new DiagnosticBag();
        SyntaxParser.Parse(new SourceText("test.nir", text), diagnostics);
        return [.. diagnostics.Select(d => d.Message)];
    }

    [Theory]
    [InlineData("fn main() { print(1 + ) }")]
    [InlineData("fn main( {\n}")]
    [InlineData("fn main() { let x = }")]
    [InlineData("fn main() { x. }")]
    [InlineData("using")]
    [InlineData("fn main()")]
    public void Messages_never_show_the_grammar_token_names(string source)
    {
        var messages = Messages(source);

        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.DoesNotMatch(@"\b[A-Z]+(_[A-Z]+)+\b|\bIDENTIFIER\b|\bSEMI\b|<EOF>", m));
    }

    [Fact]
    public void Expected_set_uses_plain_words_without_repeats()
    {
        var message = Assert.Single(Messages("fn main() { print(1 + ) }"));

        Assert.Contains("a number", message, StringComparison.Ordinal);
        Assert.Contains("a name", message, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(message, "a number"));
    }

    [Fact]
    public void Friendly_keeps_a_lowercase_start_and_names_the_end_of_file()
    {
        Assert.Equal("mismatched input the end of the file expecting a name", ParserErrorListener.Friendly("Mismatched input '<EOF>' expecting IDENTIFIER"));
    }
}
