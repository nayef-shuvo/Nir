using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Methods: <c>impl</c> blocks, <c>self</c> and <c>Self</c>, calls on values and associated function calls.</summary>
internal sealed partial class ProgramBinder
{
    // The struct whose impl is being declared or whose method is being bound: what `Self` means.
    private StructTypeSymbol? _selfType;

    // Pass 1c: the signatures of every method, so any body may call any method.
    private void DeclareImpls(ImmutableArray<ImplDeclaration> impls)
    {
        foreach (var impl in impls)
        {
            if (ResolveStructName(impl.TypeName) is not { } owner)
            {
                continue;
            }

            if (owner.IsError)
            {
                Error(DiagnosticCodes.NotAStruct, $"cannot add methods to the error type '{owner.Name}' yet", impl.TypeName.Span);
                continue;
            }

            foreach (var method in impl.Methods)
            {
                if (owner.FindMethod(method.Name.Text) is not null)
                {
                    Error(DiagnosticCodes.DuplicateFunction, $"method '{method.Name.Text}' is already defined for '{owner.Name}'", method.Name.Span);
                    continue;
                }

                owner.Methods.Add(DeclareFunction(method, owner));
            }
        }
    }

    // A struct named in code: `Self` inside an impl, or one of the structs of this file.
    private StructTypeSymbol? StructNamed(Identifier name) =>
        name.Text == ReservedNames.SelfType ? _selfType : _structs.GetValueOrDefault(name.Text);

    // The struct behind a value or a borrow of one; null for any other type.
    private static StructTypeSymbol? StructOf(TypeSymbol type) => type switch
    {
        StructTypeSymbol structType => structType,
        BorrowTypeSymbol borrow => borrow.Element,
        _ => null,
    };

    // `self` as a place: the receiver variable of the current method.
    private BoundExpression BindSelf(SelfExpression self)
    {
        if (_currentFunction?.SelfVariable is { } variable)
        {
            return new BoundVariableExpression(variable);
        }

        Error(DiagnosticCodes.SelfOutsideMethod, $"'{ReservedNames.SelfValue}' can only be used inside a method that takes '{Punctuation.SharedBorrow}{ReservedNames.SelfValue}' or '{Punctuation.MutableBorrow}{ReservedNames.SelfValue}'", self.Span);
        return new BoundErrorExpression();
    }

    // A name, `self` or a field path as a place (no copy). Anything else is an ordinary value.
    private BoundExpression BindPlace(Expression expression) => expression switch
    {
        NameExpression name => BindName(name),
        SelfExpression self => BindSelf(self),
        FieldAccessExpression access => BindFieldAccess(access),
        _ => BindExpression(expression),
    };

    // Used as a value, a borrow means the struct it points at (a copy of it).
    private static BoundExpression Deref(BoundExpression expression) =>
        expression.Type is BorrowTypeSymbol borrow ? new BoundDereference(expression, borrow.Element) : expression;

    // `value.method(args)` where value is a struct (or a borrow of one).
    private BoundExpression BindStructMethodCall(MemberCallExpression call, BoundExpression receiver, StructTypeSymbol owner, ImmutableArray<BoundExpression> arguments)
    {
        var name = call.Method.Text;
        var method = owner.FindMethod(name);
        if (method is null)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{name}' is not a member of '{owner.Name}'", call.Method.Span);
            return new BoundErrorExpression();
        }

        if (method.Receiver == ReceiverKind.None)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{name}' is an associated function; call it as {owner.Name}{Punctuation.PathSeparator}{name}(...)", call.Method.Span);
            return new BoundErrorExpression();
        }

        if (CheckUserCallArguments(call.Span, $"{owner.Name}.{name}", call.Arguments, method, arguments) is not { } checkedArguments)
        {
            return new BoundErrorExpression();
        }

        if (method.Receiver == ReceiverKind.Mutable && !CanBeChangedByCall(call, receiver, name))
        {
            return new BoundErrorExpression();
        }

        return new BoundMethodCall(receiver, method, checkedArguments);
    }

    // A `&mut self` method changes its receiver, so the receiver must be a mutable place, not a temporary copy.
    private bool CanBeChangedByCall(MemberCallExpression call, BoundExpression receiver, string name)
    {
        if (!BoundPlaces.IsPlace(receiver))
        {
            Error(
                DiagnosticCodes.MutableCallOnTemporary,
                $"cannot call '{name}' on a temporary value because it takes '&mut self'; keep the value in a variable first",
                call.Receiver.Span);
            return false;
        }

        var root = BoundPlaces.Root(receiver);
        if (!BoundPlaces.IsMutable(root))
        {
            Error(DiagnosticCodes.NotMutable, NotMutableMessage($"call '{name}' (it takes '&mut self')", root), call.Receiver.Span);
            return false;
        }

        return true;
    }

    // `Point::new(...)`, `Self::new(...)`: a function of the struct that has no receiver.
    private BoundExpression BindAssociatedCall(CallExpression call, StructTypeSymbol owner, ImmutableArray<BoundExpression> arguments)
    {
        var parts = call.Callee.Parts;
        var name = parts[1];
        var method = parts.Length == 2 ? owner.FindMethod(name.Text) : null;
        if (method is null)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{name.Text}' is not a member of '{owner.Name}'", name.Span);
            return new BoundErrorExpression();
        }

        if (method.Receiver != ReceiverKind.None)
        {
            Error(DiagnosticCodes.NoSuchMember, $"'{name.Text}' is a method; call it on a value, like value.{name.Text}(...)", name.Span);
            return new BoundErrorExpression();
        }

        return CheckUserCallArguments(call.Span, $"{owner.Name}{Punctuation.PathSeparator}{name.Text}", call.Arguments, method, arguments) is { } checkedArguments
            ? new BoundCallExpression(method, checkedArguments)
            : new BoundErrorExpression();
    }

    // Arity and argument types for a call of a function or method written in Nir; returns the arguments as the parameters
    // take them (a member passed for a union parameter is wrapped). A wrong argument type is reported but the call still has
    // its result type; a wrong count has no meaningful result, so the result is null and the call is an error.
    private ImmutableArray<BoundExpression>? CheckUserCallArguments(
        TextSpan callSpan,
        string displayName,
        ImmutableArray<Expression> argumentSyntax,
        UserFunctionSymbol target,
        ImmutableArray<BoundExpression> arguments)
    {
        if (arguments.Length != target.ParameterTypes.Length)
        {
            ReportWrongArgumentCount(callSpan, displayName, target.ParameterTypes.Length, arguments.Length);
            return null;
        }

        var result = arguments.ToBuilder();
        for (var i = 0; i < arguments.Length; i++)
        {
            var argumentType = arguments[i].Type;
            var parameterType = target.ParameterTypes[i];
            if (argumentType == TypeSymbol.ErrorType || parameterType == TypeSymbol.ErrorType || ArgumentFits(argumentType, parameterType))
            {
                continue;
            }

            if (Convert(arguments[i], parameterType) is { } converted)
            {
                result[i] = converted;
            }
            else
            {
                Error(DiagnosticCodes.TypeMismatch, MismatchMessage(argumentType, parameterType), argumentSyntax[i].Span);
            }
        }

        return result.ToImmutable();
    }

    // "cannot <action> because ...": a plain variable is not `mut`; a borrow is read-only.
    private static string NotMutableMessage(string action, VariableSymbol root) =>
        root.Type is BorrowTypeSymbol
            ? $"cannot {action} because '{root.Name}' is a read-only borrow ('{root.Type}'); {(root.Name == ReservedNames.SelfValue ? $"take '{Punctuation.MutableBorrow}{ReservedNames.SelfValue}'" : $"take '{Punctuation.MutableBorrowKeyword}'")} to change it"
            : $"cannot {action} because '{root.Name}' is not mutable";
}
