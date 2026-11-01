using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Expressions: literals, operators, names, strings and conversions.</summary>
internal sealed partial class ProgramBinder
{
    // Never returns null: a failed expression becomes a BoundErrorExpression, whose error is already reported.
    private BoundExpression BindExpression(Expression expression) => expression switch
    {
        StringLiteralExpression s => new BoundStringLiteral(s.Value),
        IntLiteralExpression i => BindNumberLiteral(i.Text, i.Span, isFraction: false, negative: false),
        DoubleLiteralExpression d => BindNumberLiteral(d.Text, d.Span, isFraction: true, negative: false),
        CharLiteralExpression c => BindCharLiteral(c),
        BoolLiteralExpression b => new BoundBoolLiteral(b.Value),
        CallExpression c => BindCall(c),
        MemberCallExpression m => BindMemberCall(m),
        NameExpression n => Deref(BindName(n)),
        SelfExpression s => Deref(BindSelf(s)),
        ReferenceExpression r => BindStrayReference(r),
        StructLiteralExpression l => BindStructLiteral(l),
        TupleLiteralExpression t => BindTupleLiteral(t),
        MatchExpression m => BindMatch(m, isStatement: false),
        ErrorLiteralExpression e => BindErrorLiteral(e),
        ErrorMessageExpression e => BindErrorMessage(e),
        TryExpression t => BindTry(t, wholeStatementValue: false),
        CatchExpression c => BindCatch(c, wholeStatementValue: false),
        FieldAccessExpression f => BindFieldAccess(f),
        InterpolatedStringExpression i => BindInterpolated(i),
        CastExpression c => BindCast(c),
        ParenthesizedExpression p => BindExpression(p.Inner),
        UnaryExpression u => BindUnary(u),
        BinaryExpression b => BindBinary(b),
        _ => throw new UnreachableException(),
    };

    // Every part becomes a string: text stays a literal, and a value of another type is wrapped in its own ToString().
    private BoundExpression BindInterpolated(InterpolatedStringExpression interpolated)
    {
        var parts = ImmutableArray.CreateBuilder<BoundExpression>();
        var failed = false;

        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case InterpolatedText { Text.Length: > 0 } text:
                    parts.Add(new BoundStringLiteral(text.Text));
                    break;

                case InterpolatedHole hole:
                    var value = BindExpression(hole.Expression);
                    if (value.Type == TypeSymbol.ErrorType)
                    {
                        failed = true;
                    }
                    else if (value.Type == TypeSymbol.UnitType)
                    {
                        Error(DiagnosticCodes.UnitInInterpolation, $"cannot use a value of type '{TypeNames.UnitType}' in an interpolated string", hole.Expression.Span);
                        failed = true;
                    }
                    else if (value.Type is StructTypeSymbol or TupleTypeSymbol or UnionTypeSymbol or AnyErrorTypeSymbol)
                    {
                        Error(
                            DiagnosticCodes.StructInInterpolation,
                            $"cannot put a value of type '{value.Type}' in an interpolated string; " +
                            value.Type switch
                            {
                                TupleTypeSymbol => "use its elements",
                                UnionTypeSymbol => "use 'match' to look inside it",
                                AnyErrorTypeSymbol => "use its 'message'",
                                _ => "use its fields",
                            },
                            hole.Expression.Span);
                        failed = true;
                    }
                    else
                    {
                        parts.Add(ToStringPart(value));
                    }

                    break;
            }
        }

        return failed ? new BoundErrorExpression() : new BoundInterpolatedString(parts.ToImmutable());
    }

    private static BoundExpression ToStringPart(BoundExpression value)
    {
        if (value.Type == TypeSymbol.StringType)
        {
            return value;
        }

        // Every number type, char and bool have ToString() with no parameters that returns a string.
        var toString = ClrMethods.FindCandidates(value.Type.ClrType, "ToString", isStatic: false)
            .First(c => c.ParameterTypes.Length == 0 && c.ReturnType == TypeSymbol.StringType);
        return new BoundMemberCallExpression(value, new ClrMethodSymbol("ToString", toString.Method, TypeSymbol.StringType, []), []);
    }

    // `x as T` converts between the number types, and between char and the integer types (design.md "Strings and conversions").
    private BoundExpression BindCast(CastExpression cast)
    {
        var operand = BindExpression(cast.Operand);
        var target = LookupType(cast.TargetType.Text);
        if (target is null)
        {
            Error(DiagnosticCodes.UnknownType, $"unknown type '{cast.TargetType.Text}'", cast.TargetType.Span);
            return new BoundErrorExpression();
        }

        if (operand.Type == TypeSymbol.ErrorType)
        {
            return operand;
        }

        if (!CanConvert(operand.Type, target))
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{operand.Type}' to '{target}'", cast.Span);
            return new BoundErrorExpression();
        }

        return operand.Type == target ? operand : new BoundConversion(operand, target);
    }

    // Any number to any number, and char to and from the integers. Not bool, string or anything else, and not char to a fraction type.
    private static bool CanConvert(TypeSymbol from, TypeSymbol to) =>
        (NumberTypes.IsNumber(from) && NumberTypes.IsNumber(to)) ||
        (NumberTypes.IsChar(from) && NumberTypes.IsInteger(to)) ||
        (NumberTypes.IsInteger(from) && NumberTypes.IsChar(to));

    private BoundExpression BindName(NameExpression name)
    {
        if (_scopes.TryGet(name.Name.Text, out var variable))
        {
            return new BoundVariableExpression(variable);
        }

        Error(DiagnosticCodes.UndefinedName, $"'{name.Name.Text}' is not defined", name.Span);
        return new BoundErrorExpression();
    }

    private BoundExpression BindUnary(UnaryExpression unary)
    {
        // A minus in front of a number literal is part of the literal, so `-128` fits an sbyte and `-2147483648` an int.
        if (unary.Operator == UnaryOperator.Negate && unary.Operand is IntLiteralExpression or DoubleLiteralExpression)
        {
            return unary.Operand is IntLiteralExpression whole
                ? BindNumberLiteral(whole.Text, unary.Span, isFraction: false, negative: true)
                : BindNumberLiteral(((DoubleLiteralExpression)unary.Operand).Text, unary.Span, isFraction: true, negative: true);
        }

        var operand = BindExpression(unary.Operand);
        if (operand.Type == TypeSymbol.ErrorType)
        {
            return operand;
        }

        var isNot = unary.Operator == UnaryOperator.Not;
        var accepted = isNot
            ? operand.Type == TypeSymbol.BoolType
            : NumberTypes.IsSignedInteger(operand.Type) || NumberTypes.IsFloatingPoint(operand.Type) || NumberTypes.IsDecimal(operand.Type);
        if (!accepted)
        {
            Error(
                DiagnosticCodes.InvalidOperator,
                $"operator '{OperatorText.Of(unary.Operator)}' cannot be applied to '{operand.Type}'",
                unary.Span);
            return new BoundErrorExpression();
        }

        return new BoundUnaryExpression(isNot ? BoundUnaryOperator.Not : BoundUnaryOperator.Negate, operand);
    }

    private BoundExpression BindBinary(BinaryExpression binary)
    {
        var left = BindExpression(binary.Left);
        var right = BindExpression(binary.Right);
        if (left.Type == TypeSymbol.ErrorType || right.Type == TypeSymbol.ErrorType)
        {
            return new BoundErrorExpression();
        }

        // A literal on one side takes the type of the other side (`count + 1` with a long count).
        (left, right) = MatchLiteral(left, right);
        if (left.Type == TypeSymbol.ErrorType || right.Type == TypeSymbol.ErrorType)
        {
            return new BoundErrorExpression();
        }

        var resultType = BinaryResultType(binary.Operator, left.Type, right.Type);
        if (resultType is null)
        {
            Error(
                DiagnosticCodes.InvalidOperator,
                $"operator '{OperatorText.Of(binary.Operator)}' cannot be applied to '{left.Type}' and '{right.Type}'",
                binary.Span);
            return new BoundErrorExpression();
        }

        return new BoundBinaryExpression(ToBoundOperator(binary.Operator), left, right, resultType);
    }

    // If exactly one operand is a literal without a type that suits the other operand, it takes that type. An out-of-range value is
    // reported here and comes back as an error expression.
    private (BoundExpression Left, BoundExpression Right) MatchLiteral(BoundExpression left, BoundExpression right)
    {
        if (left.Type == right.Type && left is not BoundNumberLiteral { Value: null } && right is not BoundNumberLiteral { Value: null })
        {
            return (left, right);
        }

        if (CanAdapt(right, left.Type))
        {
            return (left, Adapt((BoundNumberLiteral)right, left.Type));
        }

        if (CanAdapt(left, right.Type))
        {
            return (Adapt((BoundNumberLiteral)left, right.Type), right);
        }

        return (left, right);
    }

    // Both operands must have the same type; there are no implicit conversions (design.md, "Numbers").
    private static TypeSymbol? BinaryResultType(BinaryOperator op, TypeSymbol left, TypeSymbol right)
    {
        if (left != right)
        {
            return null;
        }

        var isNumber = NumberTypes.IsNumber(left);
        var isString = left == TypeSymbol.StringType;
        return op switch
        {
            BinaryOperator.Add => isNumber || isString ? left : null,
            BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Remainder
                => isNumber ? left : null,
            BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual
                => NumberTypes.IsOrdered(left) ? TypeSymbol.BoolType : null,
            BinaryOperator.Equal or BinaryOperator.NotEqual
                => isNumber || isString || NumberTypes.IsChar(left) || left == TypeSymbol.BoolType ? TypeSymbol.BoolType : null,
            BinaryOperator.And or BinaryOperator.Or
                => left == TypeSymbol.BoolType ? TypeSymbol.BoolType : null,
            _ => throw new UnreachableException(),
        };
    }

    private static BoundBinaryOperator ToBoundOperator(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => BoundBinaryOperator.Add,
        BinaryOperator.Subtract => BoundBinaryOperator.Subtract,
        BinaryOperator.Multiply => BoundBinaryOperator.Multiply,
        BinaryOperator.Divide => BoundBinaryOperator.Divide,
        BinaryOperator.Remainder => BoundBinaryOperator.Remainder,
        BinaryOperator.Less => BoundBinaryOperator.Less,
        BinaryOperator.LessOrEqual => BoundBinaryOperator.LessOrEqual,
        BinaryOperator.Greater => BoundBinaryOperator.Greater,
        BinaryOperator.GreaterOrEqual => BoundBinaryOperator.GreaterOrEqual,
        BinaryOperator.Equal => BoundBinaryOperator.Equal,
        BinaryOperator.NotEqual => BoundBinaryOperator.NotEqual,
        BinaryOperator.And => BoundBinaryOperator.And,
        BinaryOperator.Or => BoundBinaryOperator.Or,
        _ => throw new UnreachableException(),
    };
}
