namespace Nir.Compiler.Semantics;

/// <summary>Places: expressions that name a stored value and so have an address (a variable, or a field of a place).</summary>
internal static class BoundPlaces
{
    public static bool IsPlace(BoundExpression expression) => expression switch
    {
        BoundVariableExpression => true,
        BoundFieldAccess access => IsPlace(access.Receiver),
        _ => false,
    };

    /// <summary>The variable a place starts from. Only call this for a place.</summary>
    public static VariableSymbol Root(BoundExpression place) => place switch
    {
        BoundFieldAccess access => Root(access.Receiver),
        BoundVariableExpression variable => variable.Variable,
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    /// <summary>
    /// Whether a place may be changed: a <c>let mut</c> variable, or anything reached through a mutable borrow
    /// (<c>self</c> in a <c>&amp;mut self</c> method, a <c>&amp;mut</c> parameter). Parameters and <c>let</c> are not mutable.
    /// </summary>
    public static bool IsMutable(VariableSymbol root) => root.Type is BorrowTypeSymbol borrow ? borrow.IsMutable : root.IsMutable;
}
