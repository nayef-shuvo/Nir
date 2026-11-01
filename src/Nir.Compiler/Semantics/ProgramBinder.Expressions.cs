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
        IntLiteralExpression i => BindIntLiteral(i),
        DoubleLiteralExpression d => BindDoubleLiteral(d),
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

        // int, double and bool all have ToString() with no parameters that returns a string.
        var toString = ClrMethods.FindCandidates(value.Type.ClrType, "ToString", isStatic: false)
            .First(c => c.ParameterTypes.Length == 0 && c.ReturnType == TypeSymbol.StringType);
        return new BoundMemberCallExpression(value, new ClrMethodSymbol("ToString", toString.Method, TypeSymbol.StringType, []), []);
    }

    // `x as T` converts between int and double only (design.md "Strings and conversions").
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

        if (!IsNumber(operand.Type) || !IsNumber(target))
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{operand.Type}' to '{target}'", cast.Span);
            return new BoundErrorExpression();
        }

        return operand.Type == target ? operand : new BoundConversion(operand, target);
    }

    private static bool IsNumber(TypeSymbol type) => type == TypeSymbol.IntType || type == TypeSymbol.DoubleType;

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
        var operand = BindExpression(unary.Operand);
        if (operand.Type == TypeSymbol.ErrorType)
        {
            return operand;
        }

        var isNot = unary.Operator == UnaryOperator.Not;
        var accepted = isNot
            ? operand.Type == TypeSymbol.BoolType
            : operand.Type == TypeSymbol.IntType || operand.Type == TypeSymbol.DoubleType;
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

    // Both operands must have the same type; there are no implicit conversions (design.md, "Numbers").
    private static TypeSymbol? BinaryResultType(BinaryOperator op, TypeSymbol left, TypeSymbol right)
    {
        if (left != right)
        {
            return null;
        }

        var isNumber = IsNumber(left);
        var isString = left == TypeSymbol.StringType;
        return op switch
        {
            BinaryOperator.Add => isNumber || isString ? left : null,
            BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Remainder
                => isNumber ? left : null,
            BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual
                => isNumber ? TypeSymbol.BoolType : null,
            BinaryOperator.Equal or BinaryOperator.NotEqual
                => isNumber || isString || left == TypeSymbol.BoolType ? TypeSymbol.BoolType : null,
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

    private BoundExpression BindIntLiteral(IntLiteralExpression literal)
    {
        if (int.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return new BoundIntLiteral(value);
        }

        Error(DiagnosticCodes.LiteralOutOfRange, $"'{literal.Text}' is out of range for '{TypeSymbol.IntType}'", literal.Span);
        return new BoundErrorExpression();
    }

    private BoundExpression BindDoubleLiteral(DoubleLiteralExpression literal)
    {
        var value = double.Parse(literal.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value))
        {
            Error(DiagnosticCodes.LiteralOutOfRange, $"'{literal.Text}' is out of range for '{TypeSymbol.DoubleType}'", literal.Span);
            return new BoundErrorExpression();
        }

        return new BoundDoubleLiteral(value);
    }
}
