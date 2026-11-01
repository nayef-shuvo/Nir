using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Syntax;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

public class TupleParserTests
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

    private static string Body(string statements) => Tree("fn main() {\n" + statements + "\n}");

    [Fact]
    public void A_tuple_literal_has_two_or_more_elements()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  VariableDeclaration let t
                    TupleLiteral
                      IntLiteral 1
                      DoubleLiteral 2.5
                      StringLiteral "x"
            """,
            Body("let t = (1, 2.5, \"x\")"));
    }

    [Fact]
    public void A_trailing_comma_is_allowed()
    {
        Assert.Equal(Body("let t = (1, 2)"), Body("let t = (1, 2,)"));
    }

    [Fact]
    public void One_element_in_parentheses_is_still_a_parenthesized_expression()
    {
        Assert.Contains("Parenthesized", Body("let t = (1)"));
        Assert.DoesNotContain("TupleLiteral", Body("let t = (1)"));
    }

    [Fact]
    public void A_tuple_literal_can_span_lines_and_nest()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  VariableDeclaration let t
                    TupleLiteral
                      TupleLiteral
                        IntLiteral 1
                        IntLiteral 2
                      IntLiteral 3
            """,
            Body("let t = (\n    (1, 2),\n    3\n)"));
    }

    [Fact]
    public void Return_and_parameter_types_can_be_tuples()
    {
        var tree = Tree("fn f(a: (int, int), b: (x: int, y: double)) -> (int, string) {\n}");

        Assert.Contains("FunctionDeclaration f(a: (int, int), b: (x: int, y: double)) -> (int, string)", tree);
    }

    [Fact]
    public void A_struct_field_and_a_let_annotation_can_be_tuples()
    {
        var tree = Tree("type S = struct { p: (int, int), n: int }\nfn main() {\n    let t: (int, int) = (1, 2)\n}");

        Assert.Contains("FieldDecl p: (int, int)", tree);
        Assert.Contains("VariableDeclaration let t: (int, int)", tree);
    }

    [Fact]
    public void Tuple_types_nest()
    {
        Assert.Contains("-> ((int, int), string)", Tree("fn f() -> ((int, int), string) {\n}"));
    }

    [Fact]
    public void A_tuple_alias_declaration()
    {
        Assert.Equal(
            """
            CompilationUnit
              TupleAlias Pair = (x: int, y: int)
            """,
            Tree("type Pair = (x: int, y: int)"));
    }

    [Fact]
    public void An_alias_and_a_struct_can_sit_side_by_side()
    {
        var tree = Tree("type A = struct { n: int }\ntype P = (int, int)\ntype B = struct { m: int }");

        Assert.Contains("Struct A", tree);
        Assert.Contains("TupleAlias P = (int, int)", tree);
        Assert.Contains("Struct B", tree);
    }

    [Fact]
    public void Tuple_element_access_by_index()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      FieldAccess 1
                        Name t
            """,
            Body("print(t.1)"));
    }

    [Fact]
    public void Chained_indexes_are_split_even_though_the_lexer_reads_a_decimal_number()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      FieldAccess 1
                        FieldAccess 0
                          Name t
            """,
            Body("print(t.0.1)"));
    }

    [Fact]
    public void Three_indexes_in_a_row_and_a_name_after_an_index()
    {
        var tree = Body("print(t.0.1.2)\nprint(t.0.x)");

        Assert.Contains("FieldAccess 2\n", tree);
        Assert.Contains("FieldAccess x\n", tree);
    }

    [Fact]
    public void Index_spans_cover_exactly_the_digits()
    {
        const string text = "fn main() {\n    print(t.0.1)\n}";
        var (unit, _) = Parse(text);
        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        var outer = (FieldAccessExpression)call.Arguments[0];
        var inner = (FieldAccessExpression)outer.Receiver;

        Assert.Equal("1", text.Substring(outer.Field.Span.Start, outer.Field.Span.Length));
        Assert.Equal("0", text.Substring(inner.Field.Span.Start, inner.Field.Span.Length));
        Assert.Equal("t.0.1", text.Substring(outer.Span.Start, outer.Span.Length));
        Assert.Equal("t.0", text.Substring(inner.Span.Start, inner.Span.Length));
    }

    [Fact]
    public void A_range_after_a_tuple_index_is_not_a_decimal_number()
    {
        var tree = Body("for i in t.0..t.1 {\n}");

        Assert.Contains("For i in ..", tree);
        Assert.Contains("FieldAccess 0", tree);
        Assert.Contains("FieldAccess 1", tree);
    }

    [Fact]
    public void Assignment_to_an_element()
    {
        Assert.Contains("Assignment t.0\n", Body("t.0 = 5"));
        Assert.Contains("Assignment t.0.1\n", Body("t.0.1 = 5"));
        Assert.Contains("Assignment p.pos.1\n", Body("p.pos.1 = 5"));
        Assert.Contains("Assignment t.x\n", Body("t.x = 5"));
    }

    [Fact]
    public void Destructuring()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  Destructuring a, mut b
                    Name t
            """,
            Body("let (a, mut b) = t"));
    }

    [Fact]
    public void Destructuring_with_a_discard_and_a_trailing_comma()
    {
        Assert.Contains("Destructuring _, b, c", Body("let (_, b, c,) = t"));
    }

    [Fact]
    public void A_struct_literal_inside_a_tuple_in_a_condition_needs_no_extra_parentheses()
    {
        var tree = Body("if (P { n = 1 }, 2).1 == 2 {\n}");

        Assert.Contains("StructLiteral P", tree);
        Assert.Contains("TupleLiteral", tree);
    }

    [Theory]
    [InlineData("fn f() -> (int) {\n}")]
    [InlineData("type P = (int)")]
    [InlineData("fn f(a: (int, )) {\n}")]
    [InlineData("fn main() {\n    let (a) = t\n}")]
    [InlineData("fn main() {\n    let (a, b) =\n}")]
    [InlineData("fn main() {\n    let t = (1, )\n}")]
    [InlineData("fn main() {\n    let t = (, 1)\n}")]
    [InlineData("fn f(a: (&int, int)) {\n}")]
    public void Broken_tuple_syntax_reports_one_syntax_error_and_does_not_throw(string text)
    {
        var (_, diagnostics) = Parse(text);

        var error = Assert.Single(diagnostics);
        Assert.Equal("NIR1001", error.Code);
    }
}
