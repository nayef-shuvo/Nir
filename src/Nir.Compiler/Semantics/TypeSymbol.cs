namespace Nir.Compiler.Semantics;

/// <summary>A Nir type and the CLR type it compiles to.</summary>
public sealed class TypeSymbol
{
    /// <summary>The "nothing" type <c>()</c>. Compiles to <c>void</c>.</summary>
    public static readonly TypeSymbol UnitType = new("()", typeof(void));

    public static readonly TypeSymbol StringType = new("string", typeof(string));

    private TypeSymbol(string name, Type clrType)
    {
        Name = name;
        ClrType = clrType;
    }

    public string Name { get; }

    public Type ClrType { get; }

    public override string ToString() => Name;
}
