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
}
