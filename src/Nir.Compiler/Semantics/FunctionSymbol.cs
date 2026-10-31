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

/// <summary>A function declared in Nir source. Its parameters are immutable variables of the function body.</summary>
public sealed class UserFunctionSymbol(FunctionDeclaration declaration, TypeSymbol returnType, ImmutableArray<VariableSymbol> parameters)
    : FunctionSymbol(declaration.Name.Text, returnType, [.. parameters.Select(p => p.Type)])
{
    public FunctionDeclaration Declaration { get; } = declaration;

    public ImmutableArray<VariableSymbol> Parameters { get; } = parameters;
}
