using Nir.Compiler.Parsing;

namespace Nir.Tests.Parsing;

public class StringEscapesTests
{
    private static (string Decoded, List<(int Offset, int Length, string Text)> Unknown) Decode(string raw, bool interpolated = false)
    {
        var unknown = new List<(int, int, string)>();
        var decoded = StringEscapes.Decode(raw, interpolated, forCharacter: false, (offset, length, text) => unknown.Add((offset, length, text)));
        return (decoded, unknown);
    }

    [Theory]
    [InlineData("a\\nb", "a\nb")]
    [InlineData("a\\tb", "a\tb")]
    [InlineData("a\\rb", "a\rb")]
    [InlineData("a\\\\b", "a\\b")]
    [InlineData("say \\\"hi\\\"", "say \"hi\"")]
    [InlineData("\\{x}", "{x}")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    [InlineData("\\n\\n", "\n\n")]
    public void Known_escapes_are_decoded(string raw, string expected)
    {
        var (decoded, unknown) = Decode(raw);

        Assert.Equal(expected, decoded);
        Assert.Empty(unknown);
    }

    [Fact]
    public void An_escaped_backslash_followed_by_n_is_not_a_newline()
    {
        Assert.Equal("\\n", Decode("\\\\n").Decoded);
    }

    [Fact]
    public void Unknown_escapes_are_reported_with_their_position_and_dropped()
    {
        var (decoded, unknown) = Decode("ab\\qcd\\z");

        Assert.Equal("abcd", decoded);
        Assert.Equal([(2, 2, "\\q"), (6, 2, "\\z")], unknown);
    }

    [Fact]
    public void Doubled_braces_mean_one_brace_only_in_interpolated_text()
    {
        Assert.Equal("{x} and }", Decode("{{x}} and }}", interpolated: true).Decoded);
        Assert.Equal("{{x}}", Decode("{{x}}", interpolated: false).Decoded);
    }

    [Fact]
    public void Escapes_and_doubled_braces_mix_in_interpolated_text()
    {
        var (decoded, unknown) = Decode("a\\n{{b}}\\{", interpolated: true);

        Assert.Equal("a\n{b}{", decoded);
        Assert.Empty(unknown);
    }
}
