using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Errors: error values, <c>try</c>, <c>catch</c>, results with no value.</summary>
internal sealed partial class BodyEmitter
{
    // The object is built, its fields are set in the order written, and its Message is taken from the field called `message`
    // if there is one, else it is the name of the type.
    private void EmitErrorLiteral(BoundErrorLiteral literal)
    {
        var error = il.DeclareLocal(context.ClrOf(literal.Error));
        il.Emit(OpCodes.Newobj, context.ErrorConstructors[literal.Error]);
        il.Emit(OpCodes.Stloc, error);
        foreach (var initializer in literal.Initializers)
        {
            il.Emit(OpCodes.Ldloc, error);
            EmitExpression(initializer.Value);
            il.Emit(OpCodes.Stfld, context.Fields[initializer.Field]);
        }

        il.Emit(OpCodes.Ldloc, error);
        if (literal.Error.FindField(ReservedNames.MessageField) is { } message && message.Type == TypeSymbol.StringType)
        {
            il.Emit(OpCodes.Ldloc, error);
            il.Emit(OpCodes.Ldfld, context.Fields[message]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, literal.Error.Name);
        }

        il.Emit(OpCodes.Stfld, context.AnyErrorMessage);
        il.Emit(OpCodes.Ldloc, error);
    }

    // A zeroed result has tag 0, the error; success is tag 1.
    public void EmitUnitSuccess(ResultTypeSymbol result)
    {
        var layout = context.Unions[result];
        var temporary = il.DeclareLocal(layout.Type);
        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Initobj, layout.Type);
        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Ldc_I4, ResultTypeSymbol.SuccessTag);
        il.Emit(OpCodes.Stfld, layout.Tag);
        il.Emit(OpCodes.Ldloc, temporary);
    }

    // On an error, return it from the function (as the function's own result type); on a success, the value is left on the stack.
    private void EmitTry(BoundTry tryExpression)
    {
        var operandType = (ResultTypeSymbol)tryExpression.Operand.Type;
        var layout = context.Unions[operandType];
        EmitExpression(tryExpression.Operand);
        var outcome = il.DeclareLocal(layout.Type);
        il.Emit(OpCodes.Stloc, outcome);

        var succeeded = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Tag);
        il.Emit(OpCodes.Brtrue, succeeded);

        if (tryExpression.FunctionResult == operandType)
        {
            il.Emit(OpCodes.Ldloc, outcome);
        }
        else
        {
            // The function's error type is wider (anyerror): build its result with the same error.
            var own = context.Unions[tryExpression.FunctionResult];
            var widened = il.DeclareLocal(own.Type);
            il.Emit(OpCodes.Ldloca, widened);
            il.Emit(OpCodes.Initobj, own.Type);
            il.Emit(OpCodes.Ldloca, widened);
            il.Emit(OpCodes.Ldloca, outcome);
            il.Emit(OpCodes.Ldfld, layout.Cases[0]);
            il.Emit(OpCodes.Stfld, own.Cases[0]);
            il.Emit(OpCodes.Ldloc, widened);
        }

        il.Emit(OpCodes.Ret);
        il.MarkLabel(succeeded);
        if (tryExpression.ValueType != TypeSymbol.UnitType)
        {
            il.Emit(OpCodes.Ldloca, outcome);
            il.Emit(OpCodes.Ldfld, layout.Cases[ResultTypeSymbol.SuccessTag]);
        }
    }

    // The result is stored once. On a success its value is the value of the catch; on an error the handler runs (with the error
    // in its variable): a value to use, or a block, which has returned or has nothing to produce.
    private void EmitCatch(BoundCatch catchExpression)
    {
        var operandType = (ResultTypeSymbol)catchExpression.Operand.Type;
        var layout = context.Unions[operandType];
        EmitExpression(catchExpression.Operand);
        var outcome = il.DeclareLocal(layout.Type);
        il.Emit(OpCodes.Stloc, outcome);

        var value = catchExpression.ValueType == TypeSymbol.UnitType ? null : il.DeclareLocal(context.ClrOf(catchExpression.ValueType));
        var succeeded = il.DefineLabel();
        var end = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Tag);
        il.Emit(OpCodes.Brtrue, succeeded);

        if (catchExpression.Variable is { } variable)
        {
            var local = il.DeclareLocal(context.ClrOf(variable.Type));
            _locals[variable] = local;
            il.Emit(OpCodes.Ldloca, outcome);
            il.Emit(OpCodes.Ldfld, layout.Cases[0]);
            il.Emit(OpCodes.Stloc, local);
        }

        if (catchExpression.Value is { } handler)
        {
            EmitExpression(handler);
            if (value is not null)
            {
                il.Emit(OpCodes.Stloc, value);
            }
        }
        else
        {
            EmitBlock(catchExpression.Body!);
        }

        il.Emit(OpCodes.Br, end);
        il.MarkLabel(succeeded);
        if (value is not null)
        {
            il.Emit(OpCodes.Ldloca, outcome);
            il.Emit(OpCodes.Ldfld, layout.Cases[ResultTypeSymbol.SuccessTag]);
            il.Emit(OpCodes.Stloc, value);
        }

        il.MarkLabel(end);
        if (value is not null)
        {
            il.Emit(OpCodes.Ldloc, value);
        }
    }
}
