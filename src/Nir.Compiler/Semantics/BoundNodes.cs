using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>Base of the bound tree: the typed, name-resolved form of the syntax tree that Emit reads.</summary>
public abstract record BoundNode;

public sealed record BoundProgram(ImmutableArray<StructTypeSymbol> Structs, ImmutableArray<UnionTypeSymbol> Unions, ImmutableArray<BoundFunction> Functions, UserFunctionSymbol EntryPoint) : BoundNode;

public sealed record BoundFunction(UserFunctionSymbol Symbol, BoundBlock Body) : BoundNode;

public sealed record BoundBlock(ImmutableArray<BoundStatement> Statements) : BoundNode;

public abstract record BoundStatement : BoundNode;

public sealed record BoundExpressionStatement(BoundExpression Expression) : BoundStatement;

/// <summary><paramref name="Value"/> is null for a bare <c>return</c> in a function that returns <c>()</c>.</summary>
public sealed record BoundReturnStatement(BoundExpression? Value) : BoundStatement;

/// <summary><paramref name="ElseBlock"/> is null when there is no <c>else</c>. An <c>else if</c> is an else block holding one if.</summary>
public sealed record BoundIfStatement(BoundExpression Condition, BoundBlock Then, BoundBlock? ElseBlock) : BoundStatement;

public sealed record BoundWhileStatement(BoundExpression Condition, BoundBlock Body) : BoundStatement;

/// <summary><c>for</c> over an int range. Start and end are evaluated once, before the first pass.</summary>
public sealed record BoundForStatement(VariableSymbol Variable, BoundExpression Start, BoundExpression End, bool IsInclusive, BoundBlock Body)
    : BoundStatement;

public sealed record BoundBreakStatement : BoundStatement;

public sealed record BoundContinueStatement : BoundStatement;

/// <summary><c>target = value</c> where the target is a field: <c>p.x</c>, <c>line.to.x</c>.</summary>
public sealed record BoundFieldAssignment(BoundFieldAccess Target, BoundExpression Value) : BoundStatement;

/// <summary><c>let (a, b) = pair</c>: <paramref name="Variables"/> has one entry per element, <c>null</c> for a <c>_</c>.</summary>
public sealed record BoundDestructuring(ImmutableArray<VariableSymbol?> Variables, BoundExpression Initializer, TupleTypeSymbol Tuple) : BoundStatement;

public sealed record BoundVariableDeclaration(VariableSymbol Variable, BoundExpression Initializer) : BoundStatement;

public sealed record BoundAssignment(VariableSymbol Variable, BoundExpression Value) : BoundStatement;

public abstract record BoundExpression(TypeSymbol Type) : BoundNode;

public sealed record BoundCallExpression(FunctionSymbol Target, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Target.ReturnType);

/// <summary>A method called on a value: <c>receiver.Method(arguments)</c>. The receiver is evaluated first.</summary>
public sealed record BoundMemberCallExpression(BoundExpression Receiver, ClrMethodSymbol Target, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Target.ReturnType);

/// <summary>A struct value built from its field initializers, which run in source order.</summary>
public sealed record BoundStructLiteral(StructTypeSymbol Struct, ImmutableArray<BoundFieldInitializer> Initializers) : BoundExpression(Struct);

public sealed record BoundFieldInitializer(FieldSymbol Field, BoundExpression Value);

/// <summary><c>error E { ... }</c>: a new error object (a class instance) with its fields set in the order written.</summary>
public sealed record BoundErrorLiteral(StructTypeSymbol Error, ImmutableArray<BoundFieldInitializer> Initializers) : BoundExpression(Error);

/// <summary><c>error "text"</c>: a new anyerror with this message.</summary>
public sealed record BoundErrorMessage(BoundExpression Message) : BoundExpression(AnyErrorTypeSymbol.Instance);

/// <summary>A declared error seen as an <c>anyerror</c>. Costs nothing: every error object already is one.</summary>
public sealed record BoundErrorToAny(BoundExpression Value) : BoundExpression(AnyErrorTypeSymbol.Instance);

/// <summary>The success of an <c>E!</c>, which carries no value (a bare <c>return</c>, or the end of the function).</summary>
public sealed record BoundUnitSuccess(ResultTypeSymbol Result) : BoundExpression(Result);

/// <summary>
/// <c>try operand</c>: the value of a success; on an error, returns it from the function, whose result type is
/// <paramref name="FunctionResult"/>. <paramref name="ValueType"/> is <c>()</c> when success carries nothing.
/// </summary>
public sealed record BoundTry(BoundExpression Operand, ResultTypeSymbol FunctionResult, TypeSymbol ValueType) : BoundExpression(ValueType);

/// <summary>
/// <c>operand catch ...</c>: the value of a success; on an error, runs the handler, which is a <paramref name="Value"/> to use or a
/// <paramref name="Body"/> to run. <paramref name="Variable"/> holds the error (null if unused or <c>_</c>).
/// </summary>
public sealed record BoundCatch(BoundExpression Operand, VariableSymbol? Variable, BoundExpression? Value, BoundBlock? Body, TypeSymbol ValueType)
    : BoundExpression(ValueType);

/// <summary>A value of a member type turned into the union: the tag <paramref name="Index"/> and the value.</summary>
public sealed record BoundUnionWrap(BoundExpression Value, UnionTypeSymbol Union, int Index) : BoundExpression(Union);

/// <summary>
/// <c>match</c> on a union. A statement match has <paramref name="Type"/> <c>()</c> and every arm has a <c>Body</c>; a match
/// whose value is used has the common type of its arms and every arm has a <c>Value</c>.
/// </summary>
public sealed record BoundMatch(BoundExpression Scrutinee, UnionTypeSymbol Union, ImmutableArray<BoundMatchArm> Arms, TypeSymbol ResultType)
    : BoundExpression(ResultType);

/// <summary>An arm for the member with tag <paramref name="Index"/>. <paramref name="Variable"/> is null for a <c>_</c>.</summary>
public sealed record BoundMatchArm(int Index, VariableSymbol? Variable, BoundBlock? Body, BoundExpression? Value);

/// <summary>A tuple value built from its elements, which run left to right.</summary>
public sealed record BoundTupleLiteral(TupleTypeSymbol Tuple, ImmutableArray<BoundExpression> Elements) : BoundExpression(Tuple);

/// <summary>Reads a field of a struct value, or names a field as an assignment target.</summary>
public sealed record BoundFieldAccess(BoundExpression Receiver, FieldSymbol Field) : BoundExpression(Field.Type);

/// <summary><c>receiver.method(arguments)</c> on a struct: the receiver is passed by address.</summary>
public sealed record BoundMethodCall(BoundExpression Receiver, UserFunctionSymbol Method, ImmutableArray<BoundExpression> Arguments)
    : BoundExpression(Method.ReturnType);

/// <summary>Reads the struct a borrow points at (a copy of it).</summary>
public sealed record BoundDereference(BoundExpression Borrow, StructTypeSymbol Element) : BoundExpression(Element);

/// <summary><c>&amp;x</c> or <c>&amp;mut x</c> passed as an argument: the address of the variable <paramref name="Place"/>.</summary>
public sealed record BoundBorrowExpression(BoundExpression Place, BorrowTypeSymbol Borrow) : BoundExpression(Borrow);

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
    And,
    Or,
}

public enum BoundUnaryOperator
{
    Negate,
    Not,
}

/// <summary>A binary operation. The operand type (both sides have the same type) tells Emit which instructions to use.</summary>
public sealed record BoundBinaryExpression(BoundBinaryOperator Operator, BoundExpression Left, BoundExpression Right, TypeSymbol ResultType)
    : BoundExpression(ResultType);

public sealed record BoundUnaryExpression(BoundUnaryOperator Operator, BoundExpression Operand)
    : BoundExpression(Operand.Type);

/// <summary>
/// An interpolated string. Every part is already a <c>string</c>: text is a literal and a value of another type has been
/// wrapped in its <c>ToString()</c> call. The parts are joined in order.
/// </summary>
public sealed record BoundInterpolatedString(ImmutableArray<BoundExpression> Parts) : BoundExpression(TypeSymbol.StringType);

/// <summary>A conversion between <c>int</c> and <c>double</c> (<c>operand as target</c>).</summary>
public sealed record BoundConversion(BoundExpression Operand, TypeSymbol Target) : BoundExpression(Target);

public sealed record BoundIntLiteral(int Value) : BoundExpression(TypeSymbol.IntType);

public sealed record BoundDoubleLiteral(double Value) : BoundExpression(TypeSymbol.DoubleType);

public sealed record BoundBoolLiteral(bool Value) : BoundExpression(TypeSymbol.BoolType);

/// <summary>Stands in for an expression that had an error; its type is <see cref="TypeSymbol.ErrorType"/>.</summary>
public sealed record BoundErrorExpression() : BoundExpression(TypeSymbol.ErrorType);
