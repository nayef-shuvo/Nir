using System.Reflection;
using Nir.Compiler.Clr;

namespace Nir.Compiler.Emit;

/// <summary>The <c>System.String</c> methods the emitter calls, taken from the same load context as every other CLR type.</summary>
internal static class StringMethods
{
    private static readonly Type _string = ClrTypes.String;

    public static readonly MethodInfo Concat2 = _string.GetMethod("Concat", [_string, _string])!;

    public static readonly MethodInfo Concat3 = _string.GetMethod("Concat", [_string, _string, _string])!;

    public static readonly MethodInfo Concat4 = _string.GetMethod("Concat", [_string, _string, _string, _string])!;

    public static readonly MethodInfo ConcatArray = _string.GetMethod("Concat", [_string.MakeArrayType()])!;

    /// <summary>Ordinal equality of two strings (<c>==</c> in C#).</summary>
    public static readonly MethodInfo Equality = _string.GetMethod("op_Equality", [_string, _string])!;
}
