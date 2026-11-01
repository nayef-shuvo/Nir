using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Nir.Compiler.Clr;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

/// <summary>Binds one compilation unit. The class is split over several files: this one has the state, the declarations and the function bodies; ProgramBinder.Statements.cs, .Expressions.cs, .Calls.cs and .Imports.cs have the rest.</summary>
internal sealed partial class ProgramBinder(SourceText source, DiagnosticBag diagnostics)
{
    private readonly Dictionary<string, UserFunctionSymbol> _functions = [];
    private ScopeStack _scopes = new();
    private int _loopDepth;
    private readonly Dictionary<string, ImportedName> _imports = [];
    private UserFunctionSymbol? _currentFunction;
    private bool _hadError;

    public BoundProgram? Bind(CompilationUnit unit)
    {
        BindImports(unit.Usings);

        // Pass 0: the structs, so functions and bodies can use them wherever they are declared.
        DeclareStructs(unit.Structs);
        DeclareUnions(unit.Unions);
        DeclareTupleAliases(unit.TupleAliases);
        ResolveUnions();
        ResolveStructFields();

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

        // Pass 1c: the methods of every struct.
        DeclareImpls(unit.Impls);

        if (!_functions.TryGetValue(ReservedNames.EntryPoint, out var entryPoint))
        {
            Error(DiagnosticCodes.MissingEntryPoint, $"program has no '{ReservedNames.EntryPoint}' function", new TextSpan(0, 0));
        }

        // Pass 2: bind the bodies.
        var functions = _functions.Values
            .Concat(_structOrder.SelectMany(s => s.Methods))
            .Select(BindFunction)
            .ToImmutableArray();

        return _hadError || entryPoint is null ? null : new BoundProgram([.. _structs.Values], [.. _unionOrder, .. _resultOrder], functions, entryPoint);
    }

    // Resolves a function's signature. This happens for every function before any body is bound.
    private UserFunctionSymbol DeclareFunction(FunctionDeclaration declaration, StructTypeSymbol? owner = null)
    {
        _selfType = owner; // so `Self` means the owner in the signature
        var returnType = declaration.ReturnType is { } annotation ? ResolveTypeSyntax(annotation) : TypeSymbol.UnitType;
        var parameters = declaration.Parameters
            .Select(p => new VariableSymbol(p.Name.Text, ResolveParameterType(p.Type), isMutable: false))
            .ToImmutableArray();

        if (owner is null && declaration.Name.Text == ReservedNames.EntryPoint && ((declaration.ReturnType is not null && returnType is not ResultTypeSymbol { Success: null } && returnType != TypeSymbol.ErrorType) || parameters.Length > 0))
        {
            Error(DiagnosticCodes.InvalidEntryPoint, $"'{ReservedNames.EntryPoint}' must have no parameters and return nothing, or 'E{Punctuation.ResultMarker}' for an error type E", declaration.Name.Span);
        }

        if (owner is null && declaration.Receiver is { } receiverSyntax)
        {
            Error(DiagnosticCodes.SelfOutsideMethod, "'&self' and '&mut self' can only be used on methods inside an 'impl' block", receiverSyntax.Span);
        }

        _selfType = null;
        var receiver = declaration.Receiver is null ? ReceiverKind.None : declaration.Receiver.IsMutable ? ReceiverKind.Mutable : ReceiverKind.Shared;
        return new UserFunctionSymbol(declaration, returnType, parameters, owner, receiver);
    }

    // One flat scope per function: the parameters first, then the body's variables. A variable is visible only
    // after its declaration.
    private BoundFunction BindFunction(UserFunctionSymbol function)
    {
        _currentFunction = function;
        _scopes = new ScopeStack();
        _loopDepth = 0;

        // The parameters and the function body share one scope (design.md "Scopes"). `self` is in it too.
        _selfType = function.Owner;
        _scopes.Push();
        if (function.SelfVariable is { } self)
        {
            _scopes.Add(self);
        }

        var declaration = function.Declaration;
        for (var i = 0; i < function.Parameters.Length; i++)
        {
            Declare(function.Parameters[i], declaration.Parameters[i].Name);
        }

        var body = BindStatements(declaration.Body);

        var mustReturn = function.ReturnType != TypeSymbol.UnitType && function.ReturnType != TypeSymbol.ErrorType && function.ReturnType is not ResultTypeSymbol { Success: null };
        if (mustReturn && !BoundFlow.AlwaysReturns(body))
        {
            Error(DiagnosticCodes.MissingReturn, $"'{function.Name}' does not return a value on every path", declaration.Name.Span);
        }

        _selfType = null;
        return new BoundFunction(function, body);
    }

    // A nested block: its own scope, which ends with the block.
    private BoundBlock BindBlock(Block block)
    {
        _scopes.Push();
        var bound = BindStatements(block);
        _scopes.Pop();
        return bound;
    }

    private BoundBlock BindStatements(Block block) =>
        new([.. block.Statements.Select(BindStatement)]);

    // Adds the variable to the current scope, or reports that the name is taken (here, or in a block around this one).
    private void Declare(VariableSymbol variable, Identifier at)
    {
        if (_scopes.InCurrentScope(variable.Name))
        {
            Error(DiagnosticCodes.AlreadyDeclared, $"'{variable.Name}' is already declared in this block", at.Span);
        }
        else if (_scopes.TryGet(variable.Name, out _))
        {
            Error(DiagnosticCodes.AlreadyDeclared, $"'{variable.Name}' is already declared in an enclosing block", at.Span);
        }
        else
        {
            _scopes.Add(variable);
        }
    }

    private BoundStatement BindStatementCore(Statement statement) => statement switch
    {
        ExpressionStatement { Expression: MatchExpression match } => new BoundExpressionStatement(BindMatch(match, isStatement: true)),
        ExpressionStatement s => new BoundExpressionStatement(BindStatementValue(s.Expression)),
        VariableDeclaration d => BindVariableDeclaration(d),
        DestructuringDeclaration d => BindDestructuring(d),
        AssignmentStatement a => BindAssignment(a),
        ReturnStatement r => BindReturn(r),
        IfStatement i => BindIf(i),
        WhileStatement w => BindWhile(w),
        ForStatement f => BindFor(f),
        BreakStatement b => BindLoopJump(b.Span, "break", new BoundBreakStatement()),
        ContinueStatement c => BindLoopJump(c.Span, "continue", new BoundContinueStatement()),
        _ => throw new UnreachableException(),
    };

    private void Error(string code, string message, TextSpan span)
    {
        _hadError = true;
        diagnostics.Report(code, Severity.Error, message, source, span);
    }
}
