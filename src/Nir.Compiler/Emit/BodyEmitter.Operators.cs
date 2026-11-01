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

    private void EmitUnary(BoundUnaryExpression unary)
    {
        if (unary.Operator == BoundUnaryOperator.Not)
        {
            EmitExpression(unary.Operand);
            EmitNot();
            return;
        }

        EmitNegate(unary.Operand);
    }

    private void EmitNot()
    {
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }
}
