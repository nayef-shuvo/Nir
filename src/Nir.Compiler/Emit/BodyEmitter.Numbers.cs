using System.Diagnostics;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Numbers and characters: literals, arithmetic and comparison for every width, and checked conversions.</summary>
internal sealed partial class BodyEmitter
{
    private void EmitNumberLiteral(BoundNumberLiteral literal)
    {
        switch (literal.Value)
        {
            case byte or sbyte or short or ushort or int:
                il.Emit(OpCodes.Ldc_I4, System.Convert.ToInt32(literal.Value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case uint u:
                il.Emit(OpCodes.Ldc_I4, unchecked((int)u));
                break;
            case long l:
                il.Emit(OpCodes.Ldc_I8, l);
                break;
            case ulong ul:
                il.Emit(OpCodes.Ldc_I8, unchecked((long)ul));
                break;
            case float f:
                il.Emit(OpCodes.Ldc_R4, f);
                break;
            case double d:
                il.Emit(OpCodes.Ldc_R8, d);
                break;
            case decimal m:
                var bits = decimal.GetBits(m);
                il.Emit(OpCodes.Ldc_I4, bits[0]);
                il.Emit(OpCodes.Ldc_I4, bits[1]);
                il.Emit(OpCodes.Ldc_I4, bits[2]);
                il.Emit(bits[3] < 0 ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_I4, (bits[3] >> 16) & 0xFF);
                il.Emit(OpCodes.Newobj, NumberMethods.DecimalFromParts);
                break;
            default:
                throw new UnreachableException();
        }
    }

    // Negation: int and long as 0 - x so that the smallest value overflows (throws) like the other checked operations; the narrower
    // signed types are negated in 32 bits and then narrowed again with a check.
    private void EmitNegate(BoundExpression operand)
    {
        var type = operand.Type;
        if (NumberTypes.IsFloatingPoint(type))
        {
            EmitExpression(operand);
            il.Emit(OpCodes.Neg);
        }
        else if (NumberTypes.IsDecimal(type))
        {
            EmitExpression(operand);
            il.Emit(OpCodes.Call, NumberMethods.Negate);
        }
        else if (NumberTypes.IsSmallInteger(type))
        {
            EmitExpression(operand);
            il.Emit(OpCodes.Neg);
            EmitNarrow(type);
        }
        else
        {
            if (NumberTypes.Bits(type) == 64)
            {
                il.Emit(OpCodes.Ldc_I8, 0L);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
            }

            EmitExpression(operand);
            il.Emit(OpCodes.Sub_Ovf);
        }
    }

    // The checked narrowing after 8 and 16-bit arithmetic, which is done in 32 bits: it throws when the result does not fit.
    private void EmitNarrow(TypeSymbol type) => il.Emit(NumberConversions.CheckedTo(type, unsignedSource: false));

    // Integer + - * are checked (design.md, "Numbers"); the "or equal" forms use the unordered compare for float and double so a
    // NaN operand makes them false. uint and ulong use the unsigned instructions.
    private void EmitBinaryOperator(BoundBinaryOperator op, TypeSymbol type)
    {
        if (NumberTypes.IsDecimal(type))
        {
            EmitDecimalOperator(op);
            return;
        }

        var isFloat = NumberTypes.IsFloatingPoint(type);
        var unsigned = NumberTypes.IsUnsignedOnStack(type);
        switch (op)
        {
            case BoundBinaryOperator.Add:
                il.Emit(isFloat ? OpCodes.Add : unsigned ? OpCodes.Add_Ovf_Un : OpCodes.Add_Ovf);
                break;
            case BoundBinaryOperator.Subtract:
                il.Emit(isFloat ? OpCodes.Sub : unsigned ? OpCodes.Sub_Ovf_Un : OpCodes.Sub_Ovf);
                break;
            case BoundBinaryOperator.Multiply:
                il.Emit(isFloat ? OpCodes.Mul : unsigned ? OpCodes.Mul_Ovf_Un : OpCodes.Mul_Ovf);
                break;
            case BoundBinaryOperator.Divide:
                il.Emit(unsigned ? OpCodes.Div_Un : OpCodes.Div);
                break;
            case BoundBinaryOperator.Remainder:
                il.Emit(unsigned ? OpCodes.Rem_Un : OpCodes.Rem);
                break;
            case BoundBinaryOperator.Less:
                il.Emit(unsigned ? OpCodes.Clt_Un : OpCodes.Clt);
                break;
            case BoundBinaryOperator.Greater:
                il.Emit(unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt);
                break;
            case BoundBinaryOperator.LessOrEqual:
                il.Emit(isFloat || unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt);
                EmitNot();
                break;
            case BoundBinaryOperator.GreaterOrEqual:
                il.Emit(isFloat || unsigned ? OpCodes.Clt_Un : OpCodes.Clt);
                EmitNot();
                break;
            case BoundBinaryOperator.Equal:
                il.Emit(OpCodes.Ceq);
                break;
            case BoundBinaryOperator.NotEqual:
                il.Emit(OpCodes.Ceq);
                EmitNot();
                break;
            default:
                throw new UnreachableException();
        }

        // 8 and 16-bit values are added, multiplied, ... as 32-bit ones; the result must still fit the narrow type.
        if (NumberTypes.IsSmallInteger(type) && op is BoundBinaryOperator.Add or BoundBinaryOperator.Subtract or BoundBinaryOperator.Multiply or BoundBinaryOperator.Divide)
        {
            EmitNarrow(type);
        }
    }

    private void EmitDecimalOperator(BoundBinaryOperator op) =>
        il.Emit(OpCodes.Call, op switch
        {
            BoundBinaryOperator.Add => NumberMethods.Add,
            BoundBinaryOperator.Subtract => NumberMethods.Subtract,
            BoundBinaryOperator.Multiply => NumberMethods.Multiply,
            BoundBinaryOperator.Divide => NumberMethods.Divide,
            BoundBinaryOperator.Remainder => NumberMethods.Remainder,
            BoundBinaryOperator.Less => NumberMethods.Less,
            BoundBinaryOperator.LessOrEqual => NumberMethods.LessOrEqual,
            BoundBinaryOperator.Greater => NumberMethods.Greater,
            BoundBinaryOperator.GreaterOrEqual => NumberMethods.GreaterOrEqual,
            BoundBinaryOperator.Equal => NumberMethods.Equality,
            BoundBinaryOperator.NotEqual => NumberMethods.Inequality,
            _ => throw new UnreachableException(),
        });
}
