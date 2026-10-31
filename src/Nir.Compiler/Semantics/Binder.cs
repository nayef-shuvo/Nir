using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Resolves names and checks types, turning the syntax tree into a bound tree.</summary>
public static class Binder
{
    /// <summary>Returns the bound program, or <c>null</c> if this call reported any error.</summary>
    public static BoundProgram? Bind(CompilationUnit unit, SourceText source, DiagnosticBag diagnostics) =>
        new ProgramBinder(source, diagnostics).Bind(unit);
}
