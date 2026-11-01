using System.Reflection;
using Nir.Compiler.Clr;

namespace Nir.Compiler.Emit;

/// <summary>The <c>System.Decimal</c> members the emitter calls: <c>decimal</c> has no instructions of its own, only operators.</summary>
internal static class NumberMethods
{
    public static readonly ConstructorInfo DecimalFromParts =
        ClrTypes.Decimal.GetConstructor([ClrTypes.Int32, ClrTypes.Int32, ClrTypes.Int32, ClrTypes.Boolean, ClrTypes.Byte])!;

    public static readonly MethodInfo Add = Operator("op_Addition");

    public static readonly MethodInfo Subtract = Operator("op_Subtraction");

    public static readonly MethodInfo Multiply = Operator("op_Multiply");

    public static readonly MethodInfo Divide = Operator("op_Division");

    public static readonly MethodInfo Remainder = Operator("op_Modulus");

    public static readonly MethodInfo Negate = ClrTypes.Decimal.GetMethod("op_UnaryNegation", [ClrTypes.Decimal])!;

    public static readonly MethodInfo Less = Operator("op_LessThan");

    public static readonly MethodInfo LessOrEqual = Operator("op_LessThanOrEqual");

    public static readonly MethodInfo Greater = Operator("op_GreaterThan");

    public static readonly MethodInfo GreaterOrEqual = Operator("op_GreaterThanOrEqual");

    public static readonly MethodInfo Equality = Operator("op_Equality");

    public static readonly MethodInfo Inequality = Operator("op_Inequality");

    /// <summary>The conversion that makes a <c>decimal</c> from a value of <paramref name="source"/> (every integer type, <c>char</c>, <c>float</c>, <c>double</c>).</summary>
    public static MethodInfo ToDecimal(Type source) => Conversion(returnType: ClrTypes.Decimal, parameterType: source);

    /// <summary>The conversion from <c>decimal</c> to <paramref name="target"/>; it cuts off the fraction and throws when the value does not fit.</summary>
    public static MethodInfo FromDecimal(Type target) => Conversion(returnType: target, parameterType: ClrTypes.Decimal);

    private static MethodInfo Operator(string name) => ClrTypes.Decimal.GetMethod(name, [ClrTypes.Decimal, ClrTypes.Decimal])!;

    private static MethodInfo Conversion(Type returnType, Type parameterType) =>
        ClrTypes.Decimal.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name is "op_Implicit" or "op_Explicit" && m.ReturnType == returnType && m.GetParameters()[0].ParameterType == parameterType);
}
