using System.Collections.Immutable;
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

        private BoundBlock BindBlock(Block block)
        {
            var statements = ImmutableArray.CreateBuilder<BoundStatement>();
            foreach (var statement in block.Statements)
            {
                if (BindStatement(statement) is { } bound)
                {
                    statements.Add(bound);
                }
            }

            return new BoundBlock(statements.ToImmutable());
        }

        private BoundExpressionStatement? BindStatement(Statement statement) => statement switch
        {
            ExpressionStatement s => BindExpression(s.Expression) is { } expression ? new BoundExpressionStatement(expression) : null,
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        // Returns null when the expression had an error; the error is already reported.
        private BoundExpression? BindExpression(Expression expression) => expression switch
        {
            StringLiteralExpression s => new BoundStringLiteral(s.Value),
            CallExpression c => BindCall(c),
            _ => throw new System.Diagnostics.UnreachableException(),
        };

        private BoundCallExpression? BindCall(CallExpression call)
        {
            var arguments = call.Arguments.Select(BindExpression).ToList();

            FunctionSymbol? target = _functions.TryGetValue(call.Callee.Text, out var user)
                ? user
                : BuiltinFunctions.TryGet(call.Callee.Text, out var builtin) ? builtin : null;

            if (target is null)
            {
                Error(DiagnosticCodes.UndefinedName, $"'{call.Callee.Text}' is not defined", call.Callee.Span);
                return null;
            }

            if (arguments.Contains(null))
            {
                return null;
            }

            if (arguments.Count != target.ParameterTypes.Length)
            {
                Error(
                    DiagnosticCodes.WrongArgumentCount,
                    $"'{target.Name}' expects {target.ParameterTypes.Length} argument(s) but got {arguments.Count}",
                    call.Span);
                return null;
            }

            var ok = true;
            for (var i = 0; i < arguments.Count; i++)
            {
                var argumentType = arguments[i]!.Type;
                var parameterType = target.ParameterTypes[i];
                if (argumentType != parameterType)
                {
                    Error(DiagnosticCodes.TypeMismatch, $"cannot convert '{argumentType}' to '{parameterType}'", call.Arguments[i].Span);
                    ok = false;
                }
            }

            return ok ? new BoundCallExpression(target, [.. arguments.Select(a => a!)]) : null;
        }

        private void Error(string code, string message, TextSpan span)
        {
            _hadError = true;
            diagnostics.Report(code, Severity.Error, message, source, span);
        }
    }
}
