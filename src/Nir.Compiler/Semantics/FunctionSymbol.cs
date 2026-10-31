using System.Collections.Immutable;
using System.Reflection;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Semantics;

public abstract class FunctionSymbol(string name, TypeSymbol returnType, ImmutableArray<TypeSymbol> parameterTypes)
{
    public string Name { get; } = name;

    public TypeSymbol ReturnType { get; } = returnType;

    public ImmutableArray<TypeSymbol> ParameterTypes { get; } = parameterTypes;
}

/// <summary>A function implemented by a CLR method: a built-in such as <c>print</c>, or a method of a .NET type.</summary>
public sealed class ClrMethodSymbol(
    string name,
    MethodInfo method,
    TypeSymbol returnType,
    ImmutableArray<TypeSymbol> parameterTypes) : FunctionSymbol(name, returnType, parameterTypes)
{
    public MethodInfo Method { get; } = method;
}

public enum ReceiverKind
{
    /// <summary>A free function or an associated function such as <c>Point::new</c>.</summary>
    None,

    /// <summary><c>&amp;self</c>: the method may read the struct.</summary>
    Shared,

    /// <summary><c>&amp;mut self</c>: the method may read and change the struct.</summary>
    Mutable,
}

/// <summary>
/// A function declared in Nir source: a free function, a method (<paramref name="receiver"/> is not None) or an associated
/// function of a struct (<paramref name="owner"/> set). Its parameters are immutable variables of the function body.
/// </summary>
public sealed class UserFunctionSymbol(
    FunctionDeclaration declaration,
    TypeSymbol returnType,
    ImmutableArray<VariableSymbol> parameters,
    StructTypeSymbol? owner = null,
    ReceiverKind receiver = ReceiverKind.None)
    : FunctionSymbol(declaration.Name.Text, returnType, [.. parameters.Select(p => p.Type)])
{
    public FunctionDeclaration Declaration { get; } = declaration;

    public ImmutableArray<VariableSymbol> Parameters { get; } = parameters;

    /// <summary>The struct this is a method or associated function of; null for a free function.</summary>
    public StructTypeSymbol? Owner { get; } = owner;

    public ReceiverKind Receiver { get; } = owner is null ? ReceiverKind.None : receiver;

    /// <summary>The variable <c>self</c> in a method: a borrow of the owner. Null if there is no receiver.</summary>
    public VariableSymbol? SelfVariable { get; } =
        owner is not null && receiver != ReceiverKind.None
            ? new VariableSymbol("self", owner.BorrowOf(receiver == ReceiverKind.Mutable), isMutable: false)
            : null;
}
