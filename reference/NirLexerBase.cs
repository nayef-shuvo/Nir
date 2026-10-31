using Antlr4.Runtime;

namespace Nir.Lexer;

/// <summary>
/// Implements Nir's automatic statement-termination rule (semicolons are
/// optional; a newline can imply one), generated as the ANTLR
/// <c>superClass</c> for <see cref="NirLexer"/> via NirLexer.g4.
/// </summary>
/// <remarks>
/// <para>
/// The grammar itself only knows how to recognize a run of newline
/// characters (<c>NEWLINE</c>) and an explicit <c>;</c> (<c>SEMI</c>); it has
/// no way to express "sometimes a newline ends a statement, sometimes it
/// doesn't." That decision is made here, in <see cref="NextToken"/>, by
/// rewriting every <c>NEWLINE</c> the grammar produces into either a
/// synthesized <c>SEMI</c> or nothing at all, so the parser only ever has to
/// deal with one terminator token, regardless of how it was spelled.
/// </para>
/// <para>
/// A <c>NEWLINE</c> becomes a <c>SEMI</c> only when both hold:
/// </para>
/// <list type="bullet">
/// <item>bracket depth is zero -- inside <c>(...)</c> or <c>[...]</c> a
/// newline is always just whitespace, so a multi-line call's argument list
/// never needs a trailing comma to parse (unlike Go);</item>
/// <item>the token right before it could legally end a statement (an
/// identifier, a literal, a closing bracket, or <c>return</c>) -- so
/// <c>x +\n y</c> still continues past the newline, because <c>+</c> isn't
/// one of those.</item>
/// </list>
/// <para>
/// This is the same idea as Go's automatic semicolon insertion, with one
/// deliberate improvement: Go's rule ignores bracket depth entirely, which
/// is why a multi-line call in Go needs a trailing comma before its closing
/// <c>)</c> to avoid a spurious semicolon. Tracking depth here removes that
/// footgun for <c>(...)</c> and <c>[...]</c>.
/// </para>
/// <para>
/// <c>{...}</c> is deliberately NOT depth-tracked, and so keeps Go's
/// footgun: braces also delimit statement blocks, where newlines must stay
/// significant, and the lexer has no way to tell "this <c>{</c> starts a
/// block" apart from "this <c>{</c> starts a struct body or array literal."
/// A multi-line struct body or array literal therefore still needs a
/// trailing comma before its closing <c>}</c> -- exactly like Nir's own
/// struct field lists already write them.
/// </para>
/// </remarks>
// Fully qualified deliberately: an unqualified `Lexer` here would resolve to
// the sibling namespace Nir.Lexer (found via the enclosing Nir namespace)
// rather than the Antlr4.Runtime.Lexer class, even with the using above.
public abstract class NirLexerBase : Antlr4.Runtime.Lexer
{
    private int _bracketDepth;
    private int _previousTokenType = TokenConstants.InvalidType;
    private IToken? _pendingEndOfFile;

    protected NirLexerBase(ICharStream input)
        : base(input)
    {
    }

    protected NirLexerBase(ICharStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput)
    {
    }

    public override IToken NextToken()
    {
        if (_pendingEndOfFile is { } endOfFile)
        {
            _pendingEndOfFile = null;
            return endOfFile;
        }

        while (true)
        {
            var token = base.NextToken();
            TrackBracketDepth(token.Type);

            if (token.Type == TokenConstants.EOF)
            {
                return EndsStatement(_previousTokenType) ? SynthesizeFinalSemi(token) : token;
            }

            if (token.Type != NirLexer.NEWLINE)
            {
                _previousTokenType = token.Type;
                return token;
            }

            if (_bracketDepth == 0 && EndsStatement(_previousTokenType))
            {
                _previousTokenType = NirLexer.SEMI;
                return RetypeAsSemi(token);
            }

            // Insignificant newline: inside brackets, or doesn't follow a
            // token that could end a statement. Keep scanning for the next
            // token without returning anything for this one.
        }
    }

    private void TrackBracketDepth(int tokenType)
    {
        switch (tokenType)
        {
            case NirLexer.LPAREN or NirLexer.LBRACKET:
                _bracketDepth++;
                break;
            case NirLexer.RPAREN or NirLexer.RBRACKET:
                _bracketDepth = Math.Max(0, _bracketDepth - 1);
                break;
        }
    }

    private static bool EndsStatement(int tokenType) => tokenType is
        NirLexer.IDENTIFIER or
        NirLexer.INT_LITERAL or
        NirLexer.UINT_LITERAL or
        NirLexer.DOUBLE_LITERAL or
        NirLexer.STRING_END or
        NirLexer.INTERP_STRING_END or
        NirLexer.RETURN or
        NirLexer.RPAREN or
        NirLexer.RBRACKET or
        NirLexer.RBRACE;

    private static IToken RetypeAsSemi(IToken newline)
    {
        if (newline is not IWritableToken writable)
        {
            throw new InvalidOperationException($"Expected a writable token from the default token factory, got {newline.GetType()}.");
        }

        writable.Type = NirLexer.SEMI;
        return writable;
    }

    /// <summary>
    /// Go's rule inserts a semicolon before EOF too, so a file that ends
    /// without a trailing newline or `;` still terminates its last
    /// statement. The real EOF token is stashed and returned on the next
    /// call, after this synthesized one.
    /// </summary>
    private IToken SynthesizeFinalSemi(IToken endOfFile)
    {
        _pendingEndOfFile = endOfFile;
        _previousTokenType = NirLexer.SEMI;

        return TokenFactory.Create(
            Tuple.Create((ITokenSource)this, (ICharStream)InputStream),
            NirLexer.SEMI,
            string.Empty,
            TokenConstants.DefaultChannel,
            endOfFile.StartIndex,
            endOfFile.StartIndex - 1,
            endOfFile.Line,
            endOfFile.Column);
    }
}
