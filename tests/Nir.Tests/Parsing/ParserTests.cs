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

    [Fact]
    public void Function_with_parameters_and_return_type()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration add(a: int, b: int) -> int
                Block
                  Return
                    Binary +
                      Name a
                      Name b
            """,
            Tree("fn add(a: int, b: int) -> int {\n    return a + b\n}"));
    }

    [Fact]
    public void Parameter_list_may_end_with_a_comma()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration f(a: int, b: bool)
                Block
            """,
            Tree("fn f(a: int, b: bool,) {}"));
    }

    [Fact]
    public void Parameters_may_span_several_lines()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration f(a: int, b: bool)
                Block
            """,
            Tree("fn f(\n    a: int,\n    b: bool\n) {}"));
    }

    [Fact]
    public void Return_without_a_type_means_no_return_type()
    {
        var (unit, _) = Parse("fn f() {}");

        Assert.Null(unit.Functions[0].ReturnType);
        Assert.Empty(unit.Functions[0].Parameters);
    }

    [Fact]
    public void Bare_return_followed_by_a_statement()
    {
        Assert.Equal(
            """
            Return
            ExpressionStatement
              CallExpression print
                IntLiteral 1
            """,
            StatementTree("return\nprint(1)"));
    }

    [Fact]
    public void Return_with_a_value()
    {
        Assert.Equal(
            """
            Return
              Binary *
                IntLiteral 2
                Name x
            """,
            StatementTree("return 2 * x"));
    }

    [Fact]
    public void Parameter_and_return_spans()
    {
        const string text = "fn f(a: int) -> int { return a }";
        var (unit, _) = Parse(text);

        var function = unit.Functions[0];
        Assert.Equal(new TextSpan(5, 6), function.Parameters[0].Span);
        Assert.Equal(new TextSpan(5, 1), function.Parameters[0].Name.Span);
        Assert.Equal(new TextSpan(8, 3), function.Parameters[0].Type.Span);
        Assert.Equal("int", function.ReturnType!.Text);
        Assert.Equal(new TextSpan(22, 8), function.Body.Statements[0].Span);
    }

    [Theory]
    [InlineData("fn f(a) {}")]
    [InlineData("fn f(a: ) {}")]
    [InlineData("fn f(: int) {}")]
    [InlineData("fn f() -> {}")]
    [InlineData("fn f(a: int b: int) {}")]
    public void Broken_signatures_report_NIR1001_and_still_return_a_tree(string source)
    {
        var (unit, diagnostics) = Parse(source);

        Assert.NotNull(unit);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SyntaxError);
    }

    [Fact]
    public void Plain_import()
    {
        Assert.Equal(
            """
            CompilationUnit
              Using clr::cs::System::IO
              FunctionDeclaration main
                Block
            """,
            Tree("using clr::cs::System::IO\nfn main() {}"));
    }

    [Fact]
    public void Group_import()
    {
        Assert.Equal(
            """
            CompilationUnit
              Using clr::cs::System::{Math, Console}
            """,
            Tree("using clr::cs::System::{Math, Console}"));
    }

    [Fact]
    public void Group_import_over_several_lines_with_a_trailing_comma()
    {
        Assert.Equal(
            """
            CompilationUnit
              Using clr::cs::System::{Math, Console}
              FunctionDeclaration main
                Block
            """,
            Tree("using clr::cs::System::{\n    Math,\n    Console,\n}\nfn main() {}"));
    }

    [Fact]
    public void Imports_may_come_after_functions()
    {
        Assert.Equal(
            """
            CompilationUnit
              Using clr::cs::System::Math
              FunctionDeclaration main
                Block
            """,
            Tree("fn main() {}\nusing clr::cs::System::Math"));
    }

    [Fact]
    public void Several_imports()
    {
        var (unit, diagnostics) = Parse("using clr::cs::System::Math\nusing clr::cs::System::IO\n");

        Assert.Empty(diagnostics);
        Assert.Equal(2, unit.Usings.Length);
        Assert.Equal(["clr", "cs", "System", "IO"], unit.Usings[1].Path.Select(p => p.Text));
    }

    [Fact]
    public void Using_directive_spans()
    {
        var (unit, _) = Parse("using clr::cs::System::{Math}");

        var directive = unit.Usings[0];
        Assert.Equal(new TextSpan(0, 29), directive.Span);
        Assert.Equal(new TextSpan(24, 4), directive.Members[0].Span);
    }

    [Fact]
    public void Qualified_call()
    {
        Assert.Equal(
            """
            CallExpression IO::File::ReadAllText
              Name p
            """,
            ExpressionTree("IO::File::ReadAllText(p)"));
    }

    [Fact]
    public void Qualified_call_span_and_parts()
    {
        var (unit, _) = Parse("fn main() { A::B::C(1) }");

        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(["A", "B", "C"], call.Callee.Parts.Select(p => p.Text));
        Assert.Equal(new TextSpan(12, 7), call.Callee.Span);
        Assert.Equal(new TextSpan(12, 10), call.Span);
    }

    [Fact]
    public void Member_call_on_a_string()
    {
        Assert.Equal(
            """
            MemberCall ToUpper
              StringLiteral "a"
            """,
            ExpressionTree("\"a\".ToUpper()"));
    }

    [Fact]
    public void Member_calls_chain_from_left_to_right()
    {
        Assert.Equal(
            """
            MemberCall ToUpper
              MemberCall Trim
                StringLiteral "a"
            """,
            ExpressionTree("\"a\".Trim().ToUpper()"));
    }

    [Fact]
    public void Member_call_with_arguments()
    {
        Assert.Equal(
            """
            MemberCall Replace
              Name s
              StringLiteral "a"
              StringLiteral "b"
            """,
            ExpressionTree("s.Replace(\"a\", \"b\")"));
    }

    [Fact]
    public void Member_call_binds_tighter_than_unary_minus()
    {
        Assert.Equal(
            """
            Unary -
              MemberCall F
                Name x
            """,
            ExpressionTree("-x.F()"));
    }

    [Fact]
    public void Member_call_inside_an_operation()
    {
        Assert.Equal(
            """
            Binary +
              MemberCall F
                Name a
              IntLiteral 1
            """,
            ExpressionTree("a.F() + 1"));
    }

    [Fact]
    public void Member_call_on_numbers_and_on_a_call_result()
    {
        Assert.Equal("MemberCall ToString\n  IntLiteral 42", ExpressionTree("42.ToString()"));
        Assert.Equal("MemberCall ToString\n  DoubleLiteral 2.5", ExpressionTree("2.5.ToString()"));
        Assert.Equal("MemberCall G\n  CallExpression f", ExpressionTree("f().G()"));
    }

    [Fact]
    public void Member_call_span_covers_receiver_and_arguments()
    {
        var (unit, _) = Parse("fn main() { print(\"a\".Trim()) }");

        var print = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(new TextSpan(18, 10), print.Arguments[0].Span);
    }

    [Theory]
    [InlineData("using")]
    [InlineData("using a::")]
    [InlineData("using a::{}")]
    [InlineData("using a::{b")]
    [InlineData("using ::a")]
    [InlineData("fn main() { x. }")]
    [InlineData("fn main() { x.F( }")]
    [InlineData("fn main() { a::() }")]
    [InlineData("fn main() { a::b }")]
    public void Broken_imports_and_calls_report_NIR1001_and_still_return_a_tree(string source)
    {
        var (unit, diagnostics) = Parse(source);

        Assert.NotNull(unit);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SyntaxError);
    }

    [Fact]
    public void Only_the_first_syntax_error_is_reported()
    {
        var (_, diagnostics) = Parse("fn a( {\n    print(\"x\")\n}\nfn b( {\n}\n");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((1, 7), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
    }

    [Fact]
    public void A_lexical_error_and_the_parse_error_after_it_give_one_diagnostic()
    {
        var (_, diagnostics) = Parse("fn main() {\n    let x = 5 @ 3\n}");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnexpectedCharacter, diagnostic.Code);
    }

    [Fact]
    public void An_unterminated_string_gives_one_diagnostic()
    {
        var (_, diagnostics) = Parse("fn main() {\n    print(\"never ends)\n}");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnterminatedString, diagnostic.Code);
    }

    [Fact]
    public void The_earliest_problem_wins_even_if_a_later_one_is_found_first()
    {
        // The parse error at `)` comes before the stray `@`, though the lexer may reach the `@` first.
        var (_, diagnostics) = Parse("fn main() { print(1 + ) @ }");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.SyntaxError, diagnostic.Code);
    }

    [Fact]
    public void If_without_else()
    {
        Assert.Equal(
            """
            If
              Binary >
                Name x
                IntLiteral 0
              Block
                ExpressionStatement
                  CallExpression print
                    IntLiteral 1
            """,
            StatementTree("if x > 0 {\n    print(1)\n}"));
    }

    [Fact]
    public void If_with_else()
    {
        Assert.Equal(
            """
            If
              Name flag
              Block
                Return
              Else
                Block
                  Break
            """,
            StatementTree("if flag {\n    return\n} else {\n    break\n}"));
    }

    [Fact]
    public void Else_if_chain()
    {
        Assert.Equal(
            """
            If
              Name a
              Block
              Else
                If
                  Name b
                  Block
                  Else
                    Block
            """,
            StatementTree("if a {\n} else if b {\n} else {\n}"));
    }

    [Fact]
    public void Else_may_start_on_the_next_line()
    {
        Assert.Equal(StatementTree("if a {\n} else {\n}"), StatementTree("if a {\n}\nelse {\n}"));
    }

    [Fact]
    public void A_statement_after_an_if_is_not_swallowed_as_its_else()
    {
        Assert.Equal(
            """
            If
              Name a
              Block
            ExpressionStatement
              CallExpression print
                IntLiteral 1
            """,
            StatementTree("if a {\n}\nprint(1)"));
    }

    [Fact]
    public void Parenthesised_condition_is_just_an_expression()
    {
        Assert.Equal(
            """
            If
              Parenthesized
                Binary ==
                  Name a
                  Name b
              Block
            """,
            StatementTree("if (a == b) {\n}"));
    }

    [Fact]
    public void While_loop()
    {
        Assert.Equal(
            """
            While
              Binary !=
                Name n
                IntLiteral 1
              Block
                Continue
            """,
            StatementTree("while n != 1 {\n    continue\n}"));
    }

    [Theory]
    [InlineData("..", "..")]
    [InlineData("..=", "..=")]
    public void For_loop_with_exclusive_and_inclusive_ranges(string op, string shown)
    {
        Assert.Equal(
            $"""
            For i in {shown}
              IntLiteral 0
              Binary +
                Name n
                IntLiteral 1
              Block
            """,
            StatementTree($"for i in 0{op}n + 1 {{\n}}"));
    }

    [Fact]
    public void Break_and_continue()
    {
        Assert.Equal("Break\nContinue", StatementTree("break\ncontinue"));
    }

    [Fact]
    public void Not_binds_like_unary_minus()
    {
        Assert.Equal(
            """
            Binary &&
              Unary !
                Name a
              Name b
            """,
            ExpressionTree("!a && b"));
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        Assert.Equal(
            """
            Binary ||
              Name a
              Binary &&
                Name b
                Name c
            """,
            ExpressionTree("a || b && c"));
    }

    [Fact]
    public void Comparison_and_equality_bind_tighter_than_and()
    {
        Assert.Equal(
            """
            Binary &&
              Binary <
                Name a
                IntLiteral 1
              Binary ==
                Name b
                Name c
            """,
            ExpressionTree("a < 1 && b == c"));
    }

    [Fact]
    public void Logical_operators_are_left_associative()
    {
        Assert.Equal(
            """
            Binary ||
              Binary ||
                Name a
                Name b
              Name c
            """,
            ExpressionTree("a || b || c"));
    }

    [Fact]
    public void Control_flow_spans()
    {
        const string text = "fn main() { if a { } else { } }";
        var (unit, _) = Parse(text);

        var statement = (IfStatement)unit.Functions[0].Body.Statements[0];
        Assert.Equal(new TextSpan(12, 17), statement.Span);
        Assert.Equal(new TextSpan(17, 3), statement.Then.Span);
    }

    [Theory]
    [InlineData("fn main() { if { } }")]
    [InlineData("fn main() { if x }")]
    [InlineData("fn main() { if x { } else }")]
    [InlineData("fn main() { else { } }")]
    [InlineData("fn main() { while }")]
    [InlineData("fn main() { while x }")]
    [InlineData("fn main() { for i 0..3 { } }")]
    [InlineData("fn main() { for i in 0.. { } }")]
    [InlineData("fn main() { for i in 0..3 }")]
    [InlineData("fn main() { for in 0..3 { } }")]
    [InlineData("fn main() { x && }")]
    [InlineData("fn main() { ! }")]
    public void Broken_control_flow_reports_NIR1001_and_still_returns_a_tree(string source)
    {
        var (unit, diagnostics) = Parse(source);

        Assert.NotNull(unit);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.SyntaxError, diagnostic.Code);
    }

    [Fact]
    public void Interpolated_string_with_text_and_holes()
    {
        Assert.Equal(
            """
            Interpolated
              Text "a "
              Hole
                Name x
              Text " b"
            """,
            ExpressionTree("$\"a {x} b\""));
    }

    [Fact]
    public void Empty_interpolated_string()
    {
        Assert.Equal("Interpolated", ExpressionTree("$\"\""));
    }

    [Fact]
    public void Holes_hold_full_expressions()
    {
        Assert.Equal(
            """
            Interpolated
              Hole
                Binary +
                  Name a
                  IntLiteral 1
              Text "-"
              Hole
                CallExpression f
                  StringLiteral "x"
              Hole
                MemberCall ToUpper
                  Name s
            """,
            ExpressionTree("$\"{a + 1}-{f(\"x\")}{s.ToUpper()}\""));
    }

    [Fact]
    public void Interpolated_strings_nest()
    {
        Assert.Equal(
            """
            Interpolated
              Text "["
              Hole
                Interpolated
                  Hole
                    Name x
              Text "]"
            """,
            ExpressionTree("$\"[{$\"{x}\"}]\""));
    }

    [Fact]
    public void Doubled_braces_become_single_braces_in_the_text()
    {
        var (unit, diagnostics) = Parse("fn main() { print($\"{{x}} {y}\") }");

        Assert.Empty(diagnostics);
        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        var interpolated = (InterpolatedStringExpression)call.Arguments[0];
        Assert.Equal("{x} ", ((InterpolatedText)interpolated.Parts[0]).Text);
        Assert.IsType<InterpolatedHole>(interpolated.Parts[1]);
    }

    [Fact]
    public void Escapes_are_decoded_in_plain_and_interpolated_strings()
    {
        var (unit, diagnostics) = Parse("fn main() { print(\"a\\tb\\n\", \"say \\\"hi\\\"\", $\"x\\ty\") }");

        Assert.Empty(diagnostics);
        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal("a\tb\n", ((StringLiteralExpression)call.Arguments[0]).Value);
        Assert.Equal("say \"hi\"", ((StringLiteralExpression)call.Arguments[1]).Value);
        Assert.Equal("x\ty", ((InterpolatedText)((InterpolatedStringExpression)call.Arguments[2]).Parts[0]).Text);
    }

    [Fact]
    public void Interpolated_string_span_covers_the_dollar_and_both_quotes()
    {
        var (unit, _) = Parse("fn main() { print($\"a{x}\") }");

        var call = (CallExpression)((ExpressionStatement)unit.Functions[0].Body.Statements[0]).Expression;
        Assert.Equal(new TextSpan(18, 7), call.Arguments[0].Span);
    }

    [Theory]
    [InlineData("print(\"a\\qb\")", 21)]
    [InlineData("print($\"a\\qb\")", 22)]
    public void An_unknown_escape_reports_NIR0003_at_the_backslash(string statement, int column)
    {
        var (_, diagnostics) = Parse($"fn main() {{ {statement} }}");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownEscape, diagnostic.Code);
        Assert.Equal("unknown escape sequence '\\q'", diagnostic.Message);
        Assert.Equal((1, column), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
        Assert.Equal(2, diagnostic.Span.Length);
    }

    [Fact]
    public void Only_the_first_of_two_unknown_escapes_is_reported()
    {
        var (_, diagnostics) = Parse("fn main() { print(\"\\q\\z\") }");

        Assert.Equal("unknown escape sequence '\\q'", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void An_unknown_escape_before_a_syntax_error_wins()
    {
        var (_, diagnostics) = Parse("fn main() { print(\"\\q\") }\nfn broken( {");

        Assert.Equal(DiagnosticCodes.UnknownEscape, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void A_nested_string_cut_short_reports_the_outer_string_once()
    {
        var (_, diagnostics) = Parse("fn main() {\n    print($\"a {f(\"b\n}");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnterminatedString, diagnostic.Code);
        Assert.Equal((2, 11), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
    }

    [Fact]
    public void Cast_expression()
    {
        Assert.Equal(
            """
            Cast double
              Name x
            """,
            ExpressionTree("x as double"));
    }

    [Fact]
    public void Unary_minus_binds_tighter_than_as()
    {
        Assert.Equal(
            """
            Cast double
              Unary -
                Name x
            """,
            ExpressionTree("-x as double"));
    }

    [Fact]
    public void As_binds_tighter_than_addition_and_multiplication()
    {
        Assert.Equal(
            """
            Binary +
              Name a
              Cast double
                Name b
            """,
            ExpressionTree("a + b as double"));
        Assert.Equal(
            """
            Binary *
              Cast double
                IntLiteral 1
              DoubleLiteral 2.0
            """,
            ExpressionTree("1 as double * 2.0"));
    }

    [Fact]
    public void Casts_chain_to_the_left_and_apply_to_calls()
    {
        Assert.Equal(
            """
            Cast int
              Cast double
                Name x
            """,
            ExpressionTree("x as double as int"));
        Assert.Equal(
            """
            Cast int
              MemberCall F
                Name x
            """,
            ExpressionTree("x.F() as int"));
    }

    [Fact]
    public void A_cast_inside_parentheses_and_in_a_comparison()
    {
        Assert.Equal(
            """
            Binary <
              Cast double
                Name n
              Parenthesized
                Binary /
                  DoubleLiteral 1.0
                  DoubleLiteral 2.0
            """,
            ExpressionTree("n as double < (1.0 / 2.0)"));
    }

    [Theory]
    [InlineData("fn main() { x as }")]
    [InlineData("fn main() { x as 5 }")]
    [InlineData("fn main() { as int }")]
    [InlineData("fn main() { print($\"{}\") }")]
    [InlineData("fn main() { print($\"{x\") }")]
    [InlineData("fn main() { print($\"{x +}\") }")]
    public void Broken_casts_and_holes_report_one_diagnostic_and_still_return_a_tree(string source)
    {
        var (unit, diagnostics) = Parse(source);

        Assert.NotNull(unit);
        Assert.Single(diagnostics);
    }

    // ---- structs, impl, receivers, borrows ----

    [Fact]
    public void Struct_with_comma_separated_fields()
    {
        Assert.Equal(
            """
            CompilationUnit
              Struct Point
                FieldDecl x: int
                FieldDecl y: int
            """,
            Tree("type Point = struct { x: int, y: int }"));
    }

    [Theory]
    [InlineData("type Point = struct {\n    x: int,\n    y: int,\n}")]
    [InlineData("type Point = struct {\n    x: int\n    y: int\n}")]
    [InlineData("type Point = struct {\n    x: int,\n    y: int\n}")]
    [InlineData("type Point = struct { x: int; y: int }")]
    public void Struct_fields_may_be_separated_by_commas_or_newlines(string source)
    {
        Assert.Equal(Tree("type Point = struct { x: int, y: int }"), Tree(source));
    }

    [Fact]
    public void Empty_struct()
    {
        Assert.Equal("CompilationUnit\n  Struct Empty", Tree("type Empty = struct {}"));
        Assert.Equal("CompilationUnit\n  Struct Empty", Tree("type Empty = struct {\n}"));
    }

    [Fact]
    public void Impl_block_with_methods_and_receivers()
    {
        Assert.Equal(
            """
            CompilationUnit
              Impl Point
                FunctionDeclaration new(x: int, y: int) -> Self
                  Block
                    Return
                      StructLiteral Self
                        Init x
                          Name x
                        Init y
                          Name y
                FunctionDeclaration length(&self) -> int
                  Block
                    Return
                      FieldAccess x
                        self
                FunctionDeclaration move(&mut self, dx: int)
                  Block
                    Assignment self.x
                      Binary +
                        FieldAccess x
                          self
                        Name dx
            """,
            Tree(
                "Point impl {\n" +
                "    fn new(x: int, y: int) -> Self {\n        return Self { x = x, y = y }\n    }\n" +
                "    fn length(&self) -> int {\n        return self.x\n    }\n" +
                "    fn move(&mut self, dx: int) {\n        self.x = self.x + dx\n    }\n" +
                "}"));
    }

    [Fact]
    public void Empty_impl_and_impl_on_one_line()
    {
        Assert.Equal("CompilationUnit\n  Impl P", Tree("P impl {}"));
        Assert.Equal(
            """
            CompilationUnit
              Impl P
                FunctionDeclaration f
                  Block
            """,
            Tree("P impl { fn f() {} }"));
    }

    [Fact]
    public void Borrow_parameters()
    {
        Assert.Equal(
            """
            CompilationUnit
              FunctionDeclaration bump(p: &mut Point, q: &Point, n: int)
                Block
            """,
            Tree("fn bump(p: &mut Point, q: &Point, n: int) {}"));
    }

    [Fact]
    public void Struct_literal_on_one_line_and_over_several_lines()
    {
        var expected =
            """
            StructLiteral Point
              Init x
                IntLiteral 1
              Init y
                IntLiteral 2
            """;

        Assert.Equal(expected, ExpressionTree("Point { x = 1, y = 2 }"));
        Assert.Equal(expected, ExpressionTree("Point {\n        x = 1,\n        y = 2,\n    }"));
        Assert.Equal("StructLiteral Point", ExpressionTree("Point {}"));
    }

    [Fact]
    public void Struct_literal_fields_may_be_separated_by_newlines_outside_parentheses()
    {
        // Inside parentheses a newline is whitespace, so there a comma is needed between fields; as a statement it is not.
        Assert.Equal(
            """
            VariableDeclaration let v
              StructLiteral Point
                Init x
                  IntLiteral 1
                Init y
                  IntLiteral 2
            """,
            StatementTree("let v = Point {\n    x = 1\n    y = 2\n}"));
    }

    [Fact]
    public void Newline_separated_fields_inside_parentheses_need_a_comma()
    {
        var (_, diagnostics) = Parse("fn main() { print(Point {\n    x = 1\n    y = 2\n}) }");

        Assert.Equal(DiagnosticCodes.SyntaxError, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void Struct_literals_nest_and_appear_in_let_and_return()
    {
        Assert.Equal(
            """
            VariableDeclaration let line
              StructLiteral Line
                Init from
                  StructLiteral Point
                    Init x
                      IntLiteral 0
                Init to
                  Name p
            Return
              StructLiteral Point
            """,
            StatementTree("let line = Line { from = Point { x = 0 }, to = p }\nreturn Point {}"));
    }

    [Fact]
    public void Field_access_chains_to_the_left()
    {
        Assert.Equal(
            """
            FieldAccess c
              FieldAccess b
                Name a
            """,
            ExpressionTree("a.b.c"));
        Assert.Equal(
            """
            MemberCall F
              FieldAccess b
                Name a
            """,
            ExpressionTree("a.b.F()"));
        Assert.Equal(
            """
            FieldAccess x
              CallExpression f
            """,
            ExpressionTree("f().x"));
    }

    [Fact]
    public void Field_access_binds_tighter_than_unary_minus_and_cast()
    {
        Assert.Equal(
            """
            Cast double
              Unary -
                FieldAccess x
                  Name p
            """,
            ExpressionTree("-p.x as double"));
    }

    [Fact]
    public void References_and_self()
    {
        Assert.Equal("Ref\n  Name x", ExpressionTree("&x"));
        Assert.Equal("RefMut\n  Name x", ExpressionTree("&mut x"));
        Assert.Equal("RefMut\n  self", ExpressionTree("&mut self"));
        Assert.Equal("FieldAccess x\n  self", ExpressionTree("self.x"));
        Assert.Equal(
            """
            CallExpression bump
              RefMut
                Name p
              Ref
                Name q
            """,
            ExpressionTree("bump(&mut p, &q)"));
    }

    [Fact]
    public void Logical_and_still_lexes_as_one_token_next_to_a_reference()
    {
        Assert.Equal(
            """
            Binary &&
              Name a
              Name b
            """,
            ExpressionTree("a && b"));
    }

    [Fact]
    public void Assignment_targets_may_be_field_paths()
    {
        Assert.Equal("Assignment p.x\n  IntLiteral 1", StatementTree("p.x = 1"));
        Assert.Equal("Assignment a.b.c\n  IntLiteral 1", StatementTree("a.b.c = 1"));
        Assert.Equal("Assignment self.pos.x\n  IntLiteral 2", StatementTree("self.pos.x = 2"));
        Assert.Equal("Assignment x\n  IntLiteral 1", StatementTree("x = 1"));
    }

    [Fact]
    public void Field_comparison_is_an_expression_not_an_assignment()
    {
        Assert.Equal(
            """
            ExpressionStatement
              Binary ==
                FieldAccess x
                  Name p
                IntLiteral 1
            """,
            StatementTree("p.x == 1"));
    }

    // ---- struct literals in the head of if / while / for ----

    [Fact]
    public void A_name_before_the_block_of_a_while_is_not_a_struct_literal()
    {
        Assert.Equal(
            """
            While
              Binary !=
                Name n
                Name limit
              Block
                Assignment n
                  Binary +
                    Name n
                    IntLiteral 1
            """,
            StatementTree("while n != limit { n = n + 1 }"));
    }

    [Theory]
    [InlineData("if x == y { }")]
    [InlineData("if flag { }")]
    [InlineData("for i in 0..n { }")]
    [InlineData("for i in a..=b { }")]
    [InlineData("while n < limit { break }")]
    public void Heads_ending_in_a_name_parse_as_blocks(string statement)
    {
        var (unit, diagnostics) = Parse($"fn main() {{\n{statement}\n}}");

        Assert.Empty(diagnostics);
        Assert.Single(unit.Functions[0].Body.Statements);
    }

    [Fact]
    public void A_parenthesised_struct_literal_is_allowed_in_a_head()
    {
        Assert.Equal(
            """
            If
              Binary ==
                Parenthesized
                  StructLiteral P
                    Init x
                      IntLiteral 1
                Name q
              Block
            """,
            StatementTree("if (P { x = 1 }) == q { }"));
    }

    [Fact]
    public void A_struct_literal_is_allowed_in_call_arguments_and_holes_inside_a_head()
    {
        var (_, diagnostics) = Parse("fn main() {\n    if same(P { x = 1 }, q) { }\n    while $\"{P { x = 1 }}\" == s { }\n}");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_bare_struct_literal_in_a_head_is_a_syntax_error()
    {
        var (_, diagnostics) = Parse("fn main() {\n    if P { x = 1 } == q { }\n}");

        Assert.Equal(DiagnosticCodes.SyntaxError, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void Struct_literals_are_allowed_again_inside_the_body_of_a_loop()
    {
        var (unit, diagnostics) = Parse("fn main() {\n    while a < b {\n        let p = P { x = 1 }\n    }\n    let q = P { x = 2 }\n}");

        Assert.Empty(diagnostics);
        Assert.Equal(2, unit.Functions[0].Body.Statements.Length);
    }

    [Fact]
    public void Nested_heads_keep_the_rule()
    {
        var (_, diagnostics) = Parse("fn main() {\n    if a {\n        while n != limit { n = n + 1 }\n    }\n}");

        Assert.Empty(diagnostics);
    }

    // ---- spans and errors ----

    [Fact]
    public void Struct_and_literal_spans()
    {
        const string text = "type P = struct { x: int }\nfn main() { let a = P { x = 1 } }";
        var (unit, _) = Parse(text);

        Assert.Equal(new TextSpan(0, 26), unit.Structs[0].Span);
        Assert.Equal(new TextSpan(18, 6), unit.Structs[0].Fields[0].Span);
        var declaration = (VariableDeclaration)unit.Functions[0].Body.Statements[0];
        Assert.Equal(new TextSpan(47, 11), declaration.Initializer.Span);
    }

    [Theory]
    [InlineData("type = struct { }")]
    [InlineData("type P struct { }")]
    [InlineData("type P = struct { x }")]
    [InlineData("type P = struct { x: }")]
    [InlineData("type P = struct { x: int y }")]
    [InlineData("P impl")]
    [InlineData("P impl { fn }")]
    [InlineData("P impl { 5 }")]
    [InlineData("fn f(self) { }")]
    [InlineData("fn f(&) { }")]
    [InlineData("fn f(&mut) { }")]
    [InlineData("fn f(p: &) { }")]
    [InlineData("fn f(p: &mut) { }")]
    [InlineData("fn main() { let a = P { x = } }")]
    [InlineData("fn main() { let a = P { = 1 } }")]
    [InlineData("fn main() { let a = P { x = 1 y } }")]
    [InlineData("fn main() { & }")]
    [InlineData("fn main() { &mut }")]
    [InlineData("fn main() { p. }")]
    [InlineData("fn main() { p.x. = 1 }")]
    public void Broken_struct_syntax_reports_one_NIR1001_and_still_returns_a_tree(string source)
    {
        var (unit, diagnostics) = Parse(source);

        Assert.NotNull(unit);
        Assert.Equal(DiagnosticCodes.SyntaxError, Assert.Single(diagnostics).Code);
    }
}

