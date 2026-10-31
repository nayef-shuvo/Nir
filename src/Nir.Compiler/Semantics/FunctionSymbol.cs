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

/// <summary>A function provided by the compiler, implemented by a CLR method (for example <c>print</c>).</summary>
public sealed class BuiltinFunctionSymbol(
    string name,
    MethodInfo method,
    TypeSymbol returnType,
    ImmutableArray<TypeSymbol> parameterTypes) : FunctionSymbol(name, returnType, parameterTypes)
{
    public MethodInfo Method { get; } = method;
}

/// <summary>A function declared in Nir source.</summary>
public sealed class UserFunctionSymbol(FunctionDeclaration declaration)
    : FunctionSymbol(declaration.Name.Text, TypeSymbol.UnitType, [])
{
    public FunctionDeclaration Declaration { get; } = declaration;
}
