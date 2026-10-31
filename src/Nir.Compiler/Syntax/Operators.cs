using System.Diagnostics;

namespace Nir.Compiler.Syntax;

public enum BinaryOperator
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

public enum UnaryOperator
{
    Negate,
}

public static class OperatorText
{
    /// <summary>The operator as written in source, for messages.</summary>
    public static string Of(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Remainder => "%",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        _ => throw new UnreachableException(),
    };

    public static string Of(UnaryOperator op) => op switch
    {
        UnaryOperator.Negate => "-",
        _ => throw new UnreachableException(),
    };
}
