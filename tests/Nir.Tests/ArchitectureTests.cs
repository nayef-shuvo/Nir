using System.Reflection;
using Nir.Compiler;

namespace Nir.Tests;

/// <summary>
/// Enforces docs/architecture.md section 3 on the public API: which namespace may expose types of which other
/// namespace. In particular ANTLR types never leak out of Nir.Compiler.Parsing.
/// This checks public signatures only, not method bodies.
/// </summary>
public class ArchitectureTests
{
    private const string Root = "Nir.Compiler";

    // namespace -> the Nir.Compiler namespaces its public API may mention (besides itself)
    private static readonly Dictionary<string, string[]> _allowed = new()
    {
        [Root + ".Diagnostics"] = [],
        [Root + ".Clr"] = [],
        [Root + ".Syntax"] = [Root + ".Diagnostics"],
        [Root + ".Parsing"] = [Root + ".Syntax", Root + ".Diagnostics"],
        [Root + ".Semantics"] = [Root + ".Syntax", Root + ".Diagnostics", Root + ".Clr"],
        [Root + ".Emit"] = [Root + ".Semantics", Root + ".Diagnostics", Root + ".Clr"],
    };

    [Fact]
    public void Public_signatures_respect_the_dependency_rules()
    {
        var violations = new List<string>();

        foreach (var type in typeof(NirCompiler).Assembly.GetExportedTypes())
        {
            if (type.Namespace is null || !_allowed.TryGetValue(type.Namespace, out var allowed))
            {
                continue; // the facade (Nir.Compiler) may use everything
            }

            foreach (var (member, used) in ExposedTypes(type))
            {
                var ns = used.Namespace ?? string.Empty;
                var isForbiddenInternal = ns.StartsWith(Root, StringComparison.Ordinal) && ns != type.Namespace && !allowed.Contains(ns);
                var isAntlr = ns.StartsWith("Antlr4", StringComparison.Ordinal) && type.Namespace != Root + ".Parsing";

                if (isForbiddenInternal || isAntlr)
                {
                    violations.Add($"{type.FullName}.{member} exposes {used.FullName}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Antlr_is_used_by_the_Parsing_namespace()
    {
        // Guards the test above against silently checking nothing: the lexer base class does derive from an ANTLR type.
        var baseType = typeof(NirCompiler).Assembly.GetType(Root + ".Parsing.NirLexerBase");

        Assert.NotNull(baseType);
        Assert.StartsWith("Antlr4", baseType.BaseType!.Namespace, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Member, Type Type)> ExposedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
        {
            foreach (var used in Flatten(baseType))
            {
                yield return ("<base type>", used);
            }
        }

        foreach (var @interface in type.GetInterfaces())
        {
            foreach (var used in Flatten(@interface))
            {
                yield return ("<interface>", used);
            }
        }

        foreach (var member in type.GetMembers(flags))
        {
            if (!IsVisible(member))
            {
                continue;
            }

            var types = member switch
            {
                FieldInfo f => [f.FieldType],
                PropertyInfo p => [p.PropertyType, .. p.GetIndexParameters().Select(i => i.ParameterType)],
                MethodBase m => [.. (m as MethodInfo is { } mi ? [mi.ReturnType] : Array.Empty<Type>()), .. m.GetParameters().Select(a => a.ParameterType)],
                EventInfo e when e.EventHandlerType is { } h => [h],
                _ => Array.Empty<Type>(),
            };

            foreach (var used in types.SelectMany(Flatten))
            {
                yield return (member.Name, used);
            }
        }
    }

    private static bool IsVisible(MemberInfo member) => member switch
    {
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
        PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.GetMethod?.IsFamily ?? false) || (p.SetMethod?.IsPublic ?? false),
        EventInfo => true,
        _ => false,
    };

    // A type plus every type it is built from: array elements, generic arguments, by-ref targets.
    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }

            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return inner;
            }
        }
    }
}
