namespace Nir.Compiler.Semantics;

/// <summary>A Nir type and the CLR type it compiles to.</summary>
public sealed class TypeSymbol
{
    /// <summary>The "nothing" type <c>()</c>. Compiles to <c>void</c>.</summary>
    public static readonly TypeSymbol UnitType = new("()", typeof(void));

    public static readonly TypeSymbol IntType = new("int", typeof(int));

    public static readonly TypeSymbol DoubleType = new("double", typeof(double));

    public static readonly TypeSymbol BoolType = new("bool", typeof(bool));

    public static readonly TypeSymbol StringType = new("string", typeof(string));

    /// <summary>
    /// The type of an expression that already had an error. Operations on it report nothing more, so one mistake
    /// produces one diagnostic. It never reaches Emit, because a program with errors is not emitted.
    /// </summary>
    public static readonly TypeSymbol ErrorType = new("?", typeof(object));

    private TypeSymbol(string name, Type clrType)
    {
        Name = name;
        ClrType = clrType;
    }

    public string Name { get; }

    public Type ClrType { get; }

    public override string ToString() => Name;
}
