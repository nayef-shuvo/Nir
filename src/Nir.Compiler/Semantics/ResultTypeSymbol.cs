using System.Collections.Immutable;

namespace Nir.Compiler.Semantics;

/// <summary>
/// <c>E!T</c>: an error of type <c>E</c> or a value of type <c>T</c>, or <c>E!</c> where success carries nothing. It is a union
/// whose members are <c>[E, T]</c> (just <c>[E]</c> without a value), so <c>match</c> and the conversions of unions work on it.
/// Unlike a declared union it is structural: two <c>E!T</c> with the same <c>E</c> and <c>T</c> are the same type.
/// </summary>
public sealed class ResultTypeSymbol : UnionTypeSymbol
{
    internal ResultTypeSymbol(TypeSymbol error, TypeSymbol? success, int number)
        : base(error.Name + "!" + success?.Name)
    {
        Error = error;
        Success = success;
        ClrName = "Nir.Result" + number;
        Members = success is null ? [error] : [error, success];
    }

    /// <summary>An <see cref="AnyErrorTypeSymbol"/> or a declared error type.</summary>
    public TypeSymbol Error { get; }

    /// <summary>The type of the value, or null when success carries nothing.</summary>
    public TypeSymbol? Success { get; }

    /// <summary>The tag of a success: the error is tag 0.</summary>
    public const int SuccessTag = 1;

    public override string ClrName { get; }

    public override bool Equals(object? obj) => obj is ResultTypeSymbol other && Error == other.Error && Success == other.Success;

    public override int GetHashCode() => HashCode.Combine(Error.GetHashCode(), Success?.GetHashCode());
}
