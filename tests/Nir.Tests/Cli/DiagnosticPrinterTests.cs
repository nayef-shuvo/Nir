using Nir.Cli;
using Nir.Compiler.Diagnostics;

namespace Nir.Tests.Cli;

public class DiagnosticPrinterTests
{
    private static Diagnostic Error(string text, int start, int length, string code = "NIR9001", string message = "something is wrong", Severity severity = Severity.Error) =>
        new(code, severity, message, new SourceText("a.nir", text), new TextSpan(start, length));

    private static string Render(bool color, params Diagnostic[] diagnostics)
    {
        var writer = new StringWriter { NewLine = "\n" };
        DiagnosticPrinter.Print(writer, diagnostics, color);
        return writer.ToString();
    }

    [Fact]
    public void Shows_header_pointer_source_line_and_carets_under_the_span()
    {
        var text = "let x = 1\nx = 2\n";

        var output = Render(false, Error(text, start: 10, length: 1, "NIR3002", "cannot assign to 'x' because it is not mutable"));

        Assert.Equal(
            """
            error[NIR3002]: cannot assign to 'x' because it is not mutable
             --> a.nir:2:1
              |
            2 | x = 2
              | ^ cannot assign to 'x' because it is not mutable

            error: aborting due to 1 previous error

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void Carets_cover_the_whole_span_and_start_at_the_right_column()
    {
        var output = Render(false, Error("    prnt(\"x\")\n", start: 4, length: 4, message: "'prnt' is not defined"));

        Assert.Contains("1 |     prnt(\"x\")\n  |     ^^^^ 'prnt' is not defined\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Gutter_widens_with_the_line_number()
    {
        var lines = string.Concat(Enumerable.Repeat("x\n", 11)) + "bad\n";

        var output = Render(false, Error(lines, start: 22, length: 3));

        Assert.Contains(" --> a.nir:12:1\n   |\n12 | bad\n   | ^^^ something is wrong\n", output.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Tabs_are_shown_as_four_spaces_and_the_carets_follow()
    {
        var output = Render(false, Error("\tabc\n", start: 1, length: 3));

        Assert.Contains("1 |     abc\n  |     ^^^ something is wrong\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_with_no_length_still_gets_one_caret()
    {
        var output = Render(false, Error("fn other() {\n}\n", start: 0, length: 0, "NIR2002", "program has no 'main' function"));

        Assert.Contains("1 | fn other() {\n  | ^ program has no 'main' function\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_position_at_the_end_of_the_file_shows_an_empty_line()
    {
        var text = "fn main()\n";

        var output = Render(false, Error(text, start: text.Length, length: 0));

        Assert.Contains("2 | \n  | ^ something is wrong\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_span_that_runs_past_the_end_of_its_line_stops_at_the_line()
    {
        var output = Render(false, Error("ab\ncd\n", start: 1, length: 4));

        Assert.Contains("1 | ab\n  |  ^ something is wrong\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_line_endings_do_not_leak_into_the_source_line()
    {
        var output = Render(false, Error("let x = 1\r\nx = 2\r\n", start: 11, length: 1));

        Assert.DoesNotContain('\r', output);
        Assert.Contains("2 | x = 2\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_errors_are_separated_and_counted_once()
    {
        var text = "a\nb\n";

        var output = Render(false, Error(text, 0, 1, "NIR9001", "first"), Error(text, 2, 1, "NIR9002", "second"));

        Assert.Contains("error[NIR9001]: first", output, StringComparison.Ordinal);
        Assert.Contains("error[NIR9002]: second", output, StringComparison.Ordinal);
        Assert.EndsWith("\nerror: aborting due to 2 previous errors\n", output.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(1, output.Split("aborting due to", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Warnings_are_labelled_as_warnings_and_are_not_counted()
    {
        var output = Render(false, Error("a\n", 0, 1, "NIR9003", "careful", Severity.Warning));

        Assert.StartsWith("warning[NIR9003]: careful", output, StringComparison.Ordinal);
        Assert.DoesNotContain("aborting", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Colour_is_added_only_when_asked_for()
    {
        var diagnostic = Error("a\n", 0, 1);

        Assert.DoesNotContain('\u001b', Render(false, diagnostic));
        var coloured = Render(true, diagnostic);
        Assert.Contains("\u001b[1;31merror[NIR9001]\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;31m^\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;34m|\u001b[0m", coloured, StringComparison.Ordinal);
    }

    [Fact]
    public void Colour_does_not_change_the_text()
    {
        var diagnostic = Error("let x = 1\nx = 2\n", 10, 1);

        var withoutEscapes = System.Text.RegularExpressions.Regex.Replace(Render(true, diagnostic), "\u001b\\[[0-9;]*m", string.Empty);

        Assert.Equal(Render(false, diagnostic), withoutEscapes);
    }
}
