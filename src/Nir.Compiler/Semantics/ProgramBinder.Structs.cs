using System.Collections.Immutable;
using System.Diagnostics;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Structs: declarations and fields, struct literals, field reads and field writes.</summary>
internal sealed partial class ProgramBinder
{
    private readonly Dictionary<string, StructTypeSymbol> _structs = [];
    private readonly List<StructTypeSymbol> _structOrder = [];

    // Pass 1a: the names. Fields come later because a field may use a struct that is declared further down.
    private void DeclareStructs(ImmutableArray<StructDeclaration> declarations)
    {
        foreach (var declaration in declarations)
        {
            var name = declaration.Name.Text;
            var problem = TypeNameProblem(name);

            if (problem is not null)
            {
                Error(DiagnosticCodes.DuplicateType, problem, declaration.Name.Span);
                continue;
            }

            var symbol = new StructTypeSymbol(declaration);
            _structs[name] = symbol;
            _structOrder.Add(symbol);
        }
    }

    // Why a new type may not have this name, or null if it may.
    private string? TypeNameProblem(string name)
    {
        if (LookupPrimitive(name) is not null)
        {
            return $"'{name}' is a built-in type and cannot be redefined";
        }

        if (name == ReservedNames.ProgramClass)
        {
            return $"'{ReservedNames.ProgramClass}' is reserved for the functions of the compiled program";
        }

        if (_structs.ContainsKey(name) || _aliasDeclarations.ContainsKey(name) || _unionDeclarations.ContainsKey(name))
        {
            return $"type '{name}' is already defined";
        }

        return _imports.ContainsKey(name) ? $"'{name}' is already imported" : null;
    }

    // Pass 1b: the fields, then a check that no struct contains itself.
    private void ResolveStructFields()
    {
        foreach (var symbol in _structOrder)
        {
            var fields = new List<FieldSymbol>();
            var seen = new HashSet<string>();
            foreach (var declaration in symbol.Declaration.Fields)
            {
                if (!seen.Add(declaration.Name.Text))
                {
                    Error(DiagnosticCodes.DuplicateField, $"field '{declaration.Name.Text}' is already declared", declaration.Name.Span);
                    continue;
                }

                var type = ResolveTypeSyntax(declaration.Type);
                fields.Add(new FieldSymbol(declaration.Name.Text, type, symbol, declaration));
            }

            symbol.Fields = [.. fields];
        }

        var reported = new HashSet<StructTypeSymbol>();
        foreach (var symbol in _structOrder.Where(s => !reported.Contains(s)))
        {
            if (FindCycle(symbol, symbol, [symbol]) is not { } cycle)
            {
                continue;
            }

            // One cycle gets one diagnostic, at the field of its first struct that starts it.
            var first = cycle[0];
            Error(DiagnosticCodes.RecursiveStruct, $"struct '{symbol.Name}' contains itself, so it would have no size", first.Declaration!.Name.Span);
            reported.Add(symbol);
            foreach (var field in cycle)
            {
                if (field.Owner is StructTypeSymbol member)
                {
                    reported.Add(member);
                }
            }
        }
    }

    // The fields, in order, that lead from `current` back to `start` by value; null if they do not.
    private static List<FieldSymbol>? FindCycle(StructTypeSymbol start, StructTypeSymbol current, HashSet<StructTypeSymbol> visited)
    {
        foreach (var field in current.Fields)
        {
            foreach (var next in StructsInside(field.Type))
            {
                if (next == start)
                {
                    return [field];
                }

                if (visited.Add(next) && FindCycle(start, next, visited) is { } rest)
                {
                    rest.Insert(0, field);
                    return rest;
                }
            }
        }

        return null;
    }

    // The structs a value of this type holds directly: itself, or the ones inside a tuple.
    private static IEnumerable<StructTypeSymbol> StructsInside(TypeSymbol type) => type switch
    {
        StructTypeSymbol { IsError: true } => [], // an error is a class: it holds other values by reference, not inside itself
        StructTypeSymbol structType => [structType],
        TupleTypeSymbol tuple => tuple.Elements.SelectMany(element => StructsInside(element.Type)),
        UnionTypeSymbol union => union.Members.SelectMany(StructsInside),
        _ => [],
    };

    // A name used as a struct (in a literal): it must be a struct declared in this file.
    private StructTypeSymbol? ResolveStructName(Identifier name)
    {
        if (StructNamed(name) is { } symbol)
        {
            return symbol;
        }

        if (LookupPrimitive(name.Text) is not null || _aliasDeclarations.ContainsKey(name.Text) || _unionDeclarations.ContainsKey(name.Text))
        {
            Error(DiagnosticCodes.NotAStruct, $"'{name.Text}' is not a struct", name.Span);
        }
        else
        {
            Error(DiagnosticCodes.UnknownType, $"unknown type '{name.Text}'", name.Span);
        }

        return null;
    }

    // `Point { x = 1, y = 2 }`: every field exactly once, in any order. The values run in the order written.
    private BoundExpression BindStructLiteral(StructLiteralExpression literal)
    {
        var structType = ResolveStructName(literal.TypeName);
        if (structType is { IsError: true })
        {
            Error(DiagnosticCodes.ErrorLiteralForm, $"'{structType.Name}' is an error type; make one with 'error {structType.Name} {{ ... }}'", literal.TypeName.Span);
            structType = null;
        }

        var initializers = BindInitializers(structType, literal.TypeName, literal.Fields);
        return structType is null || initializers is null ? new BoundErrorExpression() : new BoundStructLiteral(structType, initializers.Value);
    }

    // The values of a struct or error literal, checked against the fields of the type. Every value is bound even when the type is
    // bad, so mistakes inside them are reported too. Null if anything was wrong (already reported).
    private ImmutableArray<BoundFieldInitializer>? BindInitializers(StructTypeSymbol? structType, Identifier typeName, ImmutableArray<FieldInitializer> fields)
    {
        var values = fields.Select(field => (Field: field, Value: BindExpression(field.Value))).ToList();
        if (structType is null)
        {
            return null;
        }

        var failed = false;
        var given = new HashSet<string>();
        var initializers = ImmutableArray.CreateBuilder<BoundFieldInitializer>();
        foreach (var (initializer, value) in values)
        {
            var field = structType.FindField(initializer.Name.Text);
            if (field is null)
            {
                Error(DiagnosticCodes.NoSuchMember, $"'{initializer.Name.Text}' is not a member of '{structType.Name}'", initializer.Name.Span);
                failed = true;
            }
            else if (!given.Add(field.Name))
            {
                Error(DiagnosticCodes.DuplicateField, $"field '{field.Name}' is given twice", initializer.Name.Span);
                failed = true;
            }
            else if (value.Type == TypeSymbol.ErrorType)
            {
                failed = true;
            }
            else if ((field.Type == TypeSymbol.ErrorType ? value : Convert(value, field.Type)) is { } converted)
            {
                initializers.Add(new BoundFieldInitializer(field, converted));
            }
            else
            {
                Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{field.Type}'", initializer.Value.Span);
                failed = true;
            }
        }

        var missing = structType.Fields.Where(field => !given.Contains(field.Name)).Select(field => $"'{field.Name}'").ToList();
        if (missing.Count > 0)
        {
            var noun = missing.Count == 1 ? "field" : "fields";
            Error(DiagnosticCodes.MissingField, $"missing {noun} {string.Join(", ", missing)} in '{structType.Name}'", typeName.Span);
            failed = true;
        }

        return failed ? null : initializers.ToImmutable();
    }

    // `receiver.field`: the receiver must be a struct value.
    private BoundExpression BindFieldAccess(FieldAccessExpression access)
    {
        var receiver = BindPlace(access.Receiver);
        if (receiver.Type == TypeSymbol.ErrorType)
        {
            return receiver;
        }

        // A borrow of a struct is read through, so `self.x` works without writing a dereference.
        // A tuple element is reached the same way, by index (`t.0`) or, if it has one, by name.
        var owner = StructOf(receiver.Type);
        var field = receiver.Type switch
        {
            TupleTypeSymbol tuple => tuple.FindElement(access.Field.Text),
            AnyErrorTypeSymbol any => access.Field.Text == ReservedNames.MessageField ? any.Message : null,
            _ => owner?.FindField(access.Field.Text),
        };
        if (field is null)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{access.Field.Text}' is not a member of '{owner?.Name ?? receiver.Type.Name}'", access.Field.Span);
            return new BoundErrorExpression();
        }

        return new BoundFieldAccess(receiver, field);
    }

    // `p.x = value`, `line.to.x = value`: the path must start at a variable and that variable must be mutable.
    private BoundStatement BindFieldAssignment(AssignmentStatement assignment, BoundExpression value)
    {
        var target = BindExpression(assignment.Target);
        if (target is not BoundFieldAccess access)
        {
            return new BoundExpressionStatement(value); // the target already had an error
        }

        var text = PathText(assignment.Target);
        for (BoundExpression step = access; step is BoundFieldAccess fieldStep; step = fieldStep.Receiver)
        {
            if (fieldStep.Receiver.Type is StructTypeSymbol { IsError: true } or AnyErrorTypeSymbol)
            {
                Error(DiagnosticCodes.NotMutable, $"cannot assign to '{text}' because error values cannot be changed", assignment.Target.Span);
                return new BoundExpressionStatement(value);
            }
        }

        var root = BoundPlaces.Root(access);
        if (!BoundPlaces.IsMutable(root))
        {
            Error(DiagnosticCodes.NotMutable, NotMutableMessage($"assign to '{text}'", root), assignment.Target.Span);
        }

        if (value.Type != TypeSymbol.ErrorType && access.Type != TypeSymbol.ErrorType)
        {
            if (Convert(value, access.Type) is { } converted)
            {
                value = converted;
            }
            else
            {
                Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{access.Type}'", assignment.Value.Span);
            }
        }

        return new BoundFieldAssignment(access, value);
    }

    // `a.b.c` as written, for messages.
    private static string PathText(Expression path) => path switch
    {
        NameExpression name => name.Name.Text,
        SelfExpression => ReservedNames.SelfValue,
        FieldAccessExpression access => PathText(access.Receiver) + "." + access.Field.Text,
        _ => throw new UnreachableException(),
    };
}
