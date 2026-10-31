using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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

        var assemblyBuilder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
        var module = assemblyBuilder.DefineDynamicModule(assemblyName);

        // Temporary naming: one class holding every function. Changes when modules arrive (slice 10).
        var type = module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var methods = program.Functions.ToDictionary(
            function => function.Symbol,
            function => type.DefineMethod(
                function.Symbol.Name,
                MethodAttributes.Public | MethodAttributes.Static,
                function.Symbol.ReturnType.ClrType,
                [.. function.Symbol.ParameterTypes.Select(t => t.ClrType)]));

        foreach (var function in program.Functions)
        {
            var il = methods[function.Symbol].GetILGenerator();
            new BodyEmitter(il, methods, function.Symbol).EmitBlock(function.Body);

            // A function that returns a value ends with its own `return` (the binder guarantees it).
            if (function.Symbol.ReturnType == TypeSymbol.UnitType)
            {
                il.Emit(OpCodes.Ret);
            }
        }

        type.CreateType();

        var metadata = assemblyBuilder.GenerateMetadata(out var ilStream, out var fieldData);
        var peBuilder = new ManagedPEBuilder(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: MetadataTokens.MethodDefinitionHandle(methods[program.EntryPoint].MetadataToken));

        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        using (var file = File.Create(Path.Combine(outputDirectory, assemblyName + ".dll")))
        {
            peImage.WriteContentTo(file);
        }

        RuntimeConfigWriter.Write(Path.Combine(outputDirectory, assemblyName + ".runtimeconfig.json"));
    }

    /// <summary>Emits the body of one function. Holds the state that belongs to a single function: its local variables.</summary>
    private sealed class BodyEmitter(ILGenerator il, Dictionary<UserFunctionSymbol, MethodBuilder> methods, UserFunctionSymbol function)
    {
        private readonly Dictionary<VariableSymbol, LocalBuilder> _locals = [];

        // Parameters are never assigned, so they are read straight from their argument slot.
        private readonly Dictionary<VariableSymbol, short> _arguments =
            function.Parameters.Select((parameter, index) => (parameter, index)).ToDictionary(p => p.parameter, p => (short)p.index);

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

                case BoundVariableDeclaration declaration:
                    EmitExpression(declaration.Initializer);
                    var local = il.DeclareLocal(declaration.Variable.Type.ClrType);
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

                case BoundUnaryExpression unary:
                    EmitUnary(unary);
                    break;

                case BoundBinaryExpression binary:
                    EmitExpression(binary.Left);
                    EmitExpression(binary.Right);
                    EmitBinaryOperator(binary.Operator, isDouble: binary.Left.Type == TypeSymbol.DoubleType);
                    break;

                case BoundCallExpression call:
                    foreach (var argument in call.Arguments)
                    {
                        EmitExpression(argument);
                    }

                    switch (call.Target)
                    {
                        case BuiltinFunctionSymbol builtin:
                            il.Emit(OpCodes.Call, builtin.Method);
                            break;
                        case UserFunctionSymbol user:
                            il.Emit(OpCodes.Call, methods[user]);
                            break;
                        default:
                            throw new UnreachableException();
                    }

                    break;

                default:
                    throw new UnreachableException();
            }
        }

        // Negation of an int is 0 - x so that int.MinValue overflows (throws) like the other checked operations.
        private void EmitUnary(BoundUnaryExpression unary)
        {
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
