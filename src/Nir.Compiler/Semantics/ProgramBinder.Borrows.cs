using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Borrows: <c>&amp;p</c> and <c>&amp;mut p</c> written at a call, and the parameters that receive them.</summary>
internal sealed partial class ProgramBinder
{
    // A parameter type: a plain type, or a borrow (`&Point`, `&mut Point`), which only a struct can be.
    private TypeSymbol ResolveParameterType(TypeSyntax syntax)
    {
        if (syntax is not TypeReference { Borrow: not BorrowKind.None } reference)
        {
            return ResolveTypeSyntax(syntax);
        }

        if (StructNamed(reference.Name) is { IsError: false } owner)
        {
            return owner.BorrowOf(reference.Borrow == BorrowKind.Mutable);
        }

        if (LookupType(reference.Name.Text) is null)
        {
            Error(DiagnosticCodes.UnknownType, UnknownTypeMessage(reference.Name.Text), reference.Name.Span);
        }
        else
        {
            Error(
                DiagnosticCodes.BorrowOfNonStruct,
                $"cannot borrow '{reference.Name.Text}': only structs can be borrowed for now, so '{reference.Text}' is not supported yet",
                reference.Span);
        }

        return TypeSymbol.ErrorType;
    }

    // An argument of a call of a function or method written in Nir. Unlike other expressions it may be `&x` or `&mut x`.
    private BoundExpression BindArgument(Expression argument) =>
        argument is ReferenceExpression reference ? BindReference(reference) : BindExpression(argument);

    // `&x` / `&mut x`: a reference to a whole struct variable. It costs nothing: it is passed as a CLR `ref`.
    private BoundExpression BindReference(ReferenceExpression reference)
    {
        if (reference.Operand is not (NameExpression or SelfExpression))
        {
            Error(DiagnosticCodes.BorrowOfNonVariable, "only a whole variable can be borrowed, not a field or another expression", reference.Span);
            return new BoundErrorExpression();
        }

        var place = BindPlace(reference.Operand);
        if (place.Type == TypeSymbol.ErrorType)
        {
            return place;
        }

        if (StructOf(place.Type) is not { IsError: false } owner)
        {
            Error(
                DiagnosticCodes.BorrowOfNonStruct,
                $"cannot borrow a value of type '{place.Type}': only structs can be borrowed for now",
                reference.Span);
            return new BoundErrorExpression();
        }

        var root = BoundPlaces.Root(place);
        if (reference.IsMutable && !BoundPlaces.IsMutable(root))
        {
            Error(DiagnosticCodes.NotMutable, NotMutableMessage($"borrow '{root.Name}' with '{Punctuation.MutableBorrowKeyword}'", root), reference.Span);
            return new BoundErrorExpression();
        }

        return new BoundBorrowExpression(place, owner.BorrowOf(reference.IsMutable));
    }

    // A borrow means something only as an argument for a borrow parameter, so everywhere else it is an error.
    private BoundErrorExpression BindStrayReference(ReferenceExpression reference)
    {
        Error(DiagnosticCodes.BorrowOutsideArgument, "a borrow can only be passed directly as an argument to a function or method written in Nir", reference.Span);
        return new BoundErrorExpression();
    }

    // For calls of built-ins and .NET methods, which take no borrows.
    private ImmutableArray<BoundExpression> RejectBorrows(ImmutableArray<Expression> syntax, ImmutableArray<BoundExpression> arguments)
    {
        if (!arguments.Any(a => a is BoundBorrowExpression))
        {
            return arguments;
        }

        return
        [
            .. arguments.Select((argument, i) =>
            {
                if (argument is not BoundBorrowExpression)
                {
                    return argument;
                }

                Error(DiagnosticCodes.BorrowOutsideArgument, "a borrow can only be passed to a function or method written in Nir, not to a .NET method or 'print'", syntax[i].Span);
                return new BoundErrorExpression();
            }),
        ];
    }

    // An argument fits a parameter of the same type; a `&mut` borrow also fits a read-only `&` parameter.
    private static bool ArgumentFits(TypeSymbol argument, TypeSymbol parameter) =>
        argument == parameter ||
        (parameter is BorrowTypeSymbol { IsMutable: false } shared && argument is BorrowTypeSymbol { IsMutable: true } mutable && mutable.Element == shared.Element);

    // The mismatch message, with the fix when the mistake is a borrow written wrongly or not at all.
    private static string MismatchMessage(TypeSymbol argument, TypeSymbol parameter)
    {
        var message = $"cannot convert '{argument}' to '{parameter}'";
        return (argument, parameter) switch
        {
            (_, BorrowTypeSymbol wanted) when argument == wanted.Element =>
                message + $"; write '{(wanted.IsMutable ? Punctuation.MutableBorrowKeyword : Punctuation.SharedBorrow)}' before the variable to borrow it",
            (BorrowTypeSymbol given, _) when parameter == given.Element =>
                message + "; a borrow cannot be passed where a value is expected, pass the variable itself to copy it",
            (BorrowTypeSymbol { IsMutable: false }, BorrowTypeSymbol { IsMutable: true }) =>
                message + $"; borrow it with '{Punctuation.MutableBorrowKeyword}' instead",
            _ => message,
        };
    }
}
