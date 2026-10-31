using System.Collections;

namespace Nir.Compiler.Diagnostics;

/// <summary>Collects diagnostics, in the order they are reported, while a compilation runs.</summary>
public sealed class DiagnosticBag : IReadOnlyList<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public bool HasErrors => _diagnostics.Any(d => d.Severity == Severity.Error);

    public int Count => _diagnostics.Count;

    public Diagnostic this[int index] => _diagnostics[index];

    public void Report(string code, Severity severity, string message, SourceText source, TextSpan span) =>
        _diagnostics.Add(new Diagnostic(code, severity, message, source, span));

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
