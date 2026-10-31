using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Syntax;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

public class ParserTests
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
    public void Empty_file_parses_to_empty_unit()
    {
        Assert.Equal("CompilationUnit", Tree(""));
    }

    [Fact]
    public void Function_with_empty_body()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
            """,
            Tree("fn main() {}"));
    }

    [Fact]
    public void Call_with_string_argument()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      StringLiteral "hello"
            """,
            Tree("fn main() {\n    print(\"hello\")\n}\n"));
    }

    [Fact]
    public void Two_statements_separated_by_newline()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      StringLiteral "a"
                  ExpressionStatement
                    CallExpression print
                      StringLiteral "b"
            """,
            Tree("fn main() {\n    print(\"a\")\n    print(\"b\")\n}"));
    }

    [Fact]
    public void Two_statements_separated_by_semicolon_on_one_line()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      StringLiteral "a"
                  ExpressionStatement
                    CallExpression print
                      StringLiteral "b"
            """,
            Tree("fn main() { print(\"a\"); print(\"b\") }"));
    }

    [Fact]
    public void Two_functions()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration a
                Block
              FunctionDeclaration b
                Block
            """,
            Tree("fn a() {}\nfn b() {}\n"));
    }

    [Fact]
    public void Call_with_two_arguments_and_trailing_comma()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression f
                      StringLiteral "a"
                      StringLiteral "b"
            """,
            Tree("fn main() { f(\"a\", \"b\",) }"));
    }

    [Fact]
    public void Call_with_arguments_over_multiple_lines()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression f
                      StringLiteral "a"
                      StringLiteral "b"
            """,
            Tree("fn main() {\n    f(\n        \"a\",\n        \"b\"\n    )\n}"));
    }

    [Fact]
    public void Comments_between_statements_are_ignored()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression f
            """,
            Tree("// first\nfn main() {\n    // inside\n    f() // after\n}\n"));
    }

    [Fact]
    public void Spans_cover_whole_node()
    {
        const string text = "fn main() {\n    print(\"hi\")\n}";
        var (unit, _) = Parse(text);

        var function = Assert.Single(unit.Functions);
        Assert.Equal(new TextSpan(0, text.Length), function.Span);
        Assert.Equal(new TextSpan(3, 4), function.Name.Span);

        var statement = Assert.Single(function.Body.Statements);
        var call = Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(statement).Expression);
        Assert.Equal(new TextSpan(16, 11), call.Span);
    }

    [Fact]
    public void Missing_closing_paren_reports_NIR1001_at_expected_position()
    {
        var (_, diagnostics) = Parse("fn main( {\n}");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.SyntaxError, diagnostic.Code);
        Assert.Equal((1, 10), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
    }

    [Fact]
    public void Missing_function_body_reports_NIR1001()
    {
        var (_, diagnostics) = Parse("fn main()");

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SyntaxError);
    }

    [Fact]
    public void Syntax_error_still_returns_a_tree()
    {
        var (unit, diagnostics) = Parse("fn main( {\n}");

        Assert.NotNull(unit);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Lexical_error_is_reported_through_the_parser()
    {
        var (_, diagnostics) = Parse("fn main() { @ }");

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.UnexpectedCharacter);
    }

    [Fact]
    public void Literals_as_arguments()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  ExpressionStatement
                    CallExpression print
                      IntLiteral 10
                      DoubleLiteral 2.5
                      BoolLiteral true
                      BoolLiteral false
            """,
            Tree("fn main() { print(10, 2.5, true, false) }"));
    }

    [Fact]
    public void Literal_spans()
    {
        var (unit, _) = Parse("fn main() { print(2.5) }");

        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(new TextSpan(18, 3), call.Arguments[0].Span);
    }

    // Parses `fn main() { print(<expression>) }` and formats only the expression.
    private static string ExpressionTree(string expression)
    {
        var (unit, diagnostics) = Parse($"fn main() {{ print({expression}) }}");
        Assert.Empty(diagnostics);

        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        return AstFormatter.Format(call.Arguments[0]);
    }

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        Assert.Equal(
            """
            Binary +
              IntLiteral 1
              Binary *
                IntLiteral 2
                IntLiteral 3
            """,
            ExpressionTree("1 + 2 * 3"));
    }

    [Fact]
    public void Subtraction_is_left_associative()
    {
        Assert.Equal(
            """
            Binary -
              Binary -
                IntLiteral 1
                IntLiteral 2
              IntLiteral 3
            """,
            ExpressionTree("1 - 2 - 3"));
    }

    [Fact]
    public void Unary_minus_binds_tighter_than_multiplication()
    {
        Assert.Equal(
            """
            Binary *
              Unary -
                IntLiteral 1
              IntLiteral 2
            """,
            ExpressionTree("-1 * 2"));
    }

    [Fact]
    public void Unary_minus_can_repeat()
    {
        Assert.Equal(
            """
            Unary -
              Unary -
                IntLiteral 1
            """,
            ExpressionTree("- -1"));
    }

    [Fact]
    public void Parentheses_override_precedence()
    {
        Assert.Equal(
            """
            Binary *
              Parenthesized
                Binary +
                  IntLiteral 1
                  IntLiteral 2
              IntLiteral 3
            """,
            ExpressionTree("(1 + 2) * 3"));
    }

    [Fact]
    public void Comparison_binds_tighter_than_equality()
    {
        Assert.Equal(
            """
            Binary ==
              Binary <
                IntLiteral 1
                IntLiteral 2
              BoolLiteral true
            """,
            ExpressionTree("1 < 2 == true"));
    }

    [Fact]
    public void Addition_binds_tighter_than_comparison()
    {
        Assert.Equal(
            """
            Binary <
              Binary +
                IntLiteral 1
                IntLiteral 2
              IntLiteral 4
            """,
            ExpressionTree("1 + 2 < 4"));
    }

    [Fact]
    public void Binary_expression_span_covers_both_operands()
    {
        var (unit, _) = Parse("fn main() { print(1 + 22) }");

        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(new TextSpan(18, 6), call.Arguments[0].Span);
    }

    [Fact]
    public void Call_inside_an_operation()
    {
        Assert.Equal(
            """
            Binary +
              CallExpression f
              IntLiteral 1
            """,
            ExpressionTree("f() + 1"));
    }

    [Fact]
    public void Missing_right_operand_reports_NIR1001()
    {
        var (_, diagnostics) = Parse("fn main() { print(1 + ) }");

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SyntaxError);
    }

    // Parses `fn main() { <statements> }` and formats just the statements.
    private static string StatementTree(string statements)
    {
        var (unit, diagnostics) = Parse($"fn main() {{\n{statements}\n}}");
        Assert.Empty(diagnostics);

        return string.Join("\n", unit.Functions[0].Body.Statements.Select(AstFormatter.Format));
    }

    [Fact]
    public void Immutable_declaration()
    {
        Assert.Equal(
            """
            VariableDeclaration let x
              IntLiteral 1
            """,
            StatementTree("let x = 1"));
    }

    [Fact]
    public void Mutable_declaration_with_annotation()
    {
        Assert.Equal(
            """
            VariableDeclaration let mut y: double
              DoubleLiteral 2.5
            """,
            StatementTree("let mut y: double = 2.5"));
    }

    [Fact]
    public void Assignment_statement()
    {
        Assert.Equal(
            """
            Assignment y
              Binary +
                Name y
                IntLiteral 1
            """,
            StatementTree("y = y + 1"));
    }

    [Fact]
    public void Equality_is_an_expression_not_an_assignment()
    {
        Assert.Equal(
            """
            ExpressionStatement
              Binary ==
                Name x
                IntLiteral 1
            """,
            StatementTree("x == 1"));
    }

    [Fact]
    public void Several_statements_in_order()
    {
        Assert.Equal(
            """
            VariableDeclaration let mut x
              IntLiteral 1
            Assignment x
              IntLiteral 2
            ExpressionStatement
              CallExpression print
                Name x
            """,
            StatementTree("let mut x = 1\nx = 2\nprint(x)"));
    }

    [Fact]
    public void Declaration_spans_cover_the_whole_statement()
    {
        var (unit, _) = Parse("fn main() { let x = 12 }");

        var declaration = (VariableDeclaration)unit.Functions[0].Body.Statements[0];
        Assert.Equal(new TextSpan(12, 10), declaration.Span);
        Assert.Equal(new TextSpan(16, 1), declaration.Name.Span);
    }

    [Theory]
    [InlineData("let x =")]
    [InlineData("let = 1")]
    [InlineData("let x 1")]
    [InlineData("x =")]
    public void Broken_statements_report_NIR1001_and_still_return_a_tree(string statement)
    {
        var (unit, diagnostics) = Parse($"fn main() {{\n{statement}\n}}");

        Assert.NotNull(unit);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SyntaxError);
    }
}
