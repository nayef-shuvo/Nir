using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Calls into .NET.</summary>
internal sealed partial class BodyEmitter
{
    // The receiver comes first. A value-type receiver (int, double, bool) must be passed by address, so it goes through
    // a temporary; if the method is declared on a reference type (such as Object) the value is boxed instead.
    private void EmitMemberCall(BoundMemberCallExpression call)
    {
        EmitExpression(call.Receiver);

        var method = call.Target.Method;
        var receiverClrType = call.Receiver.Type.ClrType;
        var useCall = false;
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

        foreach (var argument in call.Arguments)
        {
            EmitExpression(argument);
        }

        il.Emit(useCall ? OpCodes.Call : OpCodes.Callvirt, method);
    }
}
