namespace Nir.Compiler.Diagnostics;

/// <summary>A named piece of source code. Lines end at <c>\n</c>; a <c>\r</c> directly before it is part of the terminator.</summary>
public sealed record SourceText(string Name, string Text)
{
    /// <summary>Converts a character offset to a 1-based line and column. An offset equal to the text length is valid.</summary>
    public (int Line, int Column) GetLineAndColumn(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Text.Length);

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < position; i++)
        {
            if (Text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, position - lineStart + 1);
    }

    /// <summary>Returns the text of a 1-based line without its terminator.</summary>
    public string GetLineText(int line)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);

        var start = 0;
        for (var current = 1; current < line; current++)
        {
            var newline = Text.IndexOf('\n', start);
            if (newline < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line));
            }

            start = newline + 1;
        }

        var end = Text.IndexOf('\n', start);
        if (end < 0)
        {
            end = Text.Length;
        }

        if (end > start && Text[end - 1] == '\r')
        {
            end--;
        }

        return Text[start..end];
    }
}
