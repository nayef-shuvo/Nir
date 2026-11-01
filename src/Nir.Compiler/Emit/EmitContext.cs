using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Nir.Compiler.Clr;
using Nir.Compiler.Semantics;

namespace Nir.Compiler.Emit;

/// <summary>What the emitter has defined so far, shared by all function bodies: methods, struct types and their fields.</summary>
internal sealed class EmitContext
{
    public Dictionary<UserFunctionSymbol, MethodBuilder> Methods { get; } = [];

    public Dictionary<StructTypeSymbol, TypeBuilder> Structs { get; } = [];

    public Dictionary<FieldSymbol, FieldBuilder> Fields { get; } = [];

    public Dictionary<UnionTypeSymbol, UnionLayout> Unions { get; } = [];

    /// <summary>The class that holds the functions; generated helper methods go here too.</summary>
    public TypeBuilder Host { get; set; } = null!;

    /// <summary>The helper that runs one .NET method inside a CLR try block (see <c>BoundProtectedCall</c>), made on first use.</summary>
    public Dictionary<MethodInfo, MethodBuilder> ProtectedHelpers { get; } = [];

    /// <summary>The class every error derives from (<c>anyerror</c>), defined once per program.</summary>
    public TypeBuilder AnyError { get; set; } = null!;

    public FieldBuilder AnyErrorMessage { get; set; } = null!;

    public ConstructorBuilder AnyErrorConstructor { get; set; } = null!;

    /// <summary>The constructor of each declared error class.</summary>
    public Dictionary<StructTypeSymbol, ConstructorBuilder> ErrorConstructors { get; } = [];

    /// <summary>The CLR type for a Nir type. A struct's is the type the emitter defined for it, not something the symbol knows.</summary>
    public Type ClrOf(TypeSymbol type) => type switch
    {
        StructTypeSymbol structType => Structs[structType],
        UnionTypeSymbol union => Unions[union].Type,
        AnyErrorTypeSymbol => AnyError,
        BorrowTypeSymbol borrow => Structs[borrow.Element].MakeByRefType(), // a borrow is a CLR `ref`
        TupleTypeSymbol tuple => ValueTupleOf(tuple),
        _ => type.ClrType,
    };

    // A tuple is a System.ValueTuple over the CLR types of its elements. One ValueTuple holds seven, so a longer tuple keeps
    // its first seven and puts the rest in a ValueTuple of its own, in the eighth type argument (the `Rest` field), and so on.
    private const int _chunkSize = 7;

    private Type ValueTupleOf(TupleTypeSymbol tuple) => ChunkType([.. tuple.Elements.Select(element => element.Type)]);

    private Type ChunkType(IReadOnlyList<TypeSymbol> types)
    {
        var arguments = types.Take(_chunkSize).Select(ClrOf).ToList();
        if (types.Count > _chunkSize)
        {
            arguments.Add(ChunkType([.. types.Skip(_chunkSize)]));
        }

        return ValueTupleDefinition(arguments.Count, types).MakeGenericType([.. arguments]);
    }

    // A generic type from the load context cannot be instantiated over a type we are still building, but the runtime's own
    // definition can (the result is a TypeBuilderInstantiation). Both name the same type in System.Private.CoreLib.
    private static Type ValueTupleDefinition(int arity, IReadOnlyList<TypeSymbol> types) =>
        types.Any(ContainsStruct) ? RuntimeValueTuple(arity) : LoadedValueTuple(arity);

    private static Type LoadedValueTuple(int arity) =>
        ClrUniverse.FindType("System.ValueTuple`" + arity.ToString(CultureInfo.InvariantCulture))
        ?? throw new InvalidOperationException("System.ValueTuple was not found.");

    private static Type RuntimeValueTuple(int arity) => arity switch
    {
        1 => typeof(ValueTuple<>),
        2 => typeof(ValueTuple<,>),
        3 => typeof(ValueTuple<,,>),
        4 => typeof(ValueTuple<,,,>),
        5 => typeof(ValueTuple<,,,,>),
        6 => typeof(ValueTuple<,,,,,>),
        7 => typeof(ValueTuple<,,,,,,>),
        8 => typeof(ValueTuple<,,,,,,,>),
        _ => throw new InvalidOperationException($"No ValueTuple has {arity} type arguments."),
    };

    /// <summary>
    /// The CLR fields to follow, in order, to reach a struct field or a tuple element. A struct field is one step. An element
    /// past the seventh is reached through the <c>Rest</c> fields first: element 9 of a tuple is <c>Rest.Item2</c>.
    /// </summary>
    public IReadOnlyList<FieldInfo> FieldPath(FieldSymbol field)
    {
        if (field.Owner is AnyErrorTypeSymbol)
        {
            return [AnyErrorMessage];
        }

        if (field.Owner is not TupleTypeSymbol tuple)
        {
            return [Fields[field]];
        }

        var path = new List<FieldInfo>();
        IReadOnlyList<TypeSymbol> types = [.. tuple.Elements.Select(element => element.Type)];
        var index = tuple.Elements.IndexOf(field);
        while (index >= _chunkSize)
        {
            path.Add(ChunkField(types, "Rest"));
            types = [.. types.Skip(_chunkSize)];
            index -= _chunkSize;
        }

        path.Add(ChunkField(types, "Item" + (index + 1).ToString(CultureInfo.InvariantCulture)));
        return path;
    }

    private FieldInfo ChunkField(IReadOnlyList<TypeSymbol> types, string name)
    {
        var constructed = ChunkType(types);
        var definition = ValueTupleDefinition(Math.Min(types.Count, _chunkSize) + (types.Count > _chunkSize ? 1 : 0), types).GetField(name)!;

        // Once a struct we are still building is one of the type arguments, the constructed type is not an ordinary reflection
        // type any more and its members have to be found through TypeBuilder.
        return types.Any(ContainsStruct) ? TypeBuilder.GetField(constructed, definition) : constructed.GetField(name)!;
    }

    /// <summary>The constructor of the ValueTuple that holds the first seven of <paramref name="types"/> (and the rest, if more).</summary>
    public ConstructorInfo ChunkConstructor(IReadOnlyList<TypeSymbol> types)
    {
        var constructed = ChunkType(types);
        if (!types.Any(ContainsStruct))
        {
            return constructed.GetConstructors().Single();
        }

        var arity = Math.Min(types.Count, _chunkSize) + (types.Count > _chunkSize ? 1 : 0);
        return TypeBuilder.GetConstructor(constructed, ValueTupleDefinition(arity, types).GetConstructors().Single());
    }

    private static bool ContainsStruct(TypeSymbol type) => type switch
    {
        StructTypeSymbol or UnionTypeSymbol or AnyErrorTypeSymbol => true,
        TupleTypeSymbol tuple => tuple.Elements.Any(element => ContainsStruct(element.Type)),
        _ => false,
    };
}

/// <summary>A union as the emitter defined it: a value type holding the tag of the current member and one field per member.</summary>
internal sealed record UnionLayout(TypeBuilder Type, FieldBuilder Tag, FieldBuilder[] Cases);
