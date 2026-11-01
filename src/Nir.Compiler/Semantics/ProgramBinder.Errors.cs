using System.Collections.Immutable;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Errors: result types <c>E!T</c>, error values, <c>try</c> and <c>catch</c>.</summary>
internal sealed partial class ProgramBinder
{
    private readonly Dictionary<(TypeSymbol Error, TypeSymbol? Success), ResultTypeSymbol> _results = [];
    private readonly List<ResultTypeSymbol> _resultOrder = [];

    // A declared error type or anyerror.
    private static bool IsErrorType(TypeSymbol type) => type is AnyErrorTypeSymbol or StructTypeSymbol { IsError: true };

    private static bool IsDeclaredError(TypeSymbol type) => type is StructTypeSymbol { IsError: true };

    // The one symbol for an E!T, so the same written type is the same type everywhere.
    private ResultTypeSymbol GetResult(TypeSymbol error, TypeSymbol? success)
    {
        if (!_results.TryGetValue((error, success), out var result))
        {
            result = new ResultTypeSymbol(error, success, _resultOrder.Count);
            _results[(error, success)] = result;
            _resultOrder.Add(result);
        }

        return result;
    }

    // `E!T` or `E!`: E must be an error type or anyerror, and T may be neither an error nor a result.
    private TypeSymbol ResolveResultType(ResultTypeSyntax syntax)
    {
        var failed = false;
        var error = LookupType(syntax.Error.Text);
        if (error is null)
        {
            Error(DiagnosticCodes.UnknownType, $"unknown type '{syntax.Error.Text}'", syntax.Error.Span);
            failed = true;
        }
        else if (error == TypeSymbol.ErrorType)
        {
            failed = true;
        }
        else if (!IsErrorType(error))
        {
            Error(DiagnosticCodes.NotAnErrorType, $"the type before '{Punctuation.ResultMarker}' must be an error type or '{TypeNames.AnyErrorType}', but '{error}' is not one", syntax.Error.Span);
            failed = true;
        }

        TypeSymbol? success = null;
        if (syntax.Success is { } successSyntax)
        {
            success = ResolveTypeSyntax(successSyntax);
            if (success == TypeSymbol.ErrorType)
            {
                failed = true;
            }
            else if (IsErrorType(success) || success is ResultTypeSymbol)
            {
                Error(DiagnosticCodes.InvalidResultValue, $"the value after '{Punctuation.ResultMarker}' cannot be an error or a result, but '{success}' is one", successSyntax.Span);
                failed = true;
            }
        }

        return failed ? TypeSymbol.ErrorType : GetResult(error!, success);
    }

    // `error E { field = value, ... }`: every field once, like a struct literal, but the `error` marks it as the failure.
    private BoundExpression BindErrorLiteral(ErrorLiteralExpression literal)
    {
        var errorType = ResolveStructName(literal.TypeName);
        if (errorType is not null && !errorType.IsError)
        {
            Error(DiagnosticCodes.ErrorLiteralForm, $"'{errorType.Name}' is a struct, not an error type; write it without 'error'", literal.TypeName.Span);
            errorType = null;
        }

        var initializers = BindInitializers(errorType, literal.TypeName, literal.Fields);
        return errorType is null || initializers is null ? new BoundErrorExpression() : new BoundErrorLiteral(errorType, initializers.Value);
    }

    // `error "text"`: an anyerror whose message is the text.
    private BoundExpression BindErrorMessage(ErrorMessageExpression expression)
    {
        var message = BindExpression(expression.Message);
        if (message.Type == TypeSymbol.ErrorType)
        {
            return message;
        }

        if (message.Type != TypeSymbol.StringType)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{message.Type}' to '{TypeNames.StringType}'", expression.Message.Span);
            return new BoundErrorExpression();
        }

        return new BoundErrorMessage(message);
    }

    // `try`, and a `catch` with a block, can return from the function. That is only safe where nothing else is half-evaluated, so
    // for now they must be the whole value of a statement (see decisions.md D-078).
    private BoundExpression BindStatementValue(Expression expression) => expression switch
    {
        TryExpression t => BindTry(t, wholeStatementValue: true),
        CatchExpression c => BindCatch(c, wholeStatementValue: true),
        _ => BindExpression(expression),
    };

    // A call into .NET, or a conversion with `as`, can throw, and `try` / `catch` turn that into an `anyerror!T`: the emitter makes
    // the call inside a CLR `try` block of a helper method, so a protected call can sit anywhere. Anything else is returned unchanged.
    private BoundExpression ProtectDotNetCall(BoundExpression operand) =>
        operand is BoundCallExpression { Target: ClrMethodSymbol } or BoundMemberCallExpression or BoundConversion
            ? new BoundProtectedCall(operand, GetResult(AnyErrorTypeSymbol.Instance, operand.Type == TypeSymbol.UnitType ? null : operand.Type))
            : operand;

    private BoundExpression BindTry(TryExpression tryExpression, bool wholeStatementValue)
    {
        var operand = ProtectDotNetCall(BindExpression(tryExpression.Operand));
        if (!wholeStatementValue)
        {
            Error(DiagnosticCodes.TryPosition, "'try' can only be the whole value of a 'let', an assignment to a variable, a 'return' or a statement", tryExpression.Span);
            return new BoundErrorExpression();
        }

        if (operand.Type == TypeSymbol.ErrorType)
        {
            return operand;
        }

        if (operand.Type is not ResultTypeSymbol result)
        {
            Error(DiagnosticCodes.NotAResult, $"'try' needs a value of an 'E!T' type, but this is '{operand.Type}'", tryExpression.Operand.Span);
            return new BoundErrorExpression();
        }

        var function = _currentFunction!; // statements are only bound inside BindFunction
        if (function.ReturnType == TypeSymbol.ErrorType)
        {
            return new BoundErrorExpression(); // the signature already had an error
        }

        if (function.ReturnType is not ResultTypeSymbol own)
        {
            Error(
                DiagnosticCodes.TryOutsideResultFunction,
                $"'try' returns the error to the caller, but '{function.Name}' returns '{function.ReturnType}'; make it return an 'E!T' or handle the error with 'catch'",
                tryExpression.Span);
            return new BoundErrorExpression();
        }

        if (own.Error != result.Error && own.Error is not AnyErrorTypeSymbol)
        {
            Error(
                DiagnosticCodes.TryErrorMismatch,
                $"'try' would return a '{result.Error}' from '{function.Name}', which returns '{own}'; handle it with 'catch' and make a '{own.Error}' yourself",
                tryExpression.Span);
            return new BoundErrorExpression();
        }

        return new BoundTry(operand, own, result.Success ?? TypeSymbol.UnitType);
    }

    // `a catch fallback`, `a catch e => expression` and `a catch e => { block }`. The block of a catch must leave the function unless
    // there is no value to produce.
    private BoundExpression BindCatch(CatchExpression catchExpression, bool wholeStatementValue)
    {
        var operand = ProtectDotNetCall(BindExpression(catchExpression.Operand));
        var result = operand.Type as ResultTypeSymbol;
        var failed = operand.Type == TypeSymbol.ErrorType;

        if (result is null && !failed)
        {
            Error(DiagnosticCodes.NotAResult, $"'catch' needs a value of an 'E!T' type, but this is '{operand.Type}'", catchExpression.Operand.Span);
            failed = true;
        }

        if (catchExpression.Block is not null && !wholeStatementValue)
        {
            Error(DiagnosticCodes.TryPosition, "a 'catch' with a block can only be the whole value of a 'let', an assignment to a variable, a 'return' or a statement", catchExpression.Span);
            failed = true;
        }

        var valueType = result?.Success;
        _scopes.Push();
        VariableSymbol? variable = null;
        if (catchExpression.Name is { } name && name.Text != ReservedNames.Discard)
        {
            variable = new VariableSymbol(name.Text, result?.Error ?? TypeSymbol.ErrorType, isMutable: false);
            Declare(variable, name);
        }

        BoundExpression? value = null;
        BoundBlock? body = null;
        if (catchExpression.Block is { } block)
        {
            body = BindStatements(block);
            if (valueType is not null && !BoundFlow.AlwaysReturns(body) && !failed)
            {
                Error(DiagnosticCodes.CatchBlockMustReturn, "the block of a 'catch' must end with a 'return' on every path, because there is no value to carry on with", catchExpression.Span);
                failed = true;
            }
        }
        else
        {
            value = BindExpression(catchExpression.Value!);
        }

        _scopes.Pop();

        if (value is not null && value.Type != TypeSymbol.ErrorType && !failed)
        {
            if (result!.Success is null && catchExpression.Name is null)
            {
                Error(DiagnosticCodes.TypeMismatch, $"'{result}' carries no value, so there is nothing to fall back to; use 'catch e => ...'", catchExpression.Value!.Span);
                failed = true;
            }
            else if (Convert(value, valueType ?? TypeSymbol.UnitType) is { } converted)
            {
                value = converted;
            }
            else
            {
                Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{valueType ?? TypeSymbol.UnitType}'", catchExpression.Value!.Span);
                failed = true;
            }
        }
        else if (value is not null && value.Type == TypeSymbol.ErrorType)
        {
            failed = true;
        }

        return failed ? new BoundErrorExpression() : new BoundCatch(operand, variable, value, body, valueType ?? TypeSymbol.UnitType);
    }
}
