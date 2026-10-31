using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
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
        private readonly Dictionary<string, ImportedName> _imports = [];
        private UserFunctionSymbol? _currentFunction;
        private bool _hadError;

        public BoundProgram? Bind(CompilationUnit unit)
        {
            BindImports(unit.Usings);

            // Pass 1: declare every function, so a call may come before the declaration.
            foreach (var declaration in unit.Functions)
            {
                if (_functions.ContainsKey(declaration.Name.Text))
                {
                    Error(DiagnosticCodes.DuplicateFunction, $"function '{declaration.Name.Text}' is already defined", declaration.Name.Span);
                    continue;
                }

                _functions[declaration.Name.Text] = DeclareFunction(declaration);
            }

            if (!_functions.TryGetValue("main", out var entryPoint))
            {
                Error(DiagnosticCodes.MissingEntryPoint, "program has no 'main' function", new TextSpan(0, 0));
            }

            // Pass 2: bind the bodies.
            var functions = _functions.Values
                .Select(BindFunction)
                .ToImmutableArray();

            return _hadError || entryPoint is null ? null : new BoundProgram(functions, entryPoint);
        }

        // Resolves a function's signature. This happens for every function before any body is bound.
        private UserFunctionSymbol DeclareFunction(FunctionDeclaration declaration)
        {
            var returnType = declaration.ReturnType is { } annotation ? ResolveType(annotation) : TypeSymbol.UnitType;
            var parameters = declaration.Parameters
                .Select(p => new VariableSymbol(p.Name.Text, ResolveType(p.Type), isMutable: false))
                .ToImmutableArray();

            if (declaration.Name.Text == "main" && (declaration.ReturnType is not null || parameters.Length > 0))
            {
                Error(DiagnosticCodes.InvalidEntryPoint, "'main' must have no parameters and no return type", declaration.Name.Span);
            }

            return new UserFunctionSymbol(declaration, returnType, parameters);
        }

        private TypeSymbol ResolveType(Identifier name)
        {
            if (LookupType(name.Text) is { } type)
            {
                return type;
            }

            Error(DiagnosticCodes.UnknownType, $"unknown type '{name.Text}'", name.Span);
            return TypeSymbol.ErrorType;
        }

        // One flat scope per function: the parameters first, then the body's variables. A variable is visible only
        // after its declaration.
        private BoundFunction BindFunction(UserFunctionSymbol function)
        {
            _currentFunction = function;
            _variables = [];

            var declaration = function.Declaration;
            for (var i = 0; i < function.Parameters.Length; i++)
            {
                var parameter = function.Parameters[i];
                if (_variables.ContainsKey(parameter.Name))
                {
                    Error(DiagnosticCodes.AlreadyDeclared, $"'{parameter.Name}' is already declared in this block", declaration.Parameters[i].Name.Span);
                }
                else
                {
                    _variables[parameter.Name] = parameter;
                }
            }

            var body = BindBlock(declaration.Body);

            var mustReturn = function.ReturnType != TypeSymbol.UnitType && function.ReturnType != TypeSymbol.ErrorType;
            if (mustReturn && body.Statements.LastOrDefault() is not BoundReturnStatement)
            {
                Error(DiagnosticCodes.MissingReturn, $"'{function.Name}' does not return a value on every path", declaration.Name.Span);
            }

            return new BoundFunction(function, body);
        }

        private BoundBlock BindBlock(Block block) =>
            new([.. block.Statements.Select(BindStatement)]);

        private BoundStatement BindStatement(Statement statement) => statement switch
        {
            ExpressionStatement s => new BoundExpressionStatement(BindExpression(s.Expression)),
            VariableDeclaration d => BindVariableDeclaration(d),
            AssignmentStatement a => BindAssignment(a),
            ReturnStatement r => BindReturn(r),
            _ => throw new UnreachableException(),
        };

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
            MemberCallExpression m => BindMemberCall(m),
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

            if (call.Callee.Parts.Length > 1)
            {
                return BindQualifiedCall(call, arguments);
            }

            var name = call.Callee.Text;
            if (_functions.TryGetValue(name, out var user))
            {
                return BindUserCall(call, user, arguments);
            }

            var overloads = BuiltinFunctions.GetOverloads(name);
            if (overloads.IsEmpty)
            {
                Error(DiagnosticCodes.UndefinedName, $"'{name}' is not defined", call.Callee.Span);
                return new BoundErrorExpression();
            }

            return BindClrCall(call.Span, name, name, [.. overloads.Select(ClrCandidate.From)], arguments);
        }

        private BoundExpression BindUserCall(CallExpression call, UserFunctionSymbol target, ImmutableArray<BoundExpression> arguments)
        {
            if (arguments.Length != target.ParameterTypes.Length)
            {
                ReportWrongArgumentCount(call.Span, target.Name, target.ParameterTypes.Length, arguments.Length);
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

        // `A::B::Method(args)`: the first part must be an imported name; the parts in between walk namespaces down to a
        // type; the last part is a static method of that type.
        private BoundExpression BindQualifiedCall(CallExpression call, ImmutableArray<BoundExpression> arguments)
        {
            var parts = call.Callee.Parts;
            if (!_imports.TryGetValue(parts[0].Text, out var current))
            {
                Error(DiagnosticCodes.UndefinedName, $"'{parts[0].Text}' is not defined", parts[0].Span);
                return new BoundErrorExpression();
            }

            for (var i = 1; i < parts.Length - 1; i++)
            {
                var next = Descend(current, parts[i]);
                if (next is null)
                {
                    return new BoundErrorExpression();
                }

                current = next;
            }

            var method = parts[^1];
            if (current is not TypeName typeName)
            {
                Error(DiagnosticCodes.NoSuchMember, $"'{method.Text}' is not a member of '{current.FullName}'", method.Span);
                return new BoundErrorExpression();
            }

            return BindClrMethod(call.Span, typeName.Type, method, isStatic: true, arguments);
        }

        // One step down a qualified name: a namespace contains namespaces and types; a type contains nothing we can name here.
        private ImportedName? Descend(ImportedName current, Identifier part)
        {
            if (current is NamespaceName ns)
            {
                var fullName = ns.FullName + "." + part.Text;
                if (ClrUniverse.FindType(fullName) is { } type)
                {
                    return new TypeName(type);
                }

                if (ClrUniverse.NamespaceExists(fullName))
                {
                    return new NamespaceName(fullName);
                }
            }

            Error(DiagnosticCodes.NoSuchMember, $"'{part.Text}' is not a member of '{current.FullName}'", part.Span);
            return null;
        }

        // Finds the method on a .NET type and binds the call, for both `Type::Method(...)` and `value.Method(...)`.
        private BoundExpression BindClrMethod(TextSpan callSpan, Type type, Identifier method, bool isStatic, ImmutableArray<BoundExpression> arguments)
        {
            if (!ClrMethods.HasMethodNamed(type, method.Text, isStatic))
            {
                Error(DiagnosticCodes.NoSuchMember, $"'{method.Text}' is not a member of '{type.FullName}'", method.Span);
                return new BoundErrorExpression();
            }

            var candidates = ClrMethods.FindCandidates(type, method.Text, isStatic);
            return BindClrCall(callSpan, method.Text, $"{type.Name}.{method.Text}", candidates, arguments);
        }

        // Chooses the overload whose parameter types equal the argument types exactly (design.md: no implicit conversions).
        private BoundExpression BindClrCall(
            TextSpan callSpan,
            string name,
            string displayName,
            IReadOnlyList<ClrCandidate> candidates,
            ImmutableArray<BoundExpression> arguments) =>
            ChooseClrMethod(callSpan, name, displayName, candidates, arguments) is { } symbol
                ? new BoundCallExpression(symbol, arguments)
                : new BoundErrorExpression();

        // Reports the error and returns null if there is no usable overload.
        private ClrMethodSymbol? ChooseClrMethod(
            TextSpan callSpan,
            string name,
            string displayName,
            IReadOnlyList<ClrCandidate> candidates,
            ImmutableArray<BoundExpression> arguments)
        {
            var sameArity = candidates.Where(c => c.ParameterTypes.Length == arguments.Length).ToList();
            if (candidates.Count > 0 && sameArity.Count == 0)
            {
                ReportWrongArgumentCount(callSpan, displayName, candidates[0].ParameterTypes.Length, arguments.Length);
                return null;
            }

            var match = sameArity.FirstOrDefault(c => Accepts(c, arguments));
            if (match is null)
            {
                var types = string.Join(", ", arguments.Select(a => a.Type));
                Error(DiagnosticCodes.NoMatchingOverload, $"no overload of '{displayName}' accepts ({types})", callSpan);
                return null;
            }

            if (match.ReturnType is not { } returnType)
            {
                Error(
                    DiagnosticCodes.UnsupportedClrType,
                    $"'{displayName}' returns '{match.Method.ReturnType.FullName}', which Nir cannot use yet",
                    callSpan);
                return null;
            }

            return new ClrMethodSymbol(name, match.Method, returnType, match.ParameterTypes);
        }

        // `receiver.Method(args)`: the method of the .NET type that the receiver's Nir type stands for.
        private BoundExpression BindMemberCall(MemberCallExpression call)
        {
            var receiver = BindExpression(call.Receiver);
            var arguments = call.Arguments.Select(BindExpression).ToImmutableArray();
            if (receiver.Type == TypeSymbol.ErrorType)
            {
                return new BoundErrorExpression();
            }

            var typeName = receiver.Type.Name;
            if (receiver.Type == TypeSymbol.UnitType || !ClrMethods.HasMethodNamed(receiver.Type.ClrType, call.Method.Text, isStatic: false))
            {
                Error(DiagnosticCodes.NoSuchMember, $"'{call.Method.Text}' is not a member of '{typeName}'", call.Method.Span);
                return new BoundErrorExpression();
            }

            var candidates = ClrMethods.FindCandidates(receiver.Type.ClrType, call.Method.Text, isStatic: false);
            return ChooseClrMethod(call.Span, call.Method.Text, $"{typeName}.{call.Method.Text}", candidates, arguments) is { } symbol
                ? new BoundMemberCallExpression(receiver, symbol, arguments)
                : new BoundErrorExpression();
        }

        // An error-typed argument fits any parameter: its own error is already reported.
        private static bool Accepts(ClrCandidate candidate, ImmutableArray<BoundExpression> arguments) =>
            arguments.Select((argument, i) => argument.Type == TypeSymbol.ErrorType || argument.Type == candidate.ParameterTypes[i]).All(fits => fits);

        private void ReportWrongArgumentCount(TextSpan callSpan, string name, int expected, int actual) =>
            Error(DiagnosticCodes.WrongArgumentCount, $"'{name}' expects {expected} argument(s) but got {actual}", callSpan);

        // `using clr::cs::System::IO` and `using clr::cs::System::{Math, Console}` introduce local names.
        private void BindImports(ImmutableArray<UsingDirective> usings)
        {
            foreach (var directive in usings)
            {
                var path = directive.Path;
                if (path.Length < 2 || path[0].Text != "clr" || path[1].Text != "cs")
                {
                    Error(DiagnosticCodes.InvalidImport, "imports must start with 'clr::cs'", path[0].Span);
                    continue;
                }

                if (directive.Members.IsEmpty)
                {
                    Import(path);
                    continue;
                }

                foreach (var member in directive.Members)
                {
                    Import([.. path, member]);
                }
            }
        }

        private void Import(ImmutableArray<Identifier> path)
        {
            var alias = path[^1];
            if (path.Length < 3)
            {
                Error(DiagnosticCodes.InvalidImport, "'clr::cs' alone names nothing; add a namespace or type after it", path[0].Span);
                return;
            }

            var fullName = string.Join('.', path.Skip(2).Select(part => part.Text));
            ImportedName imported;
            if (ClrUniverse.FindType(fullName) is { } type)
            {
                imported = new TypeName(type);
            }
            else if (ClrUniverse.NamespaceExists(fullName))
            {
                imported = new NamespaceName(fullName);
            }
            else
            {
                Error(DiagnosticCodes.ImportNotFound, $"'{fullName}' was not found", alias.Span);
                return;
            }

            if (!_imports.TryAdd(alias.Text, imported))
            {
                Error(DiagnosticCodes.DuplicateImport, $"'{alias.Text}' is already imported", alias.Span);
            }
        }

        /// <summary>What a <c>using</c> introduced: a .NET namespace or a .NET type.</summary>
        private abstract record ImportedName(string FullName);

        private sealed record NamespaceName(string FullName) : ImportedName(FullName);

        private sealed record TypeName(Type Type) : ImportedName(Type.FullName ?? Type.Name);

        private void Error(string code, string message, TextSpan span)
        {
            _hadError = true;
            diagnostics.Report(code, Severity.Error, message, source, span);
        }
    }
}
