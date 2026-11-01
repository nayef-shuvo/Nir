using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Syntax;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

public class UnionParserTests
{
    private static (CompilationUnit Unit, DiagnosticBag Diagnostics) Parse(string text)
    {
        var diagnostics = new DiagnosticBag();
        var unit = SyntaxParser.Parse(new SourceText("test.nir", text), diagnostics);
        return (unit, diagnostics);
    }

    private static string Tree(string text)
    {
        var (unit, diagnostics) = Parse(text);
        Assert.Empty(diagnostics);
        return AstFormatter.Format(unit);
    }

    [Fact]
    public void A_union_declaration()
    {
        Assert.Equal(
            """
            CompilationUnit
              Union Shape = Circle | Square
            """,
            Tree("type Shape = union { Circle, Square }"));
    }

    [Fact]
    public void Union_members_may_be_separated_by_newlines_with_a_trailing_separator()
    {
        var one = Tree("type U = union { int, string, (int, int) }");
        var two = Tree("type U = union {\n    int\n    string\n    (int, int)\n}");
        var three = Tree("type U = union {\n    int,\n    string,\n    (int, int),\n}");

        Assert.Equal("CompilationUnit\n  Union U = int | string | (int, int)", one);
        Assert.Equal(one, two);
        Assert.Equal(one, three);
    }

    [Fact]
    public void A_union_sits_beside_structs_and_aliases()
    {
        var tree = Tree("type A = struct { n: int }\ntype U = union { A, int }\ntype P = (int, int)");

        Assert.Contains("Struct A", tree);
        Assert.Contains("Union U = A | int", tree);
        Assert.Contains("TupleAlias P", tree);
    }

    [Fact]
    public void A_match_with_expression_arms()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration f(s: Shape) -> double
                Block
                  Return
                    Match
                      Name s
                      Arm c: Circle
                        Name x
                      Arm q: Square
                        Name y
            """,
            Tree("fn f(s: Shape) -> double {\n    return match s {\n        c: Circle => x,\n        q: Square => y,\n    }\n}"));
    }

    [Fact]
    public void Arms_may_be_separated_by_newlines_and_the_last_separator_is_optional()
    {
        var comma = Tree("fn f() {\n    match s { a: A => x, b: B => y }\n}");
        var lines = Tree("fn f() {\n    match s {\n        a: A => x\n        b: B => y\n    }\n}");
        var trailing = Tree("fn f() {\n    match s {\n        a: A => x,\n        b: B => y,\n    }\n}");

        Assert.Equal(comma, lines);
        Assert.Equal(comma, trailing);
    }

    [Fact]
    public void A_block_arm()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration f
                Block
                  ExpressionStatement
                    Match
                      Name s
                      Arm a: A
                        Block
                          ExpressionStatement
                            CallExpression print
                              StringLiteral "a"
                      Arm _: B
                        CallExpression print
                          StringLiteral "b"
            """,
            Tree("fn f() {\n    match s {\n        a: A => {\n            print(\"a\")\n        }\n        _: B => print(\"b\")\n    }\n}"));
    }

    [Fact]
    public void An_arm_may_name_a_tuple_type()
    {
        Assert.Contains("Arm p: (int, int)", Tree("fn f() {\n    match s { p: (int, int) => 1, n: int => 2 }\n}"));
    }

    [Fact]
    public void A_match_is_an_ordinary_expression()
    {
        var tree = Tree("fn f() {\n    let x = match s { a: A => 1, b: B => 2 } + 3\n}");

        Assert.Contains("Binary +", tree);
        Assert.Contains("Match", tree);
    }

    [Fact]
    public void The_scrutinee_is_not_a_struct_literal_but_arms_may_contain_them()
    {
        var tree = Tree("fn f() {\n    match s { a: A => P { n = 1 }, b: B => P { n = 2 } }\n}");

        Assert.Equal(2, tree.Split("StructLiteral P").Length - 1);
    }

    [Fact]
    public void A_match_inside_an_if_head_still_allows_literals_in_its_arms()
    {
        var tree = Tree("fn f() {\n    if match s { a: A => P { n = 1 }.n, b: B => 2 } == 1 {\n    }\n}");

        Assert.Contains("StructLiteral P", tree);
    }

    [Fact]
    public void Match_and_union_are_reserved_words()
    {
        var (_, diagnostics) = Parse("fn main() {\n    let match = 1\n}");

        Assert.Equal("NIR1001", Assert.Single(diagnostics).Code);
    }

    [Theory]
    [InlineData("type U = union { A }")]
    [InlineData("type U = union { }")]
    [InlineData("type U = union { A, }")]
    [InlineData("fn f() {\n    match s { }\n}")]
    [InlineData("fn f() {\n    match s { a => 1 }\n}")]
    [InlineData("fn f() {\n    match s { a: A 1 }\n}")]
    [InlineData("fn f() {\n    match s { a: A => }\n}")]
    [InlineData("fn f() {\n    match { a: A => 1 }\n}")]
    public void Broken_union_and_match_syntax_reports_one_syntax_error_and_does_not_throw(string text)
    {
        var (_, diagnostics) = Parse(text);

        Assert.Equal("NIR1001", Assert.Single(diagnostics).Code);
    }
}
