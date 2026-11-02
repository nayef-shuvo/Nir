using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Types as written (named, tuple), tuple aliases, tuple literals and taking tuples apart.</summary>
internal sealed partial class ProgramBinder
{
    private readonly Dictionary<string, TupleAliasDeclaration> _aliasDeclarations = [];
    private readonly Dictionary<string, TypeSymbol> _aliases = [];
    private readonly HashSet<string> _aliasesBeingResolved = [];

    // Pass 0b: `type Pair = (x: int, y: int)`. Names first, then the types, because an alias may use another alias or any struct.
    private void DeclareTupleAliases(ImmutableArray<TupleAliasDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (TypeNameProblem(declaration.Name.Text) is { } problem)
            {
                Error(DiagnosticCodes.DuplicateType, problem, declaration.Name.Span);
                continue;
            }

            _aliasDeclarations[declaration.Name.Text] = declaration;
        }

        foreach (var name in _aliasDeclarations.Keys.ToList())
        {
            ResolveAlias(name);
        }
    }

    // The alias's type, worked out on first use so aliases may appear in any order.
    private TypeSymbol ResolveAlias(string name)
    {
        if (_aliases.TryGetValue(name, out var resolved))
        {
            return resolved;
        }

        var declaration = _aliasDeclarations[name];
        if (!_aliasesBeingResolved.Add(name))
        {
            Error(DiagnosticCodes.RecursiveAlias, $"type '{name}' is defined in terms of itself", declaration.Name.Span);
            return TypeSymbol.ErrorType;
        }

        var type = ResolveTypeSyntax(declaration.Type);
        _aliasesBeingResolved.Remove(name);
        _aliases[name] = type;
        return type;
    }

    // A type as written: a name, or a tuple of types. Reports what is wrong and returns the error type.
    private TypeSymbol ResolveTypeSyntax(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case TypeReference reference:
                if (LookupType(reference.Name.Text) is { } type)
                {
                    return type;
                }

                Error(DiagnosticCodes.UnknownType, UnknownTypeMessage(reference.Name.Text), reference.Name.Span);
                return TypeSymbol.ErrorType;

            case TupleTypeSyntax tuple:
                return ResolveTupleType(tuple);

            case ResultTypeSyntax result:
                return ResolveResultType(result);

            default:
                throw new System.Diagnostics.UnreachableException();
        }
    }

    private TypeSymbol ResolveTupleType(TupleTypeSyntax syntax)
    {
        var types = syntax.Elements.Select(element => ResolveTypeSyntax(element.Type)).ToImmutableArray();
        var failed = types.Any(type => type == TypeSymbol.ErrorType);

        var named = syntax.Elements.Count(element => element.Name is not null);
        if (named != 0 && named != syntax.Elements.Length)
        {
            Error(DiagnosticCodes.InconsistentTupleNames, "either every element of a tuple type has a name or none does", syntax.Span);
            failed = true;
        }

        var seen = new HashSet<string>();
        foreach (var element in syntax.Elements.Where(element => element.Name is not null))
        {
            if (!seen.Add(element.Name!.Text))
            {
                Error(DiagnosticCodes.DuplicateField, $"element '{element.Name.Text}' is already declared", element.Name.Span);
                failed = true;
            }
        }

        return failed ? TypeSymbol.ErrorType : new TupleTypeSymbol(types, [.. syntax.Elements.Select(element => element.Name?.Text)]);
    }

    // `(a, b, c)`: the elements run left to right. Its type is the tuple of their types.
    private BoundExpression BindTupleLiteral(TupleLiteralExpression literal)
    {
        var elements = literal.Elements.Select(BindExpression).ToImmutableArray();
        var failed = elements.Any(element => element.Type == TypeSymbol.ErrorType);

        foreach (var (element, syntax) in elements.Zip(literal.Elements).Where(pair => pair.First.Type == TypeSymbol.UnitType))
        {
            Error(DiagnosticCodes.UnitVariable, $"cannot use a value of type '{TypeNames.UnitType}' as a tuple element", syntax.Span);
            failed = true;
        }

        if (failed)
        {
            return new BoundErrorExpression();
        }

        var type = new TupleTypeSymbol([.. elements.Select(element => element.Type)], [.. elements.Select(_ => (string?)null)]);
        return new BoundTupleLiteral(type, elements);
    }

    // `let (a, mut b) = pair`: the right side is evaluated once, then each name takes one element.
    private BoundStatement BindDestructuring(DestructuringDeclaration declaration)
    {
        var initializer = BindStatementValue(declaration.Initializer);

        TupleTypeSymbol? tuple = null;
        if (initializer.Type == TypeSymbol.ErrorType)
        {
            // Already reported.
        }
        else if (initializer.Type is not TupleTypeSymbol found)
        {
            Error(
                DiagnosticCodes.TypeMismatch,
                $"cannot take apart a value of type '{initializer.Type}'; only a tuple can be",
                declaration.Initializer.Span);
        }
        else if (found.Elements.Length != declaration.Names.Length)
        {
            Error(
                DiagnosticCodes.TypeMismatch,
                $"cannot take apart '{found}' into {declaration.Names.Length} names; it has {found.Elements.Length} elements",
                declaration.Span);
        }
        else
        {
            tuple = found;
        }

        var variables = ImmutableArray.CreateBuilder<VariableSymbol?>();
        for (var i = 0; i < declaration.Names.Length; i++)
        {
            var name = declaration.Names[i];
            if (name.Name.Text == ReservedNames.Discard)
            {
                variables.Add(null); // `_` takes the element and drops it
                continue;
            }

            var variable = new VariableSymbol(name.Name.Text, tuple?.Elements[i].Type ?? TypeSymbol.ErrorType, name.IsMutable);
            Declare(variable, name.Name);
            variables.Add(variable);
        }

        return tuple is null ? new BoundExpressionStatement(initializer) : new BoundDestructuring(variables.ToImmutable(), initializer, tuple);
    }
}
