namespace Nir.Compiler.Semantics;

/// <summary>
/// The variables visible at one point of a function: a stack of block scopes, innermost last. A name may not be declared if
/// any scope of the function already has it (design.md "Scopes"), so a lookup finds at most one variable.
/// </summary>
internal sealed class ScopeStack
{
    private readonly List<Dictionary<string, VariableSymbol>> _scopes = [];

    public void Push() => _scopes.Add([]);

    public void Pop() => _scopes.RemoveAt(_scopes.Count - 1);

    public bool TryGet(string name, out VariableSymbol variable)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out variable!))
            {
                return true;
            }
        }

        variable = null!;
        return false;
    }

    /// <summary>True if the innermost scope already has the name.</summary>
    public bool InCurrentScope(string name) => _scopes[^1].ContainsKey(name);

    public void Add(VariableSymbol variable) => _scopes[^1].Add(variable.Name, variable);
}
