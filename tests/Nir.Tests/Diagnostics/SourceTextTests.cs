using Nir.Compiler.Diagnostics;

namespace Nir.Tests.Diagnostics;

public class SourceTextTests
{
    [Fact]
    public void Position_zero_is_line_1_column_1()
    {
        var source = new SourceText("t.nir", "ab\ncd");

        Assert.Equal((1, 1), source.GetLineAndColumn(0));
    }

    [Fact]
    public void Position_after_newline_is_next_line_column_1()
    {
        var source = new SourceText("t.nir", "ab\ncd");

        Assert.Equal((2, 1), source.GetLineAndColumn(3));
    }

    [Fact]
    public void Position_in_middle_of_line()
    {
        var source = new SourceText("t.nir", "ab\ncd");

        Assert.Equal((2, 2), source.GetLineAndColumn(4));
    }

    [Fact]
    public void Carriage_return_before_newline_is_part_of_terminator()
    {
        var source = new SourceText("t.nir", "ab\r\ncd");

        Assert.Equal((2, 1), source.GetLineAndColumn(4));
        Assert.Equal("ab", source.GetLineText(1));
    }

    [Fact]
    public void Position_at_end_of_file_is_valid()
    {
        var source = new SourceText("t.nir", "ab");

        Assert.Equal((1, 3), source.GetLineAndColumn(2));
    }

    [Fact]
    public void Last_line_without_newline()
    {
        var source = new SourceText("t.nir", "ab\ncd");

        Assert.Equal("cd", source.GetLineText(2));
    }

    [Fact]
    public void Position_past_end_of_file_throws()
    {
        var source = new SourceText("t.nir", "ab");

        Assert.Throws<ArgumentOutOfRangeException>(() => source.GetLineAndColumn(3));
    }
}
