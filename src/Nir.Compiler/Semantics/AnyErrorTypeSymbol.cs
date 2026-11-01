namespace Nir.Compiler.Semantics;

/// <summary>
/// <c>anyerror</c>: any error. Every declared error type converts to it. It compiles to the class all error types derive from,
/// which holds the message.
/// </summary>
public sealed class AnyErrorTypeSymbol : TypeSymbol
{
    public static readonly AnyErrorTypeSymbol Instance = new();

    private AnyErrorTypeSymbol()
        : base(TypeNames.AnyErrorType)
    {
        Message = new FieldSymbol(ReservedNames.MessageField, StringType, this);
    }

    /// <summary>The one field every error has: <c>e.message</c>.</summary>
    public FieldSymbol Message { get; }
}
