using System.Reflection.Emit;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Control flow: if, while, for, the short-circuit operators.</summary>
internal sealed partial class BodyEmitter
{
    // A block that always returns needs no jump over the else part, and when neither block falls through no end label is
    // needed at all (which keeps a function that ends in such an if/else valid IL: no label points past its last instruction).
    private void EmitIf(BoundIfStatement statement)
    {
        var elseLabel = il.DefineLabel();
        EmitExpression(statement.Condition);
        il.Emit(OpCodes.Brfalse, elseLabel);
        EmitBlock(statement.Then);

        if (statement.ElseBlock is not { } elseBlock)
        {
            il.MarkLabel(elseLabel);
            return;
        }

        var thenFallsThrough = !BoundFlow.AlwaysReturns(statement.Then);
        var endLabel = il.DefineLabel();
        if (thenFallsThrough)
        {
            il.Emit(OpCodes.Br, endLabel);
        }

        il.MarkLabel(elseLabel);
        EmitBlock(elseBlock);
        if (thenFallsThrough)
        {
            il.MarkLabel(endLabel);
        }
    }

    private void EmitWhile(BoundWhileStatement statement)
    {
        var top = il.DefineLabel();
        var end = il.DefineLabel();

        il.MarkLabel(top);
        EmitExpression(statement.Condition);
        il.Emit(OpCodes.Brfalse, end);

        _loops.Push((Break: end, Continue: top));
        EmitBlock(statement.Body);
        _loops.Pop();

        il.Emit(OpCodes.Br, top);
        il.MarkLabel(end);
    }

    // The loop variable is immutable, so it can serve as the counter itself. The end is evaluated once, into a hidden local.
    // `a..b` stops before the end, so the counter never goes past it. `a..=b` runs the body first and stops when the counter is
    // the end, rather than adding one more, so it also works when the end is the largest value of the type.
    private void EmitFor(BoundForStatement statement)
    {
        var type = statement.Variable.Type;
        var variable = il.DeclareLocal(type.ClrType);
        var limit = il.DeclareLocal(type.ClrType);
        _locals[statement.Variable] = variable;
        var unsigned = NumberTypes.IsUnsignedOnStack(type);

        EmitExpression(statement.Start);
        il.Emit(OpCodes.Stloc, variable);
        EmitExpression(statement.End);
        il.Emit(OpCodes.Stloc, limit);

        var top = il.DefineLabel();
        var next = il.DefineLabel();
        var end = il.DefineLabel();

        il.MarkLabel(top);
        il.Emit(OpCodes.Ldloc, variable);
        il.Emit(OpCodes.Ldloc, limit);
        il.Emit(statement.IsInclusive ? (unsigned ? OpCodes.Bgt_Un : OpCodes.Bgt) : (unsigned ? OpCodes.Bge_Un : OpCodes.Bge), end);

        _loops.Push((Break: end, Continue: next));
        EmitBlock(statement.Body);
        _loops.Pop();

        il.MarkLabel(next);
        if (statement.IsInclusive)
        {
            il.Emit(OpCodes.Ldloc, variable);
            il.Emit(OpCodes.Ldloc, limit);
            il.Emit(OpCodes.Beq, end);
        }

        il.Emit(OpCodes.Ldloc, variable);
        if (NumberTypes.Bits(type) == 64)
        {
            il.Emit(OpCodes.Ldc_I8, 1L);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_1);
        }

        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, variable);
        il.Emit(OpCodes.Br, top);
        il.MarkLabel(end);
    }

    // `a && b` and `a || b` do not evaluate b when a already decides the result.
    private void EmitShortCircuit(BoundBinaryExpression expression)
    {
        var isAnd = expression.Operator == BoundBinaryOperator.And;
        var decided = il.DefineLabel();
        var end = il.DefineLabel();

        EmitExpression(expression.Left);
        il.Emit(isAnd ? OpCodes.Brfalse : OpCodes.Brtrue, decided);
        EmitExpression(expression.Right);
        il.Emit(OpCodes.Br, end);

        il.MarkLabel(decided);
        il.Emit(isAnd ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
        il.MarkLabel(end);
    }
}
