using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Resolves names and checks types, turning the syntax tree into a bound tree.</summary>
public static class Binder
{
    /// <summary>Returns the bound program, or <c>null</c> if this call reported any error.</summary>
    public static BoundProgram? Bind(CompilationUnit unit, SourceText source, DiagnosticBag diagnostics) =>
        new ProgramBinder(source, diagnostics).Bind(unit);

    private sealed class ProgramBinder(SourceText source, DiagnosticBag diagnostics)
    {
        private readonly Dictionary<string, UserFunctionSymbol> _functions = [];
        private Dictionary<string, VariableSymbol> _variables = [];
        private bool _hadError;

        public BoundProgram? Bind(CompilationUnit unit)
        {
            // Pass 1: declare every function, so a call may come before the declaration.
            foreach (var declaration in unit.Functions)
            {
                if (_functions.ContainsKey(declaration.Name.Text))
                {
                    Error(DiagnosticCodes.DuplicateFunction, $"function '{declaration.Name.Text}' is already defined", declaration.Name.Span);
                    continue;
                }

                _functions[declaration.Name.Text] = new UserFunctionSymbol(declaration);
            }

            if (!_functions.TryGetValue("main", out var entryPoint))
            {
                Error(DiagnosticCodes.MissingEntryPoint, "program has no 'main' function", new TextSpan(0, 0));
            }

            // Pass 2: bind the bodies.
            var functions = _functions.Values
                .Select(symbol => new BoundFunction(symbol, BindBlock(symbol.Declaration.Body)))
                .ToImmutableArray();

            return _hadError || entryPoint is null ? null : new BoundProgram(functions, entryPoint);
        }

        // One flat scope per function body. A variable is visible only after its declaration.
        private BoundBlock BindBlock(Block block)
        {
            _variables = [];
            return new BoundBlock([.. block.Statements.Select(BindStatement)]);
        }

        private BoundStatement BindStatement(Statement statement) => statement switch
        {
            ExpressionStatement s => new BoundExpressionStatement(BindExpression(s.Expression)),
            VariableDeclaration d => BindVariableDeclaration(d),
            AssignmentStatement a => BindAssignment(a),
            _ => throw new UnreachableException(),
        };

        private BoundVariableDeclaration BindVariableDeclaration(VariableDeclaration declaration)
        {
            // Bind the initializer first, so `let x = x` does not see the variable being declared.
            var initializer = BindExpression(declaration.Initializer);

            var type = initializer.Type;
            if (declaration.DeclaredType is { } annotation)
            {
                type = LookupType(annotation.Text);
                if (type is null)
                {
                    Error(DiagnosticCodes.UnknownType, $"unknown type '{annotation.Text}'", annotation.Span);
                    type = TypeSymbol.ErrorType;
                }
                else if (initializer.Type != TypeSymbol.ErrorType && initializer.Type != type)
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
            if (_variables.ContainsKey(variable.Name))
            {
                Error(DiagnosticCodes.AlreadyDeclared, $"'{variable.Name}' is already declared in this block", declaration.Name.Span);
            }
            else
            {
                _variables[variable.Name] = variable;
            }

            return new BoundVariableDeclaration(variable, initializer);
        }

        private BoundStatement BindAssignment(AssignmentStatement assignment)
        {
            var value = BindExpression(assignment.Value);

            if (!_variables.TryGetValue(assignment.Target.Text, out var variable))
            {
                Error(DiagnosticCodes.UndefinedName, $"'{assignment.Target.Text}' is not defined", assignment.Target.Span);
                return new BoundExpressionStatement(value);
            }

            if (!variable.IsMutable)
            {
                Error(DiagnosticCodes.NotMutable, $"cannot assign to '{variable.Name}' because it is not mutable", assignment.Target.Span);
            }

            if (value.Type != TypeSymbol.ErrorType && variable.Type != TypeSymbol.ErrorType && value.Type != variable.Type)
            {
                Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{value.Type}' to '{variable.Type}'", assignment.Value.Span);
            }

            return new BoundAssignment(variable, value);
        }

        private static TypeSymbol? LookupType(string name) => name switch
        {
            "int" => TypeSymbol.IntType,
            "double" => TypeSymbol.DoubleType,
            "bool" => TypeSymbol.BoolType,
            "string" => TypeSymbol.StringType,
            _ => null,
        };

        // Never returns null: a failed expression becomes a BoundErrorExpression, whose error is already reported.
        private BoundExpression BindExpression(Expression expression) => expression switch
        {
            StringLiteralExpression s => new BoundStringLiteral(s.Value),
            IntLiteralExpression i => BindIntLiteral(i),
            DoubleLiteralExpression d => BindDoubleLiteral(d),
            BoolLiteralExpression b => new BoundBoolLiteral(b.Value),
            CallExpression c => BindCall(c),
            NameExpression n => BindName(n),
            ParenthesizedExpression p => BindExpression(p.Inner),
            UnaryExpression u => BindUnary(u),
            BinaryExpression b => BindBinary(b),
            _ => throw new UnreachableException(),
        };

        private BoundExpression BindName(NameExpression name)
        {
            if (_variables.TryGetValue(name.Name.Text, out var variable))
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

            if (operand.Type != TypeSymbol.IntType && operand.Type != TypeSymbol.DoubleType)
            {
                Error(
                    DiagnosticCodes.InvalidOperator,
                    $"operator '{OperatorText.Of(unary.Operator)}' cannot be applied to '{operand.Type}'",
                    unary.Span);
                return new BoundErrorExpression();
            }

            return new BoundUnaryExpression(BoundUnaryOperator.Negate, operand);
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

            var isNumber = left == TypeSymbol.IntType || left == TypeSymbol.DoubleType;
            return op switch
            {
                BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Remainder
                    => isNumber ? left : null,
                BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual
                    => isNumber ? TypeSymbol.BoolType : null,
                BinaryOperator.Equal or BinaryOperator.NotEqual
                    => isNumber || left == TypeSymbol.BoolType ? TypeSymbol.BoolType : null,
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

        private BoundExpression BindCall(CallExpression call)
        {
            var arguments = call.Arguments.Select(BindExpression).ToImmutableArray();

            if (_functions.TryGetValue(call.Callee.Text, out var user))
            {
                return BindUserCall(call, user, arguments);
            }

            var overloads = BuiltinFunctions.GetOverloads(call.Callee.Text);
            if (overloads.IsEmpty)
            {
                Error(DiagnosticCodes.UndefinedName, $"'{call.Callee.Text}' is not defined", call.Callee.Span);
                return new BoundErrorExpression();
            }

            return BindBuiltinCall(call, overloads, arguments);
        }

        private BoundExpression BindUserCall(CallExpression call, UserFunctionSymbol target, ImmutableArray<BoundExpression> arguments)
        {
            if (arguments.Length != target.ParameterTypes.Length)
            {
                ReportWrongArgumentCount(call, target, arguments.Length);
                return new BoundErrorExpression();
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                var argumentType = arguments[i].Type;
                var parameterType = target.ParameterTypes[i];
                if (argumentType != TypeSymbol.ErrorType && argumentType != parameterType)
                {
                    Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{argumentType}' to '{parameterType}'", call.Arguments[i].Span);
                }
            }

            return new BoundCallExpression(target, arguments);
        }

        private BoundExpression BindBuiltinCall(CallExpression call, ImmutableArray<BuiltinFunctionSymbol> overloads, ImmutableArray<BoundExpression> arguments)
        {
            var sameArity = overloads.Where(o => o.ParameterTypes.Length == arguments.Length).ToList();
            if (sameArity.Count == 0)
            {
                ReportWrongArgumentCount(call, overloads[0], arguments.Length);
                return new BoundErrorExpression();
            }

            var match = sameArity.FirstOrDefault(o => Accepts(o, arguments));
            if (match is null)
            {
                var types = string.Join(", ", arguments.Select(a => a.Type));
                Error(DiagnosticCodes.NoMatchingOverload, $"no overload of '{call.Callee.Text}' accepts ({types})", call.Span);
                return new BoundErrorExpression();
            }

            return new BoundCallExpression(match, arguments);
        }

        // An error-typed argument fits any parameter: its own error is already reported.
        private static bool Accepts(FunctionSymbol function, ImmutableArray<BoundExpression> arguments) =>
            arguments.Select((argument, i) => argument.Type == TypeSymbol.ErrorType || argument.Type == function.ParameterTypes[i]).All(fits => fits);

        private void ReportWrongArgumentCount(CallExpression call, FunctionSymbol target, int actual) =>
            Error(
                DiagnosticCodes.WrongArgumentCount,
                $"'{target.Name}' expects {target.ParameterTypes.Length} argument(s) but got {actual}",
                call.Span);

        private void Error(string code, string message, TextSpan span)
        {
            _hadError = true;
            diagnostics.Report(code, Severity.Error, message, source, span);
        }
    }
}
