using System.Diagnostics;
using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Emits the body of one function. Holds the state that belongs to a single function: its local variables.</summary>
internal sealed partial class BodyEmitter(ILGenerator il, EmitContext context, UserFunctionSymbol function)
{
    private readonly Dictionary<VariableSymbol, LocalBuilder> _locals = [];

    // The jump targets of the loops we are inside, innermost on top.
    private readonly Stack<(Label Break, Label Continue)> _loops = new();

    // Parameters are never assigned, so they are read straight from their argument slot. In an instance method argument 0
    // is the CLR `this`, which is `self`, so the declared parameters start at 1.
    private readonly Dictionary<VariableSymbol, short> _arguments = ArgumentSlots(function);

    private static Dictionary<VariableSymbol, short> ArgumentSlots(UserFunctionSymbol function)
    {
        var slots = new Dictionary<VariableSymbol, short>();
        var first = 0;
        if (function.SelfVariable is { } self)
        {
            slots[self] = 0;
            first = 1;
        }

        for (var i = 0; i < function.Parameters.Length; i++)
        {
            slots[function.Parameters[i]] = (short)(first + i);
        }

        return slots;
    }

    public void EmitBlock(BoundBlock block)
    {
        foreach (var statement in block.Statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundExpressionStatement s:
                EmitExpression(s.Expression);
                if (s.Expression.Type != TypeSymbol.UnitType)
                {
                    il.Emit(OpCodes.Pop);
                }

                break;

            case BoundReturnStatement returnStatement:
                if (returnStatement.Value is { } value)
                {
                    EmitExpression(value);
                }

                il.Emit(OpCodes.Ret);
                break;

            case BoundIfStatement ifStatement:
                EmitIf(ifStatement);
                break;

            case BoundWhileStatement whileStatement:
                EmitWhile(whileStatement);
                break;

            case BoundForStatement forStatement:
                EmitFor(forStatement);
                break;

            case BoundBreakStatement:
                il.Emit(OpCodes.Br, _loops.Peek().Break);
                break;

            case BoundContinueStatement:
                il.Emit(OpCodes.Br, _loops.Peek().Continue);
                break;

            case BoundFieldAssignment fieldAssignment:
                // The address of the struct that holds the field, then the value, then store.
                EmitAddress(fieldAssignment.Target.Receiver);
                var path = context.FieldPath(fieldAssignment.Target.Field);
                foreach (var step in path.Take(path.Count - 1))
                {
                    il.Emit(OpCodes.Ldflda, step);
                }

                EmitExpression(fieldAssignment.Value);
                il.Emit(OpCodes.Stfld, path[^1]);
                break;

            case BoundDestructuring destructuring:
                EmitDestructuring(destructuring);
                break;

            case BoundVariableDeclaration declaration:
                EmitExpression(declaration.Initializer);
                var local = il.DeclareLocal(context.ClrOf(declaration.Variable.Type));
                _locals[declaration.Variable] = local;
                il.Emit(OpCodes.Stloc, local);
                break;

            case BoundAssignment assignment:
                EmitExpression(assignment.Value);
                il.Emit(OpCodes.Stloc, _locals[assignment.Variable]);
                break;

            default:
                throw new UnreachableException();
        }
    }
}
