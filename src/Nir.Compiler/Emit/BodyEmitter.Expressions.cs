using System.Diagnostics;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Expressions and places (addresses).</summary>
internal sealed partial class BodyEmitter
{
    private void EmitExpression(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundStringLiteral s:
                il.Emit(OpCodes.Ldstr, s.Value);
                break;

            case BoundIntLiteral i:
                il.Emit(OpCodes.Ldc_I4, i.Value);
                break;

            case BoundDoubleLiteral d:
                il.Emit(OpCodes.Ldc_R8, d.Value);
                break;

            case BoundBoolLiteral b:
                il.Emit(b.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                break;

            case BoundVariableExpression variable when _arguments.TryGetValue(variable.Variable, out var index):
                il.Emit(OpCodes.Ldarg, index);
                break;

            case BoundVariableExpression variable:
                il.Emit(OpCodes.Ldloc, _locals[variable.Variable]);
                break;

            case BoundMethodCall methodCall:
                // The receiver goes by address, so a `&mut self` method changes the caller's struct.
                EmitAddress(methodCall.Receiver);
                foreach (var argument in methodCall.Arguments)
                {
                    EmitExpression(argument);
                }

                il.Emit(OpCodes.Call, context.Methods[methodCall.Method]);
                break;

            case BoundBorrowExpression borrow:
                EmitAddress(borrow.Place);
                break;

            case BoundDereference dereference:
                EmitExpression(dereference.Borrow);
                il.Emit(OpCodes.Ldobj, context.ClrOf(dereference.Element));
                break;

            case BoundStructLiteral structLiteral:
                EmitStructLiteral(structLiteral);
                break;

            case BoundUnionWrap wrap:
                EmitUnionWrap(wrap);
                break;

            case BoundErrorLiteral errorLiteral:
                EmitErrorLiteral(errorLiteral);
                break;

            case BoundProtectedCall protectedCall:
                EmitProtectedCall(protectedCall);
                break;

            case BoundErrorMessage errorMessage:
                il.Emit(OpCodes.Newobj, context.AnyErrorConstructor);
                il.Emit(OpCodes.Dup);
                EmitExpression(errorMessage.Message);
                il.Emit(OpCodes.Stfld, context.AnyErrorMessage);
                break;

            case BoundErrorToAny toAny:
                EmitExpression(toAny.Value); // every error object already is an AnyError
                break;

            case BoundUnitSuccess unitSuccess:
                EmitUnitSuccess(unitSuccess.Result);
                break;

            case BoundTry tryExpression:
                EmitTry(tryExpression);
                break;

            case BoundCatch catchExpression:
                EmitCatch(catchExpression);
                break;

            case BoundMatch match:
                EmitMatch(match);
                break;

            case BoundTupleLiteral tupleLiteral:
                EmitTupleLiteral([.. tupleLiteral.Tuple.Elements.Select(element => element.Type)], tupleLiteral.Elements);
                break;

            case BoundFieldAccess fieldAccess:
                // Reading through the address avoids copying the whole struct just to look at one field.
                var fieldPath = context.FieldPath(fieldAccess.Field);
                if (BoundPlaces.IsPlace(fieldAccess.Receiver))
                {
                    EmitAddress(fieldAccess.Receiver);
                    EmitFieldLoad(fieldPath);
                }
                else
                {
                    EmitExpression(fieldAccess.Receiver);
                    foreach (var step in fieldPath)
                    {
                        il.Emit(OpCodes.Ldfld, step);
                    }
                }
                break;

            case BoundUnaryExpression unary:
                EmitUnary(unary);
                break;

            case BoundBinaryExpression { Operator: BoundBinaryOperator.And or BoundBinaryOperator.Or } logical:
                EmitShortCircuit(logical);
                break;

            case BoundBinaryExpression binary:
                EmitExpression(binary.Left);
                EmitExpression(binary.Right);
                if (binary.Left.Type == TypeSymbol.StringType)
                {
                    EmitStringOperator(binary.Operator);
                }
                else
                {
                    EmitBinaryOperator(binary.Operator, isDouble: binary.Left.Type == TypeSymbol.DoubleType);
                }

                break;

            case BoundInterpolatedString interpolated:
                EmitInterpolated(interpolated);
                break;

            case BoundConversion conversion:
                EmitExpression(conversion.Operand);

                // int to double is exact. double to int cuts off the fraction and throws when the value does not fit
                // (or is NaN), like the checked arithmetic (design.md "Strings and conversions").
                il.Emit(conversion.Target == TypeSymbol.DoubleType ? OpCodes.Conv_R8 : OpCodes.Conv_Ovf_I4);
                break;

            case BoundMemberCallExpression member:
                EmitMemberCall(member);
                break;

            case BoundCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    EmitExpression(argument);
                }

                switch (call.Target)
                {
                    case ClrMethodSymbol clrMethod:
                        il.Emit(OpCodes.Call, clrMethod.Method);
                        break;
                    case UserFunctionSymbol user:
                        il.Emit(OpCodes.Call, context.Methods[user]);
                        break;
                    default:
                        throw new UnreachableException();
                }

                break;

            default:
                throw new UnreachableException();
        }
    }

    // Pushes the address of a place. Anything else (a call result, say) is stored in a temporary first.
    private void EmitAddress(BoundExpression place)
    {
        switch (place)
        {
            // An error is an object: its reference is what a field access needs, not the address of the variable holding it.
            case { Type: StructTypeSymbol { IsError: true } or AnyErrorTypeSymbol }:
                EmitExpression(place);
                break;

            // A borrow already is an address (a managed pointer), so it is loaded, not addressed.
            case BoundVariableExpression variable when variable.Variable.Type is BorrowTypeSymbol:
                il.Emit(OpCodes.Ldarg, _arguments[variable.Variable]);
                break;

            case BoundVariableExpression variable when _arguments.TryGetValue(variable.Variable, out var index):
                il.Emit(OpCodes.Ldarga, index);
                break;

            case BoundVariableExpression variable:
                il.Emit(OpCodes.Ldloca, _locals[variable.Variable]);
                break;

            case BoundFieldAccess access:
                EmitAddress(access.Receiver);
                foreach (var step in context.FieldPath(access.Field))
                {
                    il.Emit(OpCodes.Ldflda, step);
                }

                break;

            default:
                EmitExpression(place);
                var temporary = il.DeclareLocal(context.ClrOf(place.Type));
                il.Emit(OpCodes.Stloc, temporary);
                il.Emit(OpCodes.Ldloca, temporary);
                break;
        }
    }
}
