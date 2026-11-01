namespace Nir.Compiler.Language;

/// <summary>The punctuation of Nir that the compiler also needs as text: in messages and in the names of types it builds.</summary>
public static class Punctuation
{
    /// <summary>Separates the parts of a path: <c>clr::cs::System::IO</c>, <c>Point::new</c>.</summary>
    public const string PathSeparator = "::";

    /// <summary>Between an error type and a value type: <c>ParseError!int</c>.</summary>
    public const string ResultMarker = "!";

    public const string SharedBorrow = "&";

    public const string MutableBorrowKeyword = "&mut";

    /// <summary><c>&amp;mut </c>, ready to put in front of a type name.</summary>
    public const string MutableBorrow = MutableBorrowKeyword + " ";
}
