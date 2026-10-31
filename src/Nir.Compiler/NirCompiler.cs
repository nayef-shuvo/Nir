using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Emit;
using Nir.Compiler.Parsing;
using Nir.Compiler.Semantics;

namespace Nir.Compiler;

/// <summary>The outcome of compiling one source file.</summary>
public sealed record CompilationResult(BoundProgram? Program, DiagnosticBag Diagnostics)
{
    /// <summary>True when there is a program to emit and no errors were reported.</summary>
    public bool Succeeded => Program is not null && !Diagnostics.HasErrors;
}

/// <summary>The public entry point of the compiler. The CLI and tests use only this and the Diagnostics types.</summary>
public static class NirCompiler
{
    /// <summary>Parses and binds <paramref name="source"/>. Stops after the first phase that reports an error.</summary>
    public static CompilationResult Compile(SourceText source)
    {
        lock (ClrUniverse.Gate)
        {
            var diagnostics = new DiagnosticBag();

            var unit = SyntaxParser.Parse(source, diagnostics);
            if (diagnostics.HasErrors)
            {
                return new CompilationResult(null, diagnostics);
            }

            var program = Binder.Bind(unit, source, diagnostics);
            return new CompilationResult(program, diagnostics);
        }
    }

    /// <summary>Writes the assembly and its runtimeconfig. <paramref name="result"/> must have succeeded.</summary>
    public static void Emit(CompilationResult result, string assemblyName, string outputDirectory)
    {
        if (!result.Succeeded || result.Program is not { } program)
        {
            throw new InvalidOperationException("Cannot emit a compilation that has errors.");
        }

        lock (ClrUniverse.Gate)
        {
            Emitter.Emit(program, assemblyName, outputDirectory);
        }
    }
}
