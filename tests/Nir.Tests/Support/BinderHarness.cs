using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Support;

/// <summary>Parses and binds source text for the binder tests, and describes each diagnostic as <c>"NIR3001 5:13"</c> (code, line:column).</summary>
internal static class BinderHarness
{
    public static (BoundProgram? Program, DiagnosticBag Bag, IReadOnlyList<string> Diagnostics) Bind(string text)
    {
        var source = new SourceText("test.nir", text);
        var bag = new DiagnosticBag();
        var program = Binder.Bind(SyntaxParser.Parse(source, bag), source, bag);

        var described = bag.Select(d =>
        {
            var (line, column) = d.Source.GetLineAndColumn(d.Span.Start);
            return $"{d.Code} {line}:{column}";
        }).ToList();
        return (program, bag, described);
    }
}
