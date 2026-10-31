using Nir.Compiler.Diagnostics;

namespace Nir.Cli;

internal static class DiagnosticPrinter
{
    /// <summary>Writes one line per diagnostic: <c>file:line:column: error NIR1234: message</c>.</summary>
    public static void Print(TextWriter writer, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var (line, column) = diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start);
            var severity = diagnostic.Severity.ToString().ToLowerInvariant();
            writer.WriteLine($"{diagnostic.Source.Name}:{line}:{column}: {severity} {diagnostic.Code}: {diagnostic.Message}");
        }
    }
}
