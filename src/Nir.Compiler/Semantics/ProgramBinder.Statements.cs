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
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{condition.Type}' to 'bool'", expression.Span);
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
        var start = BindRangeBound(statement.Start);
        var end = BindRangeBound(statement.End);

        _scopes.Push();
        var variable = new VariableSymbol(statement.Variable.Text, TypeSymbol.IntType, isMutable: false);
        Declare(variable, statement.Variable);
        var body = BindLoopBody(statement.Body);
        _scopes.Pop();

        return new BoundForStatement(variable, start, end, statement.IsInclusive, body);
    }

    private BoundExpression BindRangeBound(Expression expression)
    {
        var bound = BindExpression(expression);
        if (bound.Type != TypeSymbol.IntType && bound.Type != TypeSymbol.ErrorType)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{bound.Type}' to 'int'", expression.Span);
        }

        return bound;
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
        var value = statement.Value is { } expression ? BindExpression(expression) : null;
        var function = _currentFunction!; // statements are only bound inside BindFunction
        var expected = function.ReturnType;

        if (expected == TypeSymbol.ErrorType || value?.Type == TypeSymbol.ErrorType)
        {
            // The signature or the value already had an error.
        }
        else if (expected == TypeSymbol.UnitType)
        {
            if (value is not null && value.Type != TypeSymbol.UnitType)
            {
                Error(
                    DiagnosticCodes.ReturnValueInUnitFunction,
                    $"cannot return a value from '{function.Name}' because it returns '()'",
                    statement.Value!.Span);
            }
        }
        else if (value is null)
        {
            Error(DiagnosticCodes.MissingReturnValue, $"'{function.Name}' must return a value of type '{expected}'", statement.Span);
        }
        else if (value.Type != expected)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{expected}'", statement.Value!.Span);
        }

        return new BoundReturnStatement(value);
    }

    private BoundVariableDeclaration BindVariableDeclaration(VariableDeclaration declaration)
    {
        // Bind the initializer first, so `let x = x` does not see the variable being declared.
        var initializer = BindExpression(declaration.Initializer);

        var type = initializer.Type;
        if (declaration.DeclaredType is { } annotation)
        {
            type = ResolveTypeSyntax(annotation);
            if (type != TypeSymbol.ErrorType && initializer.Type != TypeSymbol.ErrorType && initializer.Type != type)
            {
                Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{initializer.Type}' to '{type}'", declaration.Initializer.Span);
            }
        }
        else if (type == TypeSymbol.UnitType)
        {
            Error(DiagnosticCodes.UnitVariable, $"cannot declare '{declaration.Name.Text}' with type '()'", declaration.Name.Span);
            type = TypeSymbol.ErrorType;
        }

        var variable = new VariableSymbol(declaration.Name.Text, type, declaration.IsMutable);
        Declare(variable, declaration.Name);

        return new BoundVariableDeclaration(variable, initializer);
    }

    private BoundStatement BindAssignment(AssignmentStatement assignment)
    {
        var value = BindExpression(assignment.Value);

        if (assignment.Target is FieldAccessExpression)
        {
            return BindFieldAssignment(assignment, value);
        }

        if (assignment.Target is SelfExpression self)
        {
            if (BindSelf(self).Type != TypeSymbol.ErrorType)
            {
                Error(DiagnosticCodes.NotMutable, "cannot assign to 'self' itself; assign to its fields instead", self.Span);
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

        if (value.Type != TypeSymbol.ErrorType && variable.Type != TypeSymbol.ErrorType && value.Type != variable.Type)
        {
            Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{variable.Type}'", assignment.Value.Span);
        }

        return new BoundAssignment(variable, value);
    }

    // The built-in types and the structs of this file.
    private TypeSymbol? LookupType(string name) =>
        LookupPrimitive(name) ?? (name == "Self" ? _selfType : _structs.GetValueOrDefault(name)) ??
        (_aliasDeclarations.ContainsKey(name) ? ResolveAlias(name) : null);

    private static TypeSymbol? LookupPrimitive(string name) => name switch
    {
        "int" => TypeSymbol.IntType,
        "double" => TypeSymbol.DoubleType,
        "bool" => TypeSymbol.BoolType,
        "string" => TypeSymbol.StringType,
        _ => null,
    };
}
