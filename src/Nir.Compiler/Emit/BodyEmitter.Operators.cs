using System.Diagnostics;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Operators and strings: arithmetic, comparison, string joins and interpolation.</summary>
internal sealed partial class BodyEmitter
{
    // `+` joins, `==` and `!=` compare the characters. No other operator is allowed on strings (the binder checks).
    private void EmitStringOperator(BoundBinaryOperator op)
    {
        switch (op)
        {
            case BoundBinaryOperator.Add:
                il.Emit(OpCodes.Call, StringMethods.Concat2);
                break;
            case BoundBinaryOperator.Equal:
                il.Emit(OpCodes.Call, StringMethods.Equality);
                break;
            case BoundBinaryOperator.NotEqual:
                il.Emit(OpCodes.Call, StringMethods.Equality);
                EmitNot();
                break;
            default:
                throw new UnreachableException();
        }
    }

    // Every part is already a string. Up to four parts use the matching String.Concat overload; one part is joined with ""
    // so that a null becomes an empty string; more parts go through a string array.
    private void EmitInterpolated(BoundInterpolatedString interpolated)
    {
        var parts = interpolated.Parts;
        switch (parts.Length)
        {
            case 0:
                il.Emit(OpCodes.Ldstr, string.Empty);
                break;

            case 1:
                EmitExpression(parts[0]);
                il.Emit(OpCodes.Ldstr, string.Empty);
                il.Emit(OpCodes.Call, StringMethods.Concat2);
                break;

            case 2 or 3 or 4:
                foreach (var part in parts)
                {
                    EmitExpression(part);
                }

                il.Emit(OpCodes.Call, parts.Length switch { 2 => StringMethods.Concat2, 3 => StringMethods.Concat3, _ => StringMethods.Concat4 });
                break;

            default:
                il.Emit(OpCodes.Ldc_I4, parts.Length);
                il.Emit(OpCodes.Newarr, TypeSymbol.StringType.ClrType);
                for (var i = 0; i < parts.Length; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(parts[i]);
                    il.Emit(OpCodes.Stelem_Ref);
                }

                il.Emit(OpCodes.Call, StringMethods.ConcatArray);
                break;
        }
    }

    // Negation of an int is 0 - x so that int.MinValue overflows (throws) like the other checked operations.
    private void EmitUnary(BoundUnaryExpression unary)
    {
        if (unary.Operator == BoundUnaryOperator.Not)
        {
            EmitExpression(unary.Operand);
            EmitNot();
            return;
        }

        if (unary.Operand.Type == TypeSymbol.DoubleType)
        {
            EmitExpression(unary.Operand);
            il.Emit(OpCodes.Neg);
            return;
        }

        il.Emit(OpCodes.Ldc_I4_0);
        EmitExpression(unary.Operand);
        il.Emit(OpCodes.Sub_Ovf);
    }

    // Integer + - * are checked (design.md, "Numbers"); the "or equal" forms use the unordered compare for double so a NaN
    // operand makes them false.
    private void EmitBinaryOperator(BoundBinaryOperator op, bool isDouble)
    {
        switch (op)
        {
            case BoundBinaryOperator.Add:
                il.Emit(isDouble ? OpCodes.Add : OpCodes.Add_Ovf);
                break;
            case BoundBinaryOperator.Subtract:
                il.Emit(isDouble ? OpCodes.Sub : OpCodes.Sub_Ovf);
                break;
            case BoundBinaryOperator.Multiply:
                il.Emit(isDouble ? OpCodes.Mul : OpCodes.Mul_Ovf);
                break;
            case BoundBinaryOperator.Divide:
                il.Emit(OpCodes.Div);
                break;
            case BoundBinaryOperator.Remainder:
                il.Emit(OpCodes.Rem);
                break;
            case BoundBinaryOperator.Less:
                il.Emit(OpCodes.Clt);
                break;
            case BoundBinaryOperator.Greater:
                il.Emit(OpCodes.Cgt);
                break;
            case BoundBinaryOperator.LessOrEqual:
                il.Emit(isDouble ? OpCodes.Cgt_Un : OpCodes.Cgt);
                EmitNot();
                break;
            case BoundBinaryOperator.GreaterOrEqual:
                il.Emit(isDouble ? OpCodes.Clt_Un : OpCodes.Clt);
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
    }

    private void EmitNot()
    {
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }
}
