using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

/// <summary>Slice 9c: literal suffixes and character literals in the syntax tree.</summary>
public class NumberParserTests
{
    private static (string Tree, DiagnosticBag Diagnostics) Parse(string statement)
    {
        var diagnostics = new DiagnosticBag();
        var unit = SyntaxParser.Parse(new SourceText("test.nir", "fn main() {\n    " + statement + "\n}\n"), diagnostics);
        return (AstFormatter.Format(unit), diagnostics);
    }

    [Theory]
    [InlineData("let a = 200_byte", "IntLiteral 200_byte")]
    [InlineData("let a = 2.5_decimal", "DoubleLiteral 2.5_decimal")]
    [InlineData("let a: ulong = 1", "IntLiteral 1")]
    public void A_literal_keeps_its_suffix_as_written(string statement, string literal)
    {
        var (tree, diagnostics) = Parse(statement);

        Assert.Empty(diagnostics);
        Assert.Contains(literal, tree);
    }

    [Theory]
    [InlineData("let c = 'a'", "a")]
    [InlineData("let c = '\\n'", "\n")]
    [InlineData("let c = '\\''", "'")]
    [InlineData("let c = '\\0'", "\0")]
    [InlineData("let c = '\\\\'", "\\")]
    public void A_character_literal_is_decoded(string statement, string decoded)
    {
        var (tree, diagnostics) = Parse(statement);

        Assert.Empty(diagnostics);
        Assert.Contains($"CharLiteral '{decoded}'", tree);
    }

    [Fact]
    public void An_unknown_escape_in_a_character_is_reported_at_the_escape()
    {
        var (_, diagnostics) = Parse("let c = '\\q'");

        var only = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownEscape, only.Code);
        Assert.Equal("fn main() {\n".Length + 13, only.Span.Start); // the `\q` after `    let c = '`
        Assert.Equal(2, only.Span.Length);
    }

    [Fact]
    public void A_character_can_be_a_range_bound_and_an_operand()
    {
        var (tree, diagnostics) = Parse("for c in 'a'..='z' { print(c) }");

        Assert.Empty(diagnostics);
        Assert.Contains("CharLiteral 'a'", tree);
        Assert.Contains("CharLiteral 'z'", tree);
    }
}
