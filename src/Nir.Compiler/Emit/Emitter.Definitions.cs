using System.Reflection;
using System.Reflection.Emit;
using Nir.Compiler.Clr;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>The types and entry point the emitter defines before it writes any function body.</summary>
public static partial class Emitter
{
    // The class every error derives from, which is what `anyerror` is: an object with a message.
    private static void DefineAnyError(ModuleBuilder module, EmitContext context)
    {
        var anyError = module.DefineType(GeneratedNames.AnyErrorClass, TypeAttributes.Public | TypeAttributes.Class, ClrTypes.Object);
        context.AnyError = anyError;
        context.AnyErrorMessage = anyError.DefineField(GeneratedNames.AnyErrorMessage, ClrTypes.String, FieldAttributes.Public);
        context.AnyErrorConstructor = DefineConstructor(anyError, ClrTypes.ObjectConstructor);
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
        var main = program.DefineMethod(GeneratedNames.ErrorEntryPoint, MethodAttributes.Public | MethodAttributes.Static, ClrTypes.Int32, Type.EmptyTypes);
        var il = main.GetILGenerator();
        var outcome = il.DeclareLocal(layout.Type);
        var succeeded = il.DefineLabel();

        il.Emit(OpCodes.Call, userMain);
        il.Emit(OpCodes.Stloc, outcome);
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Tag);
        il.Emit(OpCodes.Brtrue, succeeded);

        il.Emit(OpCodes.Call, ClrTypes.ConsoleError);
        il.Emit(OpCodes.Ldstr, GeneratedNames.ErrorOutputPrefix);
        il.Emit(OpCodes.Ldloca, outcome);
        il.Emit(OpCodes.Ldfld, layout.Cases[0]);
        il.Emit(OpCodes.Ldfld, context.AnyErrorMessage);
        il.Emit(OpCodes.Call, StringMethods.Concat2);
        il.Emit(OpCodes.Callvirt, ClrTypes.TextWriterWriteLine);
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
        var valueType = ClrTypes.ValueType;
        foreach (var structSymbol in program.Structs)
        {
            if (structSymbol.IsError)
            {
                // An error is a class that derives from AnyError, so every error can be used as an anyerror.
                var errorClass = module.DefineType(structSymbol.ClrName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, context.AnyError);
                context.Structs[structSymbol] = errorClass;
                context.ErrorConstructors[structSymbol] = DefineConstructor(errorClass, context.AnyErrorConstructor);
                continue;
            }

            context.Structs[structSymbol] = module.DefineType(
                structSymbol.ClrName,
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
        var valueType = ClrTypes.ValueType;
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
            var tag = type.DefineField(GeneratedNames.TagField, ClrTypes.Int32, FieldAttributes.Public);
            var cases = union.Members
                .Select((member, i) => type.DefineField(GeneratedNames.CaseField(i), context.ClrOf(member), FieldAttributes.Public))
                .ToArray();
            context.Unions[union] = new UnionLayout(type, tag, cases);
        }
    }
}
