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
        DefineAnyError(module, context);
        DefineStructTypes(program, module, context);
        DefineUnionTypes(program, module, context);
        DefineStructFields(program, context);
        DefineUnionFields(program, context);

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
            else if (function.Symbol.ReturnType is ResultTypeSymbol { Success: null } unitResult)
            {
                // An `E!` function whose body ends without a `return` succeeds.
                new BodyEmitter(il, context, function.Symbol).EmitUnitSuccess(unitResult);
                il.Emit(OpCodes.Ret);
            }
        }

        var entry = program.EntryPoint.ReturnType is ResultTypeSymbol entryResult
            ? DefineErrorEntry(type, context, context.Methods[program.EntryPoint], entryResult)
            : context.Methods[program.EntryPoint];

        type.CreateType();
        context.AnyError.CreateType(); // before the error types that derive from it
        foreach (var structType in context.Structs.Values)
        {
            structType.CreateType();
        }

        foreach (var union in context.Unions.Values)
        {
            union.Type.CreateType();
        }

        var metadata = assemblyBuilder.GenerateMetadata(out var ilStream, out var fieldData);
        var peBuilder = new ManagedPEBuilder(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: MetadataTokens.MethodDefinitionHandle(entry.MetadataToken));

        var peImage = new BlobBuilder();
        peBuilder.Serialize(peImage);
        using (var file = File.Create(Path.Combine(outputDirectory, assemblyName + ".dll")))
        {
            peImage.WriteContentTo(file);
        }

        RuntimeConfigWriter.Write(Path.Combine(outputDirectory, assemblyName + ".runtimeconfig.json"));
    }

    // The class every error derives from, which is what `anyerror` is: an object with a message.
    private static void DefineAnyError(ModuleBuilder module, EmitContext context)
    {
        var anyError = module.DefineType("Nir.AnyError", TypeAttributes.Public | TypeAttributes.Class, ClrUniverse.CoreType("System.Object"));
        context.AnyError = anyError;
        context.AnyErrorMessage = anyError.DefineField("Message", ClrUniverse.CoreType("System.String"), FieldAttributes.Public);
        context.AnyErrorConstructor = DefineConstructor(anyError, ClrUniverse.CoreType("System.Object").GetConstructor(Type.EmptyTypes)!);
    }

    // A public constructor that only calls the base constructor.
    private static ConstructorBuilder DefineConstructor(TypeBuilder type, ConstructorInfo baseConstructor)
    {
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);
        return constructor;
    }

    // When `main` returns an `E!`, the real entry point is a `Main` that runs it, prints `error: <message>` to stderr if it failed
    // and exits with 1.
    private static MethodBuilder DefineErrorEntry(TypeBuilder program, EmitContext context, MethodInfo userMain, ResultTypeSymbol result)
    {
        var layout = context.Unions[result];
        var text = ClrUniverse.CoreType("System.String");
        var main = program.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, ClrUniverse.CoreType("System.Int32"), Type.EmptyTypes);
        var il = main.GetILGenerator();
        var outcome = il.DeclareLocal(layout.Type);
        var succeeded = il.DefineLabel();

        il.Emit(OpCodes.Call, userMain);
        il.Emit(OpCodes.Stloc, outcome);
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Tag);
        il.Emit(OpCodes.Brtrue, succeeded);

        il.Emit(OpCodes.Call, ClrUniverse.FindType("System.Console")!.GetProperty("Error")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "error: ");
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Cases[0]);
        il.Emit(OpCodes.Ldfld, context.AnyErrorMessage);
        il.Emit(OpCodes.Call, text.GetMethod("Concat", [text, text])!);
        il.Emit(OpCodes.Callvirt, ClrUniverse.FindType("System.IO.TextWriter")!.GetMethod("WriteLine", [text])!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(succeeded);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return main;
    }

    // Each struct is a public sealed CLR value type with public fields, so C# can see it. All the types are defined before any
    // field, because a field may be of a struct, union or tuple that is declared further down.
    private static void DefineStructTypes(BoundProgram program, ModuleBuilder module, EmitContext context)
    {
        var valueType = ClrUniverse.CoreType("System.ValueType");
        foreach (var structSymbol in program.Structs)
        {
            if (structSymbol.IsError)
            {
                // An error is a class that derives from AnyError, so every error can be used as an anyerror.
                var errorClass = module.DefineType(structSymbol.Name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, context.AnyError);
                context.Structs[structSymbol] = errorClass;
                context.ErrorConstructors[structSymbol] = DefineConstructor(errorClass, context.AnyErrorConstructor);
                continue;
            }

            context.Structs[structSymbol] = module.DefineType(
                structSymbol.Name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                valueType);
        }
    }

    private static void DefineStructFields(BoundProgram program, EmitContext context)
    {
        foreach (var structSymbol in program.Structs)
        {
            foreach (var field in structSymbol.Fields)
            {
                context.Fields[field] = context.Structs[structSymbol].DefineField(field.Name, context.ClrOf(field.Type), FieldAttributes.Public);
            }
        }
    }

    // A union is a tagged value type: an `int` saying which member it holds, and a field for each member. Nothing is boxed.
    private static void DefineUnionTypes(BoundProgram program, ModuleBuilder module, EmitContext context)
    {
        var valueType = ClrUniverse.CoreType("System.ValueType");
        foreach (var union in program.Unions)
        {
            var type = module.DefineType(
                union.Name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                valueType);
            context.Unions[union] = new UnionLayout(type, null!, null!);
        }
    }

    private static void DefineUnionFields(BoundProgram program, EmitContext context)
    {
        foreach (var union in program.Unions)
        {
            var type = context.Unions[union].Type;
            var tag = type.DefineField("Tag", ClrUniverse.CoreType("System.Int32"), FieldAttributes.Public);
            var cases = union.Members
                .Select((member, i) => type.DefineField("Case" + i, context.ClrOf(member), FieldAttributes.Public))
                .ToArray();
            context.Unions[union] = new UnionLayout(type, tag, cases);
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

        // Build in a temporary like a struct literal: zero it, store the tag and the value, load the finished union.
        private void EmitUnionWrap(BoundUnionWrap wrap)
        {
            var layout = context.Unions[wrap.Union];
            var temporary = il.DeclareLocal(layout.Type);

            il.Emit(OpCodes.Ldloca, temporary);
            il.Emit(OpCodes.Initobj, layout.Type);
            il.Emit(OpCodes.Ldloca, temporary);
            il.Emit(OpCodes.Ldc_I4, wrap.Index);
            il.Emit(OpCodes.Stfld, layout.Tag);
            il.Emit(OpCodes.Ldloca, temporary);
            EmitExpression(wrap.Value);
            il.Emit(OpCodes.Stfld, layout.Cases[wrap.Index]);
            il.Emit(OpCodes.Ldloc, temporary);
        }

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
            if (literal.Error.FindField("message") is { } message && message.Type == TypeSymbol.StringType)
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
