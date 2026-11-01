using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Unions and <c>match</c>.</summary>
internal sealed partial class BodyEmitter
{
    // Build in a temporary like a struct literal: zero it, store the tag and the value, load the finished union.
    private void EmitUnionWrap(BoundUnionWrap wrap)
    {
        var layout = context.Unions[wrap.Union];
        var temporary = il.DeclareLocal(layout.Type);

        // The value is worked out first and kept in a local: it may be a `try`, which returns early, and that needs an empty stack.
        EmitExpression(wrap.Value);
        var value = il.DeclareLocal(context.ClrOf(wrap.Value.Type));
        il.Emit(OpCodes.Stloc, value);

        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Initobj, layout.Type);
        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Ldc_I4, wrap.Index);
        il.Emit(OpCodes.Stfld, layout.Tag);
        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stfld, layout.Cases[wrap.Index]);
        il.Emit(OpCodes.Ldloc, temporary);
    }

    // The union is stored once. Each arm tests the tag; the last needs no test because the binder checked that the arms
    // cover every member. The value of a value-match goes through a local so the stack is the same on every path.
    private void EmitMatch(BoundMatch match)
    {
        var layout = context.Unions[match.Union];
        EmitExpression(match.Scrutinee);
        var scrutinee = il.DeclareLocal(layout.Type);
        il.Emit(OpCodes.Stloc, scrutinee);

        var result = match.ResultType == TypeSymbol.UnitType ? null : il.DeclareLocal(context.ClrOf(match.ResultType));
        var end = il.DefineLabel();
        for (var i = 0; i < match.Arms.Length; i++)
        {
            var arm = match.Arms[i];
            var next = il.DefineLabel();
            if (i < match.Arms.Length - 1)
            {
                il.Emit(OpCodes.Ldloca, scrutinee);
                il.Emit(OpCodes.Ldfld, layout.Tag);
                il.Emit(OpCodes.Ldc_I4, arm.Index);
                il.Emit(OpCodes.Bne_Un, next);
            }

            if (arm.Variable is { } variable)
            {
                var local = il.DeclareLocal(context.ClrOf(variable.Type));
                _locals[variable] = local;
                il.Emit(OpCodes.Ldloca, scrutinee);
                il.Emit(OpCodes.Ldfld, layout.Cases[arm.Index]);
                il.Emit(OpCodes.Stloc, local);
            }

            if (arm.Value is { } value)
            {
                EmitExpression(value);
                if (result is not null)
                {
                    il.Emit(OpCodes.Stloc, result);
                }
            }
            else
            {
                EmitBlock(arm.Body!);
            }

            // An arm that always returns has nothing to jump over; leaving the jump out also keeps a function that ends
            // with such a match from having a jump target past its last instruction.
            if (arm.Body is null || !BoundFlow.AlwaysReturns(arm.Body))
            {
                il.Emit(OpCodes.Br, end);
            }

            il.MarkLabel(next);
        }

        il.MarkLabel(end);
        if (result is not null)
        {
            il.Emit(OpCodes.Ldloc, result);
        }
    }
}
