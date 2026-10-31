namespace Nir.Compiler.Semantics;

/// <summary>A local variable declared with <c>let</c> or <c>let mut</c>.</summary>
public sealed class VariableSymbol(string name, TypeSymbol type, bool isMutable)
{
    public string Name { get; } = name;

    public TypeSymbol Type { get; } = type;

    public bool IsMutable { get; } = isMutable;
}
