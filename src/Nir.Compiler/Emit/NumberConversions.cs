using System.Diagnostics;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>
/// The IL for <c>value as T</c> between number types and <c>char</c>. It takes the <see cref="ILGenerator"/> because the same
/// conversion is also written inside the generated helpers that run it under <c>catch</c>.
/// </summary>
internal static class NumberConversions
{
    // `value as target`. Every conversion is checked: a value that does not fit throws an OverflowException. A fraction going to an
    // integer is cut off toward zero (design.md "Strings and conversions").
    public static void Emit(ILGenerator il, TypeSymbol from, TypeSymbol to)
    {
        if (from == to)
        {
            return;
        }

        if (NumberTypes.IsDecimal(to))
        {
            il.Emit(OpCodes.Call, NumberMethods.ToDecimal(from.ClrType));
        }
        else if (NumberTypes.IsDecimal(from))
        {
            il.Emit(OpCodes.Call, NumberMethods.FromDecimal(to.ClrType));
        }
        else if (NumberTypes.IsFloatingPoint(to))
        {
            if (NumberTypes.IsUnsignedInteger(from) || NumberTypes.IsChar(from))
            {
                il.Emit(OpCodes.Conv_R_Un);
            }

            il.Emit(to == TypeSymbol.FloatType ? OpCodes.Conv_R4 : OpCodes.Conv_R8);
        }
        else
        {
            il.Emit(CheckedTo(to, unsignedSource: NumberTypes.IsUnsignedInteger(from) || NumberTypes.IsChar(from)));
        }
    }

    // The checked conversion instruction to an integer type or char. The ".un" form reads an integer source as unsigned.
    public static OpCode CheckedTo(TypeSymbol to, bool unsignedSource)
    {
        if (to == TypeSymbol.SByteType) { return unsignedSource ? OpCodes.Conv_Ovf_I1_Un : OpCodes.Conv_Ovf_I1; }
        if (to == TypeSymbol.ByteType) { return unsignedSource ? OpCodes.Conv_Ovf_U1_Un : OpCodes.Conv_Ovf_U1; }
        if (to == TypeSymbol.ShortType) { return unsignedSource ? OpCodes.Conv_Ovf_I2_Un : OpCodes.Conv_Ovf_I2; }
        if (to == TypeSymbol.UShortType || to == TypeSymbol.CharType) { return unsignedSource ? OpCodes.Conv_Ovf_U2_Un : OpCodes.Conv_Ovf_U2; }
        if (to == TypeSymbol.IntType) { return unsignedSource ? OpCodes.Conv_Ovf_I4_Un : OpCodes.Conv_Ovf_I4; }
        if (to == TypeSymbol.UIntType) { return unsignedSource ? OpCodes.Conv_Ovf_U4_Un : OpCodes.Conv_Ovf_U4; }
        if (to == TypeSymbol.LongType) { return unsignedSource ? OpCodes.Conv_Ovf_I8_Un : OpCodes.Conv_Ovf_I8; }
        if (to == TypeSymbol.ULongType) { return unsignedSource ? OpCodes.Conv_Ovf_U8_Un : OpCodes.Conv_Ovf_U8; }
        throw new UnreachableException();
    }
}
