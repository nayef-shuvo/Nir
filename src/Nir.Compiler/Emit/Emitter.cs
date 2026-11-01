using System.Collections.Immutable;
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
public static partial class Emitter
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
        var type = module.DefineType(ReservedNames.ProgramClass, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var context = new EmitContext { Host = type };
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
}
