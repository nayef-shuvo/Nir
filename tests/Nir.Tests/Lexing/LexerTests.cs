using Nir.Compiler.Diagnostics;
using Nir.Tests.Support;

namespace Nir.Tests.Lexing;

public class LexerTests
{
    [Fact]
    public void Function_header()
    {
        Assert.Equal(
            ["FN 'fn'", "IDENTIFIER 'main'", "LPAREN '('", "RPAREN ')'", "LBRACE '{'", "RBRACE '}'", "SEMI ''", "EOF"],
            TokenFormatter.Lex("fn main() {}").Tokens);
    }

    [Fact]
    public void Newline_after_identifier_becomes_semicolon()
    {
        Assert.Equal("IDENTIFIER SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("a\nb"));
    }

    [Fact]
    public void Newline_after_closing_paren_becomes_semicolon()
    {
        Assert.Equal("IDENTIFIER LPAREN RPAREN SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("f()\nb"));
    }

    [Fact]
    public void Newline_after_open_brace_is_dropped()
    {
        Assert.Equal("LBRACE RBRACE SEMI EOF", TokenFormatter.Names("{\n}"));
    }

    [Fact]
    public void Newline_after_comma_is_dropped()
    {
        Assert.Equal("IDENTIFIER COMMA IDENTIFIER SEMI EOF", TokenFormatter.Names("a,\nb"));
    }

    [Fact]
    public void Newline_inside_parentheses_is_dropped()
    {
        Assert.Equal("IDENTIFIER LPAREN IDENTIFIER RPAREN SEMI EOF", TokenFormatter.Names("f(\na\n)"));
    }

    [Fact]
    public void Blank_lines_collapse_to_one_semicolon()
    {
        Assert.Equal("IDENTIFIER SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("a\n  \n\nb"));
    }

    [Fact]
    public void Explicit_semicolon_is_kept()
    {
        Assert.Equal("IDENTIFIER SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("a;b"));
    }

    [Fact]
    public void Newline_after_explicit_semicolon_adds_nothing()
    {
        Assert.Equal("IDENTIFIER SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("a;\nb"));
    }

    [Fact]
    public void Line_comment_is_skipped()
    {
        Assert.Equal("IDENTIFIER SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("a // note\nb"));
    }

    [Fact]
    public void String_literal_tokens()
    {
        Assert.Equal(
            ["STRING_START '\"'", "STRING_TEXT 'hi'", "STRING_END '\"'", "SEMI ''", "EOF"],
            TokenFormatter.Lex("\"hi\"").Tokens);
    }

    [Fact]
    public void Empty_string_has_no_text_token()
    {
        Assert.Equal("STRING_START STRING_END SEMI EOF", TokenFormatter.Names("\"\""));
    }

    [Fact]
    public void Backslash_is_kept_raw_in_string()
    {
        var (tokens, diagnostics) = TokenFormatter.Lex("\"a\\nb\"");

        // The source has a backslash followed by the letter n, and the token text keeps exactly those two characters.
        Assert.Equal(["STRING_START '\"'", "STRING_TEXT 'a\\nb'", "STRING_END '\"'", "SEMI ''", "EOF"], tokens);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Escaped_quote_does_not_end_the_string()
    {
        var (tokens, diagnostics) = TokenFormatter.Lex("\"a\\\"b\"");

        Assert.Equal(["STRING_START '\"'", "STRING_TEXT 'a\\\"b'", "STRING_END '\"'", "SEMI ''", "EOF"], tokens);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Missing_closing_quote_reports_NIR0002_at_the_opening_quote()
    {
        var (_, diagnostics) = TokenFormatter.Lex("x \"abc\ny");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnterminatedString, diagnostic.Code);
        Assert.Equal((1, 3), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
    }

    [Fact]
    public void Missing_closing_quote_still_yields_a_complete_string()
    {
        Assert.Equal(
            "IDENTIFIER STRING_START STRING_TEXT STRING_END IDENTIFIER SEMI EOF",
            TokenFormatter.Names("x \"abc\ny"));
    }

    [Fact]
    public void Unterminated_string_at_end_of_file_reports_NIR0002()
    {
        var (tokens, diagnostics) = TokenFormatter.Lex("\"abc");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnterminatedString, diagnostic.Code);
        Assert.Equal((1, 1), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
        Assert.Equal(["STRING_START '\"'", "STRING_TEXT 'abc'", "STRING_END ''", "SEMI ''", "EOF"], tokens);
    }

    [Fact]
    public void Unexpected_character_reports_NIR0001()
    {
        var (_, diagnostics) = TokenFormatter.Lex("a @ b");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UnexpectedCharacter, diagnostic.Code);
        Assert.Equal("unexpected character '@'", diagnostic.Message);
        Assert.Equal((1, 3), diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start));
    }

    [Fact]
    public void Int_literal()
    {
        Assert.Equal(["INT_LITERAL '10'", "SEMI ''", "EOF"], TokenFormatter.Lex("10").Tokens);
    }

    [Fact]
    public void Double_literal()
    {
        Assert.Equal(["DOUBLE_LITERAL '2.5'", "SEMI ''", "EOF"], TokenFormatter.Lex("2.5").Tokens);
    }

    [Fact]
    public void Digits_then_dot_without_digit_is_an_int_then_a_dot()
    {
        var (tokens, diagnostics) = TokenFormatter.Lex("1.");

        Assert.Equal(["INT_LITERAL '1'", "DOT '.'", "EOF"], tokens);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("false")]
    public void Newline_after_literal_becomes_semicolon(string literal)
    {
        Assert.EndsWith("SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names(literal + "\nx"), StringComparison.Ordinal);
    }

    [Fact]
    public void True_and_false_are_keywords()
    {
        Assert.Equal("TRUE FALSE IDENTIFIER SEMI EOF", TokenFormatter.Names("true false truest"));
    }

    [Fact]
    public void Operators()
    {
        Assert.Equal(
            "INT_LITERAL PLUS INT_LITERAL MINUS INT_LITERAL STAR INT_LITERAL SLASH INT_LITERAL PERCENT INT_LITERAL SEMI EOF",
            TokenFormatter.Names("1 + 2 - 3 * 4 / 5 % 6"));
    }

    [Fact]
    public void Comparison_operators_prefer_the_longest_match()
    {
        Assert.Equal(
            "IDENTIFIER LE IDENTIFIER GE IDENTIFIER EQEQ IDENTIFIER NEQ IDENTIFIER LT IDENTIFIER GT IDENTIFIER SEMI EOF",
            TokenFormatter.Names("a <= b >= c == d != e < f > g"));
    }

    [Fact]
    public void Newline_after_operator_is_dropped()
    {
        Assert.Equal("INT_LITERAL PLUS INT_LITERAL SEMI EOF", TokenFormatter.Names("1 +\n2"));
    }

    [Fact]
    public void Newline_before_operator_ends_the_statement()
    {
        Assert.Equal("INT_LITERAL SEMI PLUS INT_LITERAL SEMI EOF", TokenFormatter.Names("1\n+ 2"));
    }

    [Fact]
    public void Variable_declaration_tokens()
    {
        Assert.Equal(
            "LET MUT IDENTIFIER COLON IDENTIFIER EQ INT_LITERAL SEMI EOF",
            TokenFormatter.Names("let mut x: int = 1"));
    }

    [Fact]
    public void Assignment_and_equality_are_different_tokens()
    {
        Assert.Equal("IDENTIFIER EQ IDENTIFIER EQEQ IDENTIFIER SEMI EOF", TokenFormatter.Names("x = y == z"));
    }

    [Fact]
    public void Newline_after_equals_is_dropped()
    {
        Assert.Equal("LET IDENTIFIER EQ INT_LITERAL SEMI EOF", TokenFormatter.Names("let x =\n1"));
    }

    [Fact]
    public void Keyword_prefix_is_still_an_identifier()
    {
        Assert.Equal("IDENTIFIER IDENTIFIER SEMI EOF", TokenFormatter.Names("letter muted"));
    }

    [Fact]
    public void Arrow_is_one_token()
    {
        Assert.Equal("ARROW EOF", TokenFormatter.Names("->"));
        Assert.Equal("MINUS GT EOF", TokenFormatter.Names("- >"));
    }

    [Fact]
    public void Return_is_a_keyword()
    {
        Assert.Equal("RETURN IDENTIFIER SEMI EOF", TokenFormatter.Names("return returned"));
    }

    [Fact]
    public void Newline_after_return_ends_the_statement()
    {
        Assert.Equal("RETURN SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names("return\nx"));
    }

    [Fact]
    public void Function_signature_tokens()
    {
        Assert.Equal(
            "FN IDENTIFIER LPAREN IDENTIFIER COLON IDENTIFIER COMMA IDENTIFIER COLON IDENTIFIER RPAREN ARROW IDENTIFIER LBRACE RBRACE SEMI EOF",
            TokenFormatter.Names("fn add(a: int, b: int) -> int {}"));
    }

    [Fact]
    public void Colon_colon_is_one_token_and_colon_stays_one()
    {
        Assert.Equal("IDENTIFIER COLONCOLON IDENTIFIER SEMI EOF", TokenFormatter.Names("a::b"));
        Assert.Equal("IDENTIFIER COLON COLON IDENTIFIER SEMI EOF", TokenFormatter.Names("a: :b"));
        Assert.Equal("IDENTIFIER COLON IDENTIFIER SEMI EOF", TokenFormatter.Names("a:b"));
    }

    [Fact]
    public void Using_is_a_keyword()
    {
        Assert.Equal("USING IDENTIFIER SEMI EOF", TokenFormatter.Names("using x"));
        Assert.Equal("IDENTIFIER SEMI EOF", TokenFormatter.Names("usingx"));
    }

    [Fact]
    public void Dot_after_a_string_an_identifier_and_an_int()
    {
        Assert.Equal("STRING_START STRING_TEXT STRING_END DOT IDENTIFIER LPAREN RPAREN SEMI EOF", TokenFormatter.Names("\"a\".F()"));
        Assert.Equal("IDENTIFIER DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("x.y"));
        Assert.Equal("INT_LITERAL DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("42.ToString"));
        Assert.Equal("DOUBLE_LITERAL DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("2.5.ToString"));
    }

    [Fact]
    public void Newline_after_dot_is_dropped_but_newline_before_it_ends_the_statement()
    {
        Assert.Equal("IDENTIFIER DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("x.\ny"));
        Assert.Equal("IDENTIFIER SEMI DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("x\n.y"));
    }

    [Fact]
    public void Control_flow_keywords()
    {
        Assert.Equal(
            "IF ELSE WHILE FOR IN BREAK CONTINUE SEMI EOF",
            TokenFormatter.Names("if else while for in break continue"));
        Assert.Equal("IDENTIFIER IDENTIFIER IDENTIFIER IDENTIFIER SEMI EOF", TokenFormatter.Names("iffy elsewhere whiled format"));
    }

    [Fact]
    public void Range_operators_are_not_confused_with_a_dot_or_a_double()
    {
        Assert.Equal("INT_LITERAL DOTDOT INT_LITERAL SEMI EOF", TokenFormatter.Names("0..10"));
        Assert.Equal("INT_LITERAL DOTDOTEQ INT_LITERAL SEMI EOF", TokenFormatter.Names("0..=10"));
        Assert.Equal("IDENTIFIER DOTDOT IDENTIFIER SEMI EOF", TokenFormatter.Names("a..b"));
        Assert.Equal("DOUBLE_LITERAL SEMI EOF", TokenFormatter.Names("0.5"));
        Assert.Equal("INT_LITERAL DOT IDENTIFIER SEMI EOF", TokenFormatter.Names("5.F"));
    }

    [Fact]
    public void Logical_operators_and_bang()
    {
        Assert.Equal("BANG IDENTIFIER AMPAMP IDENTIFIER PIPEPIPE IDENTIFIER SEMI EOF", TokenFormatter.Names("!a && b || c"));
        Assert.Equal("IDENTIFIER NEQ IDENTIFIER SEMI EOF", TokenFormatter.Names("a != b"));
        Assert.Equal("BANG IDENTIFIER EQ IDENTIFIER SEMI EOF", TokenFormatter.Names("!a = b"));
    }

    [Theory]
    [InlineData("break")]
    [InlineData("continue")]
    public void Newline_after_break_and_continue_ends_the_statement(string keyword)
    {
        Assert.Equal($"{keyword.ToUpperInvariant()} SEMI IDENTIFIER SEMI EOF", TokenFormatter.Names(keyword + "\nx"));
    }

    [Fact]
    public void Newline_before_else_ends_the_statement_but_after_open_brace_does_not()
    {
        Assert.Equal("RBRACE SEMI ELSE LBRACE RBRACE SEMI EOF", TokenFormatter.Names("}\nelse {\n}"));
    }
}

