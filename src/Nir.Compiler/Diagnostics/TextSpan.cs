namespace Nir.Compiler.Diagnostics;

/// <summary>A range of characters in a <see cref="SourceText"/>: <paramref name="Length"/> characters from offset <paramref name="Start"/>.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}
