using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>Base of the bound tree: the typed, name-resolved form of the syntax tree that Emit reads.</summary>
public abstract record BoundNode;

public sealed record BoundProgram(ImmutableArray<BoundFunction> Functions, UserFunctionSymbol EntryPoint) : BoundNode;

public sealed record BoundFunction(UserFunctionSymbol Symbol, BoundBlock Body) : BoundNode;

public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements) : BoundNode;

public abstract record BoundStatement : BoundNode;

public sealed record BoundExpressionStatement(BoundExpression Expression) : BoundStatement;

/// <summary><paramref name="Value"/> is null for a bare <c>return</c> in a function that returns <c>()</c>.</summary>
public sealed record BoundReturnStatement(BoundExpression? Value) : BoundStatement;

public sealed record BoundVariableDeclaration(VariableSymbol Variable, BoundExpression Initializer) : BoundStatement;

public sealed record BoundAssignment(VariableSymbol Variable, BoundExpression Value) : BoundStatement;

public abstract record BoundExpression(TypeSymbol Type) : BoundNode;

public sealed record BoundCallExpression(FunctionSymbol Target, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Target.ReturnType);

public sealed record BoundStringLiteral(string Value) : BoundExpression(TypeSymbol.StringType);

public sealed record BoundVariableExpression(VariableSymbol Variable) : BoundExpression(Variable.Type);

public enum BoundBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
}

public enum BoundUnaryOperator
{
    Negate,
}

/// <summary>A binary operation. The operand type (both sides have the same type) tells Emit which instructions to use.</summary>
public sealed record BoundBinaryExpression(BoundBinaryOperator Operator, BoundExpression Left, BoundExpression Right, TypeSymbol ResultType)
    : BoundExpression(ResultType);

public sealed record BoundUnaryExpression(BoundUnaryOperator Operator, BoundExpression Operand)
    : BoundExpression(Operand.Type);

public sealed record BoundIntLiteral(int Value) : BoundExpression(TypeSymbol.IntType);

public sealed record BoundDoubleLiteral(double Value) : BoundExpression(TypeSymbol.DoubleType);

public sealed record BoundBoolLiteral(bool Value) : BoundExpression(TypeSymbol.BoolType);

/// <summary>Stands in for an expression that had an error; its type is <see cref="TypeSymbol.ErrorType"/>.</summary>
public sealed record BoundErrorExpression() : BoundExpression(TypeSymbol.ErrorType);
