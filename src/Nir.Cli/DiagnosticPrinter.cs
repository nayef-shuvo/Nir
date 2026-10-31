using System.Text;
using Nir.Compiler.Diagnostics;

namespace Nir.Cli;

/// <summary>
/// Prints diagnostics the way rustc does: a header with the code and message, a pointer to the file position, then the
/// source line with carets under the exact span and the message beside them.
/// </summary>
/// <remarks>
/// <code>
/// error[NIR3002]: cannot assign to 'x' because it is not mutable
///  --> samples/errors/assign_immutable.nir:4:5
///   |
/// 4 |     x = 2
///   |     ^ cannot assign to 'x' because it is not mutable
/// </code>
/// </remarks>
internal static class DiagnosticPrinter
{
    private const int TabWidth = 4;

    public static void Print(TextWriter writer, IReadOnlyList<Diagnostic> diagnostics, bool useColor)
    {
        var colors = new Colors(useColor);

        foreach (var diagnostic in diagnostics)
        {
            PrintOne(writer, diagnostic, colors);
            writer.WriteLine();
        }

        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        if (errors > 0)
        {
            var noun = errors == 1 ? "error" : "errors";
            writer.WriteLine($"{colors.Error("error")}{colors.Bold($": aborting due to {errors} previous {noun}")}");
        }
    }

    private static void PrintOne(TextWriter writer, Diagnostic diagnostic, Colors colors)
    {
        var source = diagnostic.Source;
        var (line, column) = source.GetLineAndColumn(diagnostic.Span.Start);
        var text = source.GetLineText(line);
        var number = line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var gutter = new string(' ', number.Length);

        var isError = diagnostic.Severity == Severity.Error;
        var severity = isError ? "error" : "warning";
        Func<string, string> mark = isError ? colors.Error : colors.Warning;

        // The span may reach past the end of its first line (or have no length at all); underline at least one character
        // and never past the line.
        var startIndex = Math.Min(column - 1, text.Length);
        var length = Math.Clamp(diagnostic.Span.Length, 1, Math.Max(1, text.Length - startIndex));
        var indent = DisplayWidth(text, startIndex);
        var underline = Math.Max(1, DisplayWidth(text.Substring(startIndex, Math.Min(length, text.Length - startIndex)), int.MaxValue));

        writer.WriteLine($"{mark($"{severity}[{diagnostic.Code}]")}{colors.Bold($": {diagnostic.Message}")}");
        writer.WriteLine($"{gutter}{colors.Accent("-->")} {source.Name}:{line}:{column}");
        writer.WriteLine($"{gutter} {colors.Accent("|")}");
        writer.WriteLine($"{colors.Accent(number)} {colors.Accent("|")} {Expand(text)}");
        writer.WriteLine($"{gutter} {colors.Accent("|")} {new string(' ', indent)}{mark(new string('^', underline))} {mark(diagnostic.Message)}");
    }

    // Tabs would throw the carets off, so every tab is shown as the same number of spaces in the source line and the carets.
    private static string Expand(string text) => text.Replace("\t", new string(' ', TabWidth), StringComparison.Ordinal);

    private static int DisplayWidth(string text, int characters)
    {
        var width = 0;
        foreach (var character in text.Take(characters))
        {
            width += character == '\t' ? TabWidth : 1;
        }

        return width;
    }

    /// <summary>ANSI colours, or plain text when colour is off.</summary>
    private sealed class Colors(bool enabled)
    {
        public string Error(string text) => Wrap("1;31", text);

        public string Warning(string text) => Wrap("1;33", text);

        public string Accent(string text) => Wrap("1;34", text);

        public string Bold(string text) => Wrap("1", text);

        private string Wrap(string code, string text) =>
            enabled ? new StringBuilder().Append("\u001b[").Append(code).Append('m').Append(text).Append("\u001b[0m").ToString() : text;
    }
}
