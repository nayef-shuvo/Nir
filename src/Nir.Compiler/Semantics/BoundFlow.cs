namespace Nir.Compiler.Semantics;

/// <summary>Questions about how control moves through bound code.</summary>
internal static class BoundFlow
{
    /// <summary>
    /// True if every path through the block ends in a <c>return</c>: its last statement is a return, or an <c>if</c> with an
    /// <c>else</c> whose two blocks both always return. A loop never counts, even <c>while true</c> (design.md "Control flow").
    /// </summary>
    public static bool AlwaysReturns(BoundBlock block) => block.Statements.LastOrDefault() switch
    {
        BoundReturnStatement => true,
        BoundIfStatement { ElseBlock: { } elseBlock } statement => AlwaysReturns(statement.Then) && AlwaysReturns(elseBlock),
        _ => false,
    };
}
