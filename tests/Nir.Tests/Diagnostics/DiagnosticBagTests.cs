using Nir.Compiler.Diagnostics;

namespace Nir.Tests.Diagnostics;

public class DiagnosticBagTests
{
    private static readonly SourceText _source = new("t.nir", "x");

    [Fact]
    public void Empty_bag_has_no_errors()
    {
        var bag = new DiagnosticBag();

        Assert.False(bag.HasErrors);
        Assert.Empty(bag);
    }

    [Fact]
    public void Warning_alone_is_not_an_error()
    {
        var bag = new DiagnosticBag();

        bag.Report("NIR9999", Severity.Warning, "careful", _source, new TextSpan(0, 1));

        Assert.False(bag.HasErrors);
        Assert.Single(bag);
    }

    [Fact]
    public void Error_sets_HasErrors_and_keeps_order()
    {
        var bag = new DiagnosticBag();

        bag.Report("NIR0001", Severity.Error, "first", _source, new TextSpan(0, 1));
        bag.Report("NIR0002", Severity.Error, "second", _source, new TextSpan(0, 1));

        Assert.True(bag.HasErrors);
        Assert.Equal(["NIR0001", "NIR0002"], bag.Select(d => d.Code));
    }

    [Fact]
    public void Span_end_is_start_plus_length()
    {
        Assert.Equal(7, new TextSpan(3, 4).End);
    }
}
