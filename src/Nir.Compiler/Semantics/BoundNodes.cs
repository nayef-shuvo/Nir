using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>Base of the bound tree: the typed, name-resolved form of the syntax tree that Emit reads.</summary>
public abstract record BoundNode;

public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, UserFunctionSymbol EntryPoint) : BoundNode;

public sealed record BoundFunction(UserFunctionSymbol Symbol, BoundBlock Body) : BoundNode;

public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements) : BoundNode;

public abstract record BoundStatement : BoundNode;

public sealed record BoundExpressionStatement(BoundExpression Expression) : BoundStatement;

public abstract record BoundExpression(TypeSymbol Type) : BoundNode;

public sealed record BoundCallExpression(FunctionSymbol Target, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Target.ReturnType);

public sealed record BoundStringLiteral(string Value) : BoundExpression(TypeSymbol.StringType);
