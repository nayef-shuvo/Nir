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

        // Temporary naming: one class holding every function. Changes when modules arrive (slice 9).
        var type = module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var methods = program.Functions.ToDictionary(
            function => function.Symbol,
            function => type.DefineMethod(
                function.Symbol.Name,
                MethodAttributes.Public | MethodAttributes.Static,
                function.Symbol.ReturnType.ClrType,
                Type.EmptyTypes));

        foreach (var function in program.Functions)
        {
            var il = methods[function.Symbol].GetILGenerator();
            EmitBlock(il, function.Body, methods);
            il.Emit(OpCodes.Ret);
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

    private static void EmitBlock(ILGenerator il, BoundBlock block, Dictionary<UserFunctionSymbol, MethodBuilder> methods)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case BoundExpressionStatement s:
                    EmitExpression(il, s.Expression, methods);
                    if (s.Expression.Type != TypeSymbol.UnitType)
                    {
                        il.Emit(OpCodes.Pop);
                    }

                    break;
                default:
                    throw new UnreachableException();
            }
        }
    }

    private static void EmitExpression(ILGenerator il, BoundExpression expression, Dictionary<UserFunctionSymbol, MethodBuilder> methods)
    {
        switch (expression)
        {
            case BoundStringLiteral s:
                il.Emit(OpCodes.Ldstr, s.Value);
                break;

            case BoundCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    EmitExpression(il, argument, methods);
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
}
