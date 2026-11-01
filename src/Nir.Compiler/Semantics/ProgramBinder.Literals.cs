using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>
/// Number and character literals. A number without a suffix has no type of its own yet: it is an <c>int</c> or <c>double</c>
/// unless the place it is used in asks for another number type and the value fits (design.md "Number literals").
/// </summary>
internal sealed partial class ProgramBinder
{
    // Whole numbers too big for int that have not been given a type yet. A context may still make them a long, so the error is only
    // reported when the statement they are in has been bound and nothing has adopted them.
    private readonly List<(BoundNumberLiteral Literal, string Message, TextSpan Span)> _pendingLiterals = [];

    private BoundStatement BindStatement(Statement statement)
    {
        var mark = _pendingLiterals.Count;
        var bound = BindStatementCore(statement);
        ReportPendingLiterals(mark);
        return bound;
    }

    private void ReportPendingLiterals(int mark)
    {
        for (var i = _pendingLiterals.Count - 1; i >= mark; i--)
        {
            Error(DiagnosticCodes.LiteralOutOfRange, _pendingLiterals[i].Message, _pendingLiterals[i].Span);
            _pendingLiterals.RemoveAt(i);
        }
    }

    // `negative` is set when the literal is the operand of a unary minus: `-128` is checked as a whole, so it fits an sbyte.
    private BoundExpression BindNumberLiteral(string written, TextSpan span, bool isFraction, bool negative)
    {
        var underscore = written.IndexOf('_', StringComparison.Ordinal);
        var digits = underscore < 0 ? written : written[..underscore];
        var text = negative ? "-" + digits : digits;
        var shown = text;

        if (underscore >= 0)
        {
            var type = LookupPrimitive(written[(underscore + 1)..]);
            if (type is null || !NumberTypes.IsNumber(type))
            {
                throw new InvalidOperationException($"The lexer accepted the suffix of '{written}'.");
            }

            if (isFraction && !NumberTypes.IsFloatingPoint(type) && !NumberTypes.IsDecimal(type))
            {
                Error(DiagnosticCodes.LiteralSuffixNotAllowed, $"'{digits}' has a fraction, so it cannot be a '{type}'", span);
                return new BoundErrorExpression();
            }

            if (NumberTypes.Parse(text, type) is not { } typed)
            {
                Error(DiagnosticCodes.LiteralOutOfRange, $"'{shown}' is out of range for '{type}'", span);
                return new BoundErrorExpression();
            }

            return new BoundNumberLiteral(typed, text, type, CanAdapt: false, span);
        }

        var defaultType = isFraction ? TypeSymbol.DoubleType : TypeSymbol.IntType;
        if (NumberTypes.Parse(text, defaultType) is { } value)
        {
            return new BoundNumberLiteral(value, text, defaultType, CanAdapt: true, span);
        }

        var message = $"'{shown}' is out of range for '{defaultType}'";
        if (isFraction || (NumberTypes.Parse(text, TypeSymbol.LongType) is null && NumberTypes.Parse(text, TypeSymbol.ULongType) is null))
        {
            Error(DiagnosticCodes.LiteralOutOfRange, message, span);
            return new BoundErrorExpression();
        }

        var pending = new BoundNumberLiteral(null, text, defaultType, CanAdapt: true, span);
        _pendingLiterals.Add((pending, message, span));
        return pending;
    }

    private BoundExpression BindCharLiteral(CharLiteralExpression literal)
    {
        if (literal.Value.Length != 1)
        {
            Error(
                DiagnosticCodes.InvalidCharLiteral,
                literal.Value.Length == 0
                    ? "a character literal cannot be empty"
                    : "a character literal must hold exactly one character (one UTF-16 code unit)",
                literal.Span);
            return new BoundErrorExpression();
        }

        return new BoundCharLiteral(literal.Value[0]);
    }

    // A literal without a suffix can take the type its context asks for: whole numbers become integer types, numbers with a
    // fraction become float, double or decimal.
    private static bool CanAdapt(BoundExpression expression, TypeSymbol target) =>
        expression is BoundNumberLiteral { CanAdapt: true } literal &&
        (literal.Text.Contains('.', StringComparison.Ordinal)
            ? NumberTypes.IsFloatingPoint(target) || NumberTypes.IsDecimal(target)
            : NumberTypes.IsInteger(target));

    // "cannot convert 'int' to 'double'", and for a whole-number literal that wants a fraction type, how to write one.
    private static string ConversionMessage(BoundExpression value, TypeSymbol expected)
    {
        var message = $"cannot convert '{value.Type}' to '{expected}'";
        return value is BoundNumberLiteral { CanAdapt: true } literal && !literal.Text.Contains('.', StringComparison.Ordinal) &&
               (NumberTypes.IsFloatingPoint(expected) || NumberTypes.IsDecimal(expected))
            ? $"{message}; write '{literal.Text}.0' or '{literal.Text}_{expected}' for a {expected}"
            : message;
    }

    // Gives the literal the type of its context, or reports that the value does not fit it.
    private BoundExpression Adapt(BoundNumberLiteral literal, TypeSymbol target)
    {
        _pendingLiterals.RemoveAll(p => ReferenceEquals(p.Literal, literal));
        if (NumberTypes.Parse(literal.Text, target) is not { } value)
        {
            Error(DiagnosticCodes.LiteralOutOfRange, $"'{literal.Text}' is out of range for '{target}'", literal.Span);
            return new BoundErrorExpression();
        }

        return new BoundNumberLiteral(value, literal.Text, target, CanAdapt: false, literal.Span);
    }
}
