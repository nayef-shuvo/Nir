using Nir.Compiler.Diagnostics;
using Nir.Compiler.Parsing;
using Nir.Compiler.Syntax;
using Nir.Tests.Support;

namespace Nir.Tests.Parsing;

public class ErrorParserTests
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
    public void An_error_type_declaration()
    {
        Assert.Equal(
            """
            CompilationUnit
              ErrorType ParseError
                FieldDecl message: string
                FieldDecl line: int
            """,
            Tree("type ParseError = error { message: string, line: int }"));
    }

    [Fact]
    public void Result_types_in_signatures()
    {
        var tree = Tree("fn a() -> ParseError!int {\n}\nfn b() -> anyerror! {\n}\nfn c(x: E!(int, int)) -> E!string {\n}");

        Assert.Contains("FunctionDeclaration a -> ParseError!int", tree);
        Assert.Contains("FunctionDeclaration b -> anyerror!", tree);
        Assert.Contains("FunctionDeclaration c(x: E!(int, int)) -> E!string", tree);
    }

    [Fact]
    public void A_result_type_with_no_value_ends_a_struct_field_at_the_newline()
    {
        var tree = Tree("type S = struct {\n    a: E!\n    b: int\n}");

        Assert.Contains("FieldDecl a: E!", tree);
        Assert.Contains("FieldDecl b: int", tree);
    }

    [Fact]
    public void A_result_type_in_a_let_annotation_and_a_union_member()
    {
        var tree = Tree("type U = union { E!int, string }\nfn main() {\n    let r: E!int = x\n}");

        Assert.Contains("Union U = E!int | string", tree);
        Assert.Contains("VariableDeclaration let r: E!int", tree);
    }

    [Fact]
    public void An_error_literal()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  Return
                    ErrorLiteral ParseError
                      Init message
                        StringLiteral "empty"
                      Init line
                        IntLiteral 1
            """,
            Body("return error ParseError { message = \"empty\", line = 1 }"));
    }

    [Fact]
    public void An_error_made_from_a_message()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  Return
                    ErrorMessage
                      StringLiteral "path is empty"
            """,
            Body("return error \"path is empty\""));
    }

    [Fact]
    public void An_error_message_may_be_a_name_or_an_interpolation()
    {
        Assert.Contains("ErrorMessage\n", Body("return error msg"));
        Assert.Contains("ErrorMessage\n", Body("return error $\"bad {x}\""));
    }

    [Fact]
    public void Try()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  VariableDeclaration let x
                    Try
                      CallExpression f
            """,
            Body("let x = try f()"));
    }

    [Fact]
    public void Catch_with_a_fallback()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  VariableDeclaration let x
                    Catch
                      CallExpression f
                      IntLiteral 80
            """,
            Body("let x = f() catch 80"));
    }

    [Fact]
    public void Catch_with_a_handler_expression_and_with_a_block()
    {
        var one = Body("let x = f() catch e => 0");
        var two = Body("let x = f() catch e => {\n    return\n}");

        Assert.Contains("Catch e", one);
        Assert.Contains("Catch e", two);
        Assert.Contains("Return", two);
    }

    [Fact]
    public void Catch_has_the_lowest_precedence()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration main
                Block
                  VariableDeclaration let x
                    Catch
                      Binary +
                        CallExpression f
                        IntLiteral 1
                      Binary +
                        IntLiteral 2
                        IntLiteral 3
            """,
            Body("let x = f() + 1 catch 2 + 3"));
    }

    [Fact]
    public void Try_binds_like_a_unary_operator()
    {
        var tree = Body("let x = try f() + 1");

        Assert.Contains("Binary +\n", tree);
        Assert.Contains("Try\n", tree);
    }

    [Fact]
    public void A_catch_block_may_hold_statements_on_several_lines()
    {
        var tree = Body("let b = load(\"x\") catch e => {\n    print(e.message)\n    return\n}\nprint(b)");

        Assert.Contains("Catch e", tree);
        Assert.Contains("Return", tree);
        Assert.Contains("CallExpression print", tree);
    }

    [Fact]
    public void Error_try_and_catch_are_reserved_words()
    {
        foreach (var word in new[] { "error", "try", "catch" })
        {
            var (_, diagnostics) = Parse($"fn main() {{\n    let {word} = 1\n}}");

            Assert.Equal("NIR1001", Assert.Single(diagnostics).Code);
        }
    }

    [Fact]
    public void A_bang_still_means_not_in_expressions()
    {
        var tree = Body("let a = !b\nlet c = d != e");

        Assert.Contains("Unary !", tree);
        Assert.Contains("Binary !=", tree);
    }

    [Theory]
    [InlineData("fn main() {\n    let x = try\n}")]
    [InlineData("fn main() {\n    let x = f() catch\n}")]
    [InlineData("fn main() {\n    let x = f() catch e =>\n}")]
    [InlineData("fn main() {\n    return error\n}")]
    [InlineData("type E = error { a }")]
    [InlineData("fn f() -> !int {\n}")]
    public void Broken_error_syntax_reports_one_syntax_error_and_does_not_throw(string text)
    {
        var (_, diagnostics) = Parse(text);

        Assert.Equal("NIR1001", Assert.Single(diagnostics).Code);
    }
}
