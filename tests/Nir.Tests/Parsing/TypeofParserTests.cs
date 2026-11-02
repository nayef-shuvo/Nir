using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

/// <summary>Slice 9d: <c>typeof(...)</c> in the syntax tree. A type is tried first, so a plain name comes out as a type; the binder decides.</summary>
public class TypeofParserTests
{
    private static (string Tree, DiagnosticBag Diagnostics) Parse(string statement)
    {
        var diagnostics = new DiagnosticBag();
        var unit = SyntaxParser.Parse(new SourceText("test.nir", "fn main() {\n    " + statement + "\n}\n"), diagnostics);
        return (AstFormatter.Format(unit), diagnostics);
    }

    [Theory]
    [InlineData("let t = typeof(x)", "Typeof x")]
    [InlineData("let t = typeof(int)", "Typeof int")]
    [InlineData("let t = typeof((int, string))", "Typeof (int, string)")]
    [InlineData("let t = typeof((x: int, y: int))", "Typeof (x: int, y: int)")]
    [InlineData("let t = typeof(E!int)", "Typeof E!int")]
    public void A_type_or_a_plain_name_is_read_as_a_type(string statement, string node)
    {
        var (tree, diagnostics) = Parse(statement);

        Assert.Empty(diagnostics);
        Assert.Contains(node, tree);
    }

    [Theory]
    [InlineData("let t = typeof(x + 1)", "Binary")]
    [InlineData("let t = typeof(p.x)", "FieldAccess x")]
    [InlineData("let t = typeof(5)", "IntLiteral 5")]
    [InlineData("let t = typeof(f(1))", "CallExpression f")]
    [InlineData("let t = typeof(self)", "self")]
    [InlineData("let t = typeof((a, 1))", "TupleLiteral")]
    public void Anything_else_is_read_as_an_expression(string statement, string child)
    {
        var (tree, diagnostics) = Parse(statement);

        Assert.Empty(diagnostics);
        Assert.Contains("Typeof", tree);
        Assert.DoesNotContain("Typeof ", tree); // an expression operand is a child node, not a type written on the line
        Assert.Contains(child, tree);
    }

    [Fact]
    public void A_struct_literal_is_allowed_inside_typeof_in_an_if_head()
    {
        var (tree, diagnostics) = Parse("if typeof(P { x = 1 }).name == \"P\" { }");

        Assert.Empty(diagnostics);
        Assert.Contains("StructLiteral P", tree);
    }

    [Fact]
    public void The_result_can_be_used_like_any_value()
    {
        var (tree, diagnostics) = Parse("print(typeof(x).name)");

        Assert.Empty(diagnostics);
        Assert.Contains("FieldAccess name", tree);
        Assert.Contains("Typeof x", tree);
    }

    [Theory]
    [InlineData("let t = typeof()")]
    [InlineData("let t = typeof(x")]
    [InlineData("let t = typeof")]
    public void A_broken_typeof_is_a_syntax_error(string statement)
    {
        var (_, diagnostics) = Parse(statement);

        Assert.Equal([DiagnosticCodes.SyntaxError], diagnostics.Select(d => d.Code).Distinct());
    }
}
