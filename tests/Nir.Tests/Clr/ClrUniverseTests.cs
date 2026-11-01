using Nir.Compiler.Clr;
using Nir.Compiler.Semantics;

namespace Nir.Tests.Clr;

public class ClrUniverseTests
{
    [Theory]
    [InlineData("System.Math")]
    [InlineData("System.IO.File")]
    [InlineData("System.Console")]
    [InlineData("System.String")]
    [InlineData("System.Text.StringBuilder")]
    public void Finds_public_framework_types(string fullName)
    {
        var type = ClrUniverse.FindType(fullName);

        Assert.NotNull(type);
        Assert.Equal(fullName, type.FullName);
    }

    [Theory]
    [InlineData("System.Nope")]
    [InlineData("System.IO.Nope")]
    [InlineData("Nope")]
    [InlineData("System.IO")]
    public void Does_not_find_missing_types(string fullName)
    {
        Assert.Null(ClrUniverse.FindType(fullName));
    }

    [Theory]
    [InlineData("System", true)]
    [InlineData("System.IO", true)]
    [InlineData("System.Collections.Generic", true)]
    [InlineData("System.IO.File", false)]
    [InlineData("System.Nope", false)]
    [InlineData("Nope", false)]
    [InlineData("", false)]
    public void Knows_namespaces(string name, bool exists)
    {
        Assert.Equal(exists, ClrUniverse.NamespaceExists(name));
    }

    [Fact]
    public void Types_come_from_the_load_context_not_the_running_runtime()
    {
        var universeString = ClrUniverse.CoreType("System.String");

        Assert.NotSame(typeof(string), universeString);
        Assert.Equal(typeof(string).FullName, universeString.FullName);
        Assert.Same(universeString, ClrUniverse.FindType("System.String"));
    }

    [Theory]
    [InlineData("System.Byte", "byte")]
    [InlineData("System.SByte", "sbyte")]
    [InlineData("System.Int16", "short")]
    [InlineData("System.UInt16", "ushort")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Decimal", "decimal")]
    [InlineData("System.Char", "char")]
    [InlineData("System.Boolean", "bool")]
    [InlineData("System.String", "string")]
    [InlineData("System.Void", "()")]
    public void Maps_the_supported_clr_types(string fullName, string nirName)
    {
        Assert.Equal(nirName, TypeSymbol.FromClr(ClrUniverse.CoreType(fullName))!.Name);
    }

    [Theory]
    [InlineData("System.Object")]
    [InlineData("System.IO.File")]
    [InlineData("System.DateTime")]
    public void Other_clr_types_have_no_nir_type_yet(string fullName)
    {
        Assert.Null(TypeSymbol.FromClr(ClrUniverse.FindType(fullName)!));
    }

    [Fact]
    public void Finds_a_method_with_universe_parameter_types()
    {
        var file = ClrUniverse.FindType("System.IO.File")!;

        var method = file.GetMethod("ReadAllText", [TypeSymbol.StringType.ClrType]);

        Assert.NotNull(method);
        Assert.Equal(TypeSymbol.StringType.ClrType, method.ReturnType);
    }
}
