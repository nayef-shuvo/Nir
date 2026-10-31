using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Nir.Compiler.Clr;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>Turns a bound program into a runnable .NET assembly on disk.</summary>
public static class Emitter
{
    /// <summary>
    /// Writes <c>&lt;assemblyName&gt;.dll</c> and <c>&lt;assemblyName&gt;.runtimeconfig.json</c> to
    /// <paramref name="outputDirectory"/>, creating the directory and overwriting existing files.
    /// </summary>
    public static void Emit(BoundProgram program, string assemblyName, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var assemblyBuilder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), ClrUniverse.CoreAssembly);
        var module = assemblyBuilder.DefineDynamicModule(assemblyName);

        // Temporary naming: one class holding every function. Changes when modules arrive (slice 11).
        var type = module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var context = new EmitContext();
        DefineStructs(program, module, context);

        // Free functions are static methods of Program. A struct's methods live on the struct: an instance method (the receiver
        // is the CLR `this`, a managed pointer to the struct) when it has `&self` / `&mut self`, otherwise a static method.
        foreach (var function in program.Functions)
        {
            var symbol = function.Symbol;
            var host = symbol.Owner is { } owner ? context.Structs[owner] : type;
            var isInstance = symbol.Receiver != ReceiverKind.None;

            context.Methods[symbol] = host.DefineMethod(
                symbol.Name,
                MethodAttributes.Public | (isInstance ? MethodAttributes.HideBySig : MethodAttributes.Static),
                isInstance ? CallingConventions.HasThis : CallingConventions.Standard,
                context.ClrOf(symbol.ReturnType),
                [.. symbol.ParameterTypes.Select(context.ClrOf)]);
        }

        foreach (var function in program.Functions)
        {
            var il = context.Methods[function.Symbol].GetILGenerator();
            new BodyEmitter(il, context, function.Symbol).EmitBlock(function.Body);

            // A function that returns a value ends with its own `return` (the binder guarantees it).
            if (function.Symbol.ReturnType == TypeSymbol.UnitType)
            {
                il.Emit(OpCodes.Ret);
            }
        }

        type.CreateType();
        foreach (var structType in context.Structs.Values)
        {
            structType.CreateType();
        }

        var metadata = assemblyBuilder.GenerateMetadata(out var ilStream, out var fieldData);
        var peBuilder = new ManagedPEBuilder(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: MetadataTokens.MethodDefinitionHandle(context.Methods[program.EntryPoint].MetadataToken));

        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        using (var file = File.Create(Path.Combine(outputDirectory, assemblyName + ".dll")))
        {
            peImage.WriteContentTo(file);
        }

        RuntimeConfigWriter.Write(Path.Combine(outputDirectory, assemblyName + ".runtimeconfig.json"));
    }

    // Each struct is a public sealed CLR value type with public fields, so C# can see it.
    private static void DefineStructs(BoundProgram program, ModuleBuilder module, EmitContext context)
    {
        var valueType = ClrUniverse.CoreType("System.ValueType");
        foreach (var structSymbol in program.Structs)
        {
            context.Structs[structSymbol] = module.DefineType(
                structSymbol.Name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                valueType);
        }

        foreach (var structSymbol in program.Structs)
        {
            foreach (var field in structSymbol.Fields)
            {
                context.Fields[field] = context.Structs[structSymbol].DefineField(field.Name, context.ClrOf(field.Type), FieldAttributes.Public);
            }
        }
    }

    /// <summary>Emits the body of one function. Holds the state that belongs to a single function: its local variables.</summary>
    private sealed class BodyEmitter(ILGenerator il, EmitContext context, UserFunctionSymbol function)
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
                    EmitExpression(fieldAssignment.Value);
                    il.Emit(OpCodes.Stfld, context.Fields[fieldAssignment.Target.Field]);
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

                case BoundFieldAccess fieldAccess:
                    // Reading through the address avoids copying the whole struct just to look at one field.
                    if (BoundPlaces.IsPlace(fieldAccess.Receiver))
                    {
                        EmitAddress(fieldAccess.Receiver);
                    }
                    else
                    {
                        EmitExpression(fieldAccess.Receiver);
                    }

                    il.Emit(OpCodes.Ldfld, context.Fields[fieldAccess.Field]);
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
                    il.Emit(OpCodes.Ldflda, context.Fields[access.Field]);
                    break;

                default:
                    EmitExpression(place);
                    var temporary = il.DeclareLocal(context.ClrOf(place.Type));
                    il.Emit(OpCodes.Stloc, temporary);
                    il.Emit(OpCodes.Ldloca, temporary);
                    break;
            }
        }

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
        private void EmitFor(BoundForStatement statement)
        {
            var variable = il.DeclareLocal(TypeSymbol.IntType.ClrType);
            var limit = il.DeclareLocal(TypeSymbol.IntType.ClrType);
            _locals[statement.Variable] = variable;

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
            il.Emit(statement.IsInclusive ? OpCodes.Bgt : OpCodes.Bge, end);

            _loops.Push((Break: end, Continue: next));
            EmitBlock(statement.Body);
            _loops.Pop();

            il.MarkLabel(next);
            il.Emit(OpCodes.Ldloc, variable);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add_Ovf);
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
}
