using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Nir.Compiler.Clr;

/// <summary>
/// The .NET types the compiler knows about, read from the framework assemblies of the running runtime through a
/// <see cref="MetadataLoadContext"/> (no BCL code is executed). One instance serves the whole process.
/// </summary>
/// <remarks>
/// Every CLR type used while emitting must come from here, never from <c>typeof(...)</c>: mixing the two makes the
/// emitted program fail to start (see docs/architecture.md section 9). The load context is not guaranteed to be safe for
/// concurrent use, so callers hold <see cref="Gate"/> while compiling or emitting.
/// </remarks>
internal static class ClrUniverse
{
    /// <summary>Held by <c>NirCompiler.Compile</c> and <c>Emit</c> so only one compilation uses the load context at a time.</summary>
    public static readonly object Gate = new();

    private static readonly Lazy<Universe> _universe = new(() => new Universe(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static Assembly CoreAssembly => _universe.Value.Core;

    /// <summary>A type of the core library that must exist (for example <c>System.Int32</c>).</summary>
    public static Type CoreType(string fullName) =>
        _universe.Value.Core.GetType(fullName) ?? throw new InvalidOperationException($"Core type '{fullName}' was not found.");

    /// <summary>A public top-level type of the framework, or <c>null</c> if there is none with that full name.</summary>
    public static Type? FindType(string fullName) => _universe.Value.FindType(fullName);

    /// <summary>True if some public type of the framework lives in <paramref name="name"/> or in a namespace below it.</summary>
    public static bool NamespaceExists(string name) => _universe.Value.NamespaceExists(name);

    private sealed class Universe : IDisposable
    {
        private readonly MetadataLoadContext _context;
        private readonly Lazy<Index> _index;

        public Universe()
        {
            var paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
            _context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
            _index = new Lazy<Index>(() => Index.Build(paths), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Assembly Core => _context.CoreAssembly ?? throw new InvalidOperationException("The core assembly was not loaded.");

        public Type? FindType(string fullName) =>
            _index.Value.TypeAssemblies.TryGetValue(fullName, out var assemblyName)
                ? _context.LoadFromAssemblyName(assemblyName).GetType(fullName)
                : null;

        public bool NamespaceExists(string name) => _index.Value.Namespaces.Contains(name);

        public void Dispose() => _context.Dispose();
    }

    /// <summary>Which assembly defines each public top-level type, and every namespace. Built from metadata tables only.</summary>
    private sealed class Index
    {
        public Dictionary<string, string> TypeAssemblies { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);

        public static Index Build(string[] paths)
        {
            var index = new Index();
            foreach (var path in paths)
            {
                index.Add(path);
            }

            return index;
        }

        private void Add(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                {
                    return; // a native library
                }

                var reader = pe.GetMetadataReader();
                var assemblyName = Path.GetFileNameWithoutExtension(path);
                foreach (var handle in reader.TypeDefinitions)
                {
                    var definition = reader.GetTypeDefinition(handle);
                    if ((definition.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                    {
                        continue; // not public, or a nested type
                    }

                    var ns = reader.GetString(definition.Namespace);
                    var fullName = ns.Length == 0 ? reader.GetString(definition.Name) : ns + "." + reader.GetString(definition.Name);
                    if (TypeAssemblies.TryAdd(fullName, assemblyName))
                    {
                        AddNamespaceAndParents(ns);
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // Not a managed assembly; nothing to index.
            }
        }

        private void AddNamespaceAndParents(string ns)
        {
            while (ns.Length > 0 && Namespaces.Add(ns))
            {
                var dot = ns.LastIndexOf('.');
                ns = dot < 0 ? string.Empty : ns[..dot];
            }
        }
    }
}
