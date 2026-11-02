using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Statements: conditions, loops, returns, variables and assignment.</summary>
internal sealed partial class ProgramBinder
{
    // A condition must be a bool; there is no "truthy" value (design.md "Control flow").
    private BoundExpression BindCondition(Expression expression)
    {
        var condition = BindExpression(expression);
        if (condition.Type != TypeSymbol.BoolType && condition.Type != TypeSymbol.ErrorType)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{condition.Type}' to '{TypeNames.BoolType}'", expression.Span);
        }

        return condition;
    }

    private BoundIfStatement BindIf(IfStatement statement)
    {
        var condition = BindCondition(statement.Condition);
        var then = BindBlock(statement.Then);

        BoundBlock? elseBlock = null;
        if (statement.ElseBlock is { } block)
        {
            elseBlock = BindBlock(block);
        }
        else if (statement.ElseIf is { } elseIf)
        {
            elseBlock = new BoundBlock([BindIf(elseIf)]);
        }

        return new BoundIfStatement(condition, then, elseBlock);
    }

    private BoundWhileStatement BindWhile(WhileStatement statement)
    {
        var condition = BindCondition(statement.Condition);
        return new BoundWhileStatement(condition, BindLoopBody(statement.Body));
    }

    private BoundForStatement BindFor(ForStatement statement)
    {
        // The bounds are bound before the loop variable exists, so they cannot refer to it.
        var start = BindExpression(statement.Start);
        var end = BindExpression(statement.End);
        (start, end) = MatchLiteral(start, end);

        // Both bounds are the same integer type or char, and the loop variable has that type.
        var rangeType = CheckRangeBound(start, statement.Start, TypeSymbol.ErrorType);
        rangeType = CheckRangeBound(end, statement.End, rangeType);

        _scopes.Push();
        var variable = new VariableSymbol(statement.Variable.Text, rangeType, isMutable: false);
        Declare(variable, statement.Variable);
        var body = BindLoopBody(statement.Body);
        _scopes.Pop();

        return new BoundForStatement(variable, start, end, statement.IsInclusive, body);
    }

    // The type of the range so far: the bound's own type if it is an integer or char and agrees with the other bound.
    private TypeSymbol CheckRangeBound(BoundExpression bound, Expression syntax, TypeSymbol rangeType)
    {
        if (bound.Type == TypeSymbol.ErrorType)
        {
            return rangeType;
        }

        if (!NumberTypes.IsCountable(bound.Type))
        {
            Error(DiagnosticCodes.TypeMismatch, $"a range runs over integers or '{TypeNames.CharType}', but this is '{bound.Type}'", syntax.Span);
            return rangeType;
        }

        if (rangeType == TypeSymbol.ErrorType)
        {
            return bound.Type;
        }

        if (bound.Type != rangeType)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{bound.Type}' to '{rangeType}'", syntax.Span);
        }

        return rangeType;
    }

    private BoundBlock BindLoopBody(Block body)
    {
        _loopDepth++;
        var bound = BindBlock(body);
        _loopDepth--;
        return bound;
    }

    private BoundStatement BindLoopJump(TextSpan span, string keyword, BoundStatement statement)
    {
        if (_loopDepth == 0)
        {
            Error(DiagnosticCodes.JumpOutsideLoop, $"'{keyword}' can only be used inside a loop", span);
        }

        return statement;
    }

    private BoundReturnStatement BindReturn(ReturnStatement statement)
    {
        var value = statement.Value is { } expression ? BindStatementValue(expression) : null;
        var function = _currentFunction!; // statements are only bound inside BindFunction
        var expected = function.ReturnType;

        if (expected == TypeSymbol.ErrorType || value?.Type == TypeSymbol.ErrorType)
        {
            // The signature or the value already had an error.
        }
        else if (value is null && expected is ResultTypeSymbol { Success: null } unitResult)
        {
            value = new BoundUnitSuccess(unitResult); // a bare `return` in an `E!` function succeeds
        }
        else if (expected == TypeSymbol.UnitType)
        {
            if (value is not null && value.Type != TypeSymbol.UnitType)
            {
                Error(
                    DiagnosticCodes.ReturnValueInUnitFunction,
                    $"cannot return a value from '{function.Name}' because it returns '{TypeNames.UnitType}'",
                    statement.Value!.Span);
            }
        }
        else if (value is null)
        {
            Error(DiagnosticCodes.MissingReturnValue, $"'{function.Name}' must return a value of type '{expected}'", statement.Span);
        }
        else if (Convert(value, expected) is { } converted)
        {
            value = converted;
        }
        else
        {
            Error(DiagnosticCodes.TypeMismatch, ConversionMessage(value, expected), statement.Value!.Span);
        }

        return new BoundReturnStatement(value);
    }

    private BoundVariableDeclaration BindVariableDeclaration(VariableDeclaration declaration)
    {
        // Bind the initializer first, so `let x = x` does not see the variable being declared.
        var initializer = BindStatementValue(declaration.Initializer);

        var type = initializer.Type;
        if (declaration.DeclaredType is { } annotation)
        {
            type = ResolveTypeSyntax(annotation);
            if (type != TypeSymbol.ErrorType && initializer.Type != TypeSymbol.ErrorType && Convert(initializer, type) is { } converted)
            {
                initializer = converted;
            }
            else if (type != TypeSymbol.ErrorType && initializer.Type != TypeSymbol.ErrorType)
            {
                Error(DiagnosticCodes.TypeMismatch, ConversionMessage(initializer, type), declaration.Initializer.Span);
            }
        }
        else if (type == TypeSymbol.UnitType)
        {
            Error(DiagnosticCodes.UnitVariable, $"cannot declare '{declaration.Name.Text}' with type '{TypeNames.UnitType}'", declaration.Name.Span);
            type = TypeSymbol.ErrorType;
        }

        var variable = new VariableSymbol(declaration.Name.Text, type, declaration.IsMutable);
        Declare(variable, declaration.Name);

        return new BoundVariableDeclaration(variable, initializer);
    }

    private BoundStatement BindAssignment(AssignmentStatement assignment)
    {
        // `x = try f()` is fine, but `p.x = try f()` is not: the address of `p` would be on the stack when `try` returns.
        var value = assignment.Target is NameExpression ? BindStatementValue(assignment.Value) : BindExpression(assignment.Value);

        if (assignment.Target is FieldAccessExpression)
        {
            return BindFieldAssignment(assignment, value);
        }

        if (assignment.Target is SelfExpression self)
        {
            if (BindSelf(self).Type != TypeSymbol.ErrorType)
            {
                Error(DiagnosticCodes.NotMutable, $"cannot assign to '{ReservedNames.SelfValue}' itself; assign to its fields instead", self.Span);
            }

            return new BoundExpressionStatement(value);
        }

        if (assignment.Target is not NameExpression target)
        {
            throw new UnreachableException();
        }

        if (!_scopes.TryGet(target.Name.Text, out var variable))
        {
            Error(DiagnosticCodes.UndefinedName, $"'{target.Name.Text}' is not defined", target.Span);
            return new BoundExpressionStatement(value);
        }

        if (variable.Type is BorrowTypeSymbol)
        {
            Error(DiagnosticCodes.NotMutable, $"cannot assign to '{variable.Name}' itself because it is a borrow; assign to its fields instead", target.Span);
            return new BoundExpressionStatement(value);
        }

        if (!variable.IsMutable)
        {
            Error(DiagnosticCodes.NotMutable, $"cannot assign to '{variable.Name}' because it is not mutable", target.Span);
        }

        if (value.Type != TypeSymbol.ErrorType && variable.Type != TypeSymbol.ErrorType)
        {
            if (Convert(value, variable.Type) is { } converted)
            {
                value = converted;
            }
            else
            {
                Error(DiagnosticCodes.TypeMismatch, ConversionMessage(value, variable.Type), assignment.Value.Span);
            }
        }

        return new BoundAssignment(variable, value);
    }

    // The built-in types and the structs of this file.
    private TypeSymbol? LookupType(string name) =>
        LookupPrimitive(name) ?? (name == ReservedNames.SelfType ? _selfType : FindStruct(name)) ??
        (_aliasDeclarations.ContainsKey(name) ? ResolveAlias(name) : null) ??
        (_unionDeclarations.ContainsKey(name) ? ResolveUnion(name) : null);

    private static TypeSymbol? LookupPrimitive(string name) => name switch
    {
        TypeNames.ByteType => TypeSymbol.ByteType,
        TypeNames.SByteType => TypeSymbol.SByteType,
        TypeNames.ShortType => TypeSymbol.ShortType,
        TypeNames.UShortType => TypeSymbol.UShortType,
        TypeNames.IntType => TypeSymbol.IntType,
        TypeNames.UIntType => TypeSymbol.UIntType,
        TypeNames.LongType => TypeSymbol.LongType,
        TypeNames.ULongType => TypeSymbol.ULongType,
        TypeNames.FloatType => TypeSymbol.FloatType,
        TypeNames.DoubleType => TypeSymbol.DoubleType,
        TypeNames.DecimalType => TypeSymbol.DecimalType,
        TypeNames.CharType => TypeSymbol.CharType,
        TypeNames.BoolType => TypeSymbol.BoolType,
        TypeNames.StringType => TypeSymbol.StringType,
        TypeNames.AnyErrorType => AnyErrorTypeSymbol.Instance,
        _ => null,
    };
}
