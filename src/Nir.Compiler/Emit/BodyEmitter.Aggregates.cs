using System.Reflection;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Structs and tuples: literals, field access, destructuring.</summary>
internal sealed partial class BodyEmitter
{
    // Build in a temporary: zero it, store each field in the order written, then load the finished struct.
    private void EmitStructLiteral(BoundStructLiteral literal)
    {
        var clrType = context.ClrOf(literal.Struct);
        var temporary = il.DeclareLocal(clrType);

        il.Emit(OpCodes.Ldloca, temporary);
        il.Emit(OpCodes.Initobj, clrType);
        foreach (var initializer in literal.Initializers)
        {
            il.Emit(OpCodes.Ldloca, temporary);
            EmitExpression(initializer.Value);
            il.Emit(OpCodes.Stfld, context.Fields[initializer.Field]);
        }

        il.Emit(OpCodes.Ldloc, temporary);
    }

    // With the address of a tuple or struct on the stack: follow the fields to the last one and load its value.
    private void EmitFieldLoad(IReadOnlyList<FieldInfo> path)
    {
        foreach (var step in path.Take(path.Count - 1))
        {
            il.Emit(OpCodes.Ldflda, step);
        }

        il.Emit(OpCodes.Ldfld, path[^1]);
    }

    // The elements run left to right. A tuple longer than seven ends with the ValueTuple that holds the rest, built last.
    private void EmitTupleLiteral(IReadOnlyList<TypeSymbol> types, IReadOnlyList<BoundExpression> elements)
    {
        const int chunkSize = 7;
        foreach (var element in elements.Take(chunkSize))
        {
            EmitExpression(element);
        }

        if (elements.Count > chunkSize)
        {
            EmitTupleLiteral([.. types.Skip(chunkSize)], [.. elements.Skip(chunkSize)]);
        }

        il.Emit(OpCodes.Newobj, context.ChunkConstructor(types));
    }

    // The tuple is stored once, then each name reads its element from the stored copy.
    private void EmitDestructuring(BoundDestructuring destructuring)
    {
        EmitExpression(destructuring.Initializer);
        var tuple = il.DeclareLocal(context.ClrOf(destructuring.Tuple));
        il.Emit(OpCodes.Stloc, tuple);

        for (var i = 0; i < destructuring.Variables.Length; i++)
        {
            if (destructuring.Variables[i] is not { } variable)
            {
                continue;
            }

            il.Emit(OpCodes.Ldloca, tuple);
            EmitFieldLoad(context.FieldPath(destructuring.Tuple.Elements[i]));
            var local = il.DeclareLocal(context.ClrOf(variable.Type));
            _locals[variable] = local;
            il.Emit(OpCodes.Stloc, local);
        }
    }
}
