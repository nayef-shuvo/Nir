using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Language;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary><c>typeof(x)</c> and the built-in struct <c>TypeInfo</c> it returns (design.md "Knowing a type").</summary>
internal sealed partial class ProgramBinder
{
    private StructTypeSymbol? _typeInfo;

    // The built-in struct `std::types::TypeInfo { name: string }`. It is made when something first needs it (a `typeof`, or the import),
    // so a program that never uses it does not carry it. It is not in `_structs` or `_structOrder`, which are for the structs the file
    // declares: the name `TypeInfo` reaches it only through `using std::types::TypeInfo` (`FindStruct`).
    private StructTypeSymbol TypeInfo()
    {
        if (_typeInfo is not null)
        {
            return _typeInfo;
        }

        var span = new TextSpan(0, 0);
        var nameField = new FieldDeclaration(
            new Identifier(ReservedNames.TypeInfoNameField, span),
            new TypeReference(new Identifier(TypeNames.StringType, span), BorrowKind.None, span),
            span);
        var declaration = new StructDeclaration(new Identifier(TypeNames.TypeInfoType, span), [nameField], IsError: false, span);

        _typeInfo = new StructTypeSymbol(declaration, GeneratedNames.TypeInfoClass);
        _typeInfo.Fields = [new FieldSymbol(ReservedNames.TypeInfoNameField, TypeSymbol.StringType, _typeInfo, nameField)];
        return _typeInfo;
    }

    // A struct by its name: the structs of this file, and the built-in ones the file has imported.
    private StructTypeSymbol? FindStruct(string name) =>
        _structs.GetValueOrDefault(name) ?? (_imports.GetValueOrDefault(name) is StdTypeName std ? std.Struct : null);

    // "unknown type 'X'", and for the one built-in type a file has to import, which import to add.
    private static string UnknownTypeMessage(string name) =>
        name == TypeNames.TypeInfoType
            ? $"unknown type '{name}'; import it with 'using {ImportRoot.Std}{Punctuation.PathSeparator}{ImportRoot.StdTypes}{Punctuation.PathSeparator}{name}'"
            : $"unknown type '{name}'";

    // `typeof(operand)` is worked out here, while compiling: the result is a `TypeInfo` holding a constant name, and the operand
    // is bound (so its mistakes are reported) but never run.
    private BoundExpression BindTypeof(TypeofExpression typeofExpression)
    {
        var type = TypeofOperandType(typeofExpression);
        if (type is null || type == TypeSymbol.ErrorType)
        {
            return new BoundErrorExpression();
        }

        var info = TypeInfo();
        return new BoundStructLiteral(info, [new BoundFieldInitializer(info.Fields[0], new BoundStringLiteral(type.Name))]);
    }

    // The declared type of the operand. A variable keeps its borrow: `typeof(p)` for `p: &Point` is `&Point`, and `typeof(self)`
    // is the borrow that `self` is. Null if the operand was wrong (already reported).
    private TypeSymbol? TypeofOperandType(TypeofExpression typeofExpression)
    {
        if (typeofExpression.Value is { } value)
        {
            return value switch
            {
                SelfExpression self => BindSelf(self).Type,
                NameExpression name => BindName(name).Type,
                _ => BindExpression(value).Type,
            };
        }

        return TypeofSyntaxType(typeofExpression.Type!);
    }

    // A type as written in `typeof(...)`. A plain name is a variable if there is one, otherwise a type, so `typeof(x)` and
    // `typeof((a, b))` work on variables too. Nothing else about the syntax changes: it is read as a type.
    private TypeSymbol? TypeofSyntaxType(TypeSyntax syntax)
    {
        switch (syntax)
        {
            case TypeReference { Borrow: BorrowKind.None } reference:
                if (_scopes.TryGet(reference.Name.Text, out var variable))
                {
                    return variable.Type;
                }

                if (LookupType(reference.Name.Text) is { } type)
                {
                    return type;
                }

                Error(DiagnosticCodes.UndefinedName, $"'{reference.Name.Text}' is not defined", reference.Name.Span);
                return null;

            case TupleTypeSyntax { Elements: var elements } when elements.All(element => element.Name is null):
                var types = elements.Select(element => TypeofSyntaxType(element.Type)).ToList();
                return types.Any(t => t is null || t == TypeSymbol.ErrorType)
                    ? null
                    : new TupleTypeSymbol([.. types.OfType<TypeSymbol>()], [.. types.Select(_ => (string?)null)]);

            default:
                return ResolveTypeSyntax(syntax);
        }
    }
}
