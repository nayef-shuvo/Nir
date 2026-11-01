using System.Globalization;

namespace Nir.Compiler.Clr;

/// <summary>The names of the types, members and methods the compiler generates in the compiled program.</summary>
internal static class GeneratedNames
{
    /// <summary>The class every error derives from.</summary>
    public const string AnyErrorClass = "Nir.AnyError";

    public const string AnyErrorMessage = "Message";

    /// <summary>The start of the name of a result type; a number follows.</summary>
    public const string ResultTypePrefix = "Nir.Result";

    /// <summary>The field of a union or result that says which member it holds.</summary>
    public const string TagField = "Tag";

    public const string CaseFieldPrefix = "Case";

    /// <summary>The start of the name of a helper that runs a .NET call in a try block; a number follows.</summary>
    public const string ProtectedHelperPrefix = "Protected";

    /// <summary>The entry point generated for a <c>main</c> that returns <c>E!</c>.</summary>
    public const string ErrorEntryPoint = "Main";

    /// <summary>What is printed before an error's message when it escapes <c>main</c>.</summary>
    public const string ErrorOutputPrefix = "error: ";

    /// <summary>The name of the field of a <c>ValueTuple</c> that holds element <c>index + 1</c> (<c>Item1</c> ... <c>Item7</c>).</summary>
    public static string TupleItem(int index) => "Item" + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>The field of a <c>ValueTuple</c> that holds the elements past the seventh.</summary>
    public const string TupleRest = "Rest";

    public static string CaseField(int index) => CaseFieldPrefix + index.ToString(CultureInfo.InvariantCulture);
}
