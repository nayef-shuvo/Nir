using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>The <c>using</c> imports and the names they introduce.</summary>
internal sealed partial class ProgramBinder
{
    // `using clr::cs::System::IO` and `using clr::cs::System::{Math, Console}` introduce local names.
    private void BindImports(ImmutableArray<UsingDirective> usings)
    {
        foreach (var directive in usings)
        {
            var path = directive.Path;
            if (path.Length < 2 || path[0].Text != ImportRoot.Clr || path[1].Text != ImportRoot.CSharp)
            {
                Error(DiagnosticCodes.InvalidImport, $"imports must start with '{ImportRoot.Prefix}'", path[0].Span);
                continue;
            }

            if (directive.Members.IsEmpty)
            {
                Import(path);
                continue;
            }

            foreach (var member in directive.Members)
            {
                Import([.. path, member]);
            }
        }
    }

    private void Import(ImmutableArray<Identifier> path)
    {
        var alias = path[^1];
        if (path.Length < 3)
        {
            Error(DiagnosticCodes.InvalidImport, $"'{ImportRoot.Prefix}' alone names nothing; add a namespace or type after it", path[0].Span);
            return;
        }

        var fullName = string.Join('.', path.Skip(2).Select(part => part.Text));
        ImportedName imported;
        if (ClrUniverse.FindType(fullName) is { } type)
        {
            imported = new TypeName(type);
        }
        else if (ClrUniverse.NamespaceExists(fullName))
        {
            imported = new NamespaceName(fullName);
        }
        else
        {
            Error(DiagnosticCodes.ImportNotFound, $"'{fullName}' was not found", alias.Span);
            return;
        }

        if (!_imports.TryAdd(alias.Text, imported))
        {
            Error(DiagnosticCodes.DuplicateImport, $"'{alias.Text}' is already imported", alias.Span);
        }
    }

    /// <summary>What a <c>using</c> introduced: a .NET namespace or a .NET type.</summary>
    private abstract record ImportedName(string FullName);

    private sealed record NamespaceName(string FullName) : ImportedName(FullName);

    private sealed record TypeName(Type Type) : ImportedName(Type.FullName ?? Type.Name);
}
