using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Nir.Compiler.Clr;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>.NET calls under <c>try</c> / <c>catch</c>: the generated helper methods.</summary>
internal sealed partial class BodyEmitter
{
    // The operands of the .NET call are worked out here, where an exception is not caught, and passed to a generated helper that
    // makes the call inside a CLR try block. The block cannot be entered with something on the stack, and here there may be, so
    // the try block lives in a method of its own.
    private void EmitProtectedCall(BoundProtectedCall protectedCall)
    {
        ClrMethodSymbol target;
        BoundExpression? receiver = null;
        ImmutableArray<BoundExpression> arguments;
        switch (protectedCall.Call)
        {
            case BoundCallExpression { Target: ClrMethodSymbol staticTarget } call:
                target = staticTarget;
                arguments = call.Arguments;
                break;
            case BoundMemberCallExpression member:
                target = member.Target;
                receiver = member.Receiver;
                arguments = member.Arguments;
                break;
            default:
                throw new UnreachableException();
        }

        if (receiver is not null)
        {
            EmitExpression(receiver);
        }

        foreach (var argument in arguments)
        {
            EmitExpression(argument);
        }

        il.Emit(OpCodes.Call, ProtectedHelper(target.Method, protectedCall.Result, receiver?.Type, [.. arguments.Select(a => a.Type)]));
    }

    private MethodBuilder ProtectedHelper(MethodInfo method, ResultTypeSymbol result, TypeSymbol? receiverType, TypeSymbol[] argumentTypes)
    {
        if (context.ProtectedHelpers.TryGetValue(method, out var existing))
        {
            return existing;
        }

        var layout = context.Unions[result];
        var parameters = (receiverType is null ? argumentTypes : [receiverType, .. argumentTypes]).Select(context.ClrOf).ToArray();
        var helper = context.Host.DefineMethod(
            GeneratedNames.ProtectedHelperPrefix + context.ProtectedHelpers.Count,
            MethodAttributes.Private | MethodAttributes.Static,
            layout.Type,
            parameters);
        context.ProtectedHelpers[method] = helper;

        var il = helper.GetILGenerator();
        var outcome = il.DeclareLocal(layout.Type);
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Initobj, layout.Type);

        il.BeginExceptionBlock();
        var useCall = false;
        if (receiverType is not null)
        {
            il.Emit(OpCodes.Ldarg_0);

            // The same receiver handling as an unprotected call (EmitMemberCall).
            var receiverClrType = receiverType.ClrType;
            if (receiverClrType.IsValueType)
            {
                if (method.DeclaringType is { IsValueType: true })
                {
                    var temporary = il.DeclareLocal(receiverClrType);
                    il.Emit(OpCodes.Stloc, temporary);
                    il.Emit(OpCodes.Ldloca, temporary);
                    useCall = true;
                }
                else
                {
                    il.Emit(OpCodes.Box, receiverClrType);
                }
            }
        }

        for (var i = receiverType is null ? 0 : 1; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, (short)i);
        }

        il.Emit(receiverType is null || useCall ? OpCodes.Call : OpCodes.Callvirt, method);
        if (result.Success is { } valueType)
        {
            var value = il.DeclareLocal(context.ClrOf(valueType));
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, outcome);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stfld, layout.Cases[ResultTypeSymbol.SuccessTag]);
        }

        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldc_I4, ResultTypeSymbol.SuccessTag);
        il.Emit(OpCodes.Stfld, layout.Tag);

        // Any exception becomes an anyerror carrying the exception's message.
        var exception = ClrTypes.Exception;
        il.BeginCatchBlock(exception);
        var message = il.DeclareLocal(ClrTypes.String);
        il.Emit(OpCodes.Callvirt, ClrTypes.ExceptionMessage);
        il.Emit(OpCodes.Stloc, message);
        il.Emit(OpCodes.Newobj, context.AnyErrorConstructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, message);
        il.Emit(OpCodes.Stfld, context.AnyErrorMessage);
        var error = il.DeclareLocal(context.AnyError);
        il.Emit(OpCodes.Stloc, error);
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldloc, error);
        il.Emit(OpCodes.Stfld, layout.Cases[0]);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, outcome);
        il.Emit(OpCodes.Ret);
        return helper;
    }
}
