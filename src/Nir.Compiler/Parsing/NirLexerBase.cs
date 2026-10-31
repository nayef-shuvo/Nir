using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Nir.Compiler.Diagnostics;

namespace Nir.Compiler.Parsing;

/// <summary>
/// Base class for the generated <see cref="NirLexer"/> (set through <c>superClass</c> in NirLexer.g4). It does two
/// things the grammar cannot express: it implements Nir's automatic statement-termination rule, and it reports
/// lexical errors as <see cref="Diagnostic"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Semicolons are optional; a newline can imply one. The grammar only recognizes a run of newline characters
/// (<c>NEWLINE</c>) and an explicit <c>;</c> (<c>SEMI</c>). <see cref="NextToken"/> rewrites every <c>NEWLINE</c> into
/// either a synthesized <c>SEMI</c> or nothing, so the parser only ever sees one terminator token.
/// </para>
/// <para>A <c>NEWLINE</c> becomes a <c>SEMI</c> only when both hold:</para>
/// <list type="bullet">
/// <item>bracket depth is zero: inside <c>(...)</c> or <c>[...]</c> a newline is whitespace, so a multi-line call never
/// needs a trailing comma;</item>
/// <item>the previous token could end a statement (see <see cref="EndsStatement"/>), so <c>x +\n y</c> continues.</item>
/// </list>
/// <para>
/// <c>{...}</c> is deliberately not depth-tracked: braces also delimit statement blocks, where newlines must stay
/// significant. A multi-line struct body or array literal therefore needs a trailing comma before its closing brace.
/// </para>
/// <para>
/// Strings: a newline or end of file inside a string literal reports <c>NIR0002</c> once and is repaired into a
/// <c>STRING_END</c> token, so one mistake does not also produce parser errors about the string.
/// </para>
/// </remarks>
public abstract class NirLexerBase : Antlr4.Runtime.Lexer
{
    private readonly Queue<IToken> _pending = new();
    private int _bracketDepth;
    private int _previousTokenType = TokenConstants.InvalidType;
    private bool _insideString;
    private int _stringStart;
    private SourceText? _source;
    private DiagnosticBag? _diagnostics;

    protected NirLexerBase(ICharStream input)
        : base(input)
    {
        RemoveErrorListeners();
    }

    protected NirLexerBase(ICharStream input, TextWriter output, TextWriter errorOutput)
        : base(input, output, errorOutput)
    {
        RemoveErrorListeners();
    }

    /// <summary>Directs lexical errors to a diagnostic bag. Without it, errors are silently ignored.</summary>
    public void Attach(SourceText source, DiagnosticBag diagnostics)
    {
        _source = source;
        _diagnostics = diagnostics;
    }

    public override IToken NextToken()
    {
        if (_pending.Count > 0)
        {
            return _pending.Dequeue();
        }

        while (true)
        {
            var token = base.NextToken();
            TrackBracketDepth(token.Type);

            switch (token.Type)
            {
                case TokenConstants.EOF:
                    return EndOfFile(token);

                case NirLexer.STRING_START:
                    _insideString = true;
                    _stringStart = token.StartIndex;
                    _previousTokenType = token.Type;
                    return token;

                case NirLexer.STRING_END:
                    _insideString = false;
                    _previousTokenType = token.Type;
                    return token;

                case NirLexer.UNTERMINATED_STRING:
                    _insideString = false;
                    ReportUnterminatedString(token.StartIndex);
                    _previousTokenType = NirLexer.STRING_END;
                    return Retype(token, NirLexer.STRING_END);

                case NirLexer.NEWLINE when _bracketDepth == 0 && EndsStatement(_previousTokenType):
                    _previousTokenType = NirLexer.SEMI;
                    return Retype(token, NirLexer.SEMI);

                case NirLexer.NEWLINE:
                    // Insignificant newline: inside brackets, or not after a token that could end a statement.
                    continue;

                default:
                    _previousTokenType = token.Type;
                    return token;
            }
        }
    }

    public override void NotifyListeners(LexerNoViableAltException e)
    {
        var start = e.StartIndex;
        var text = ((ICharStream)e.InputStream).GetText(Interval.Of(start, start));
        Report(DiagnosticCodes.UnexpectedCharacter, $"unexpected character '{text}'", start, 1);
    }

    private IToken EndOfFile(IToken endOfFile)
    {
        var previous = _previousTokenType;

        if (_insideString)
        {
            ReportUnterminatedString(endOfFile.StartIndex);
            _pending.Enqueue(Create(NirLexer.STRING_END, endOfFile));
            previous = NirLexer.STRING_END;
        }

        // Go's rule inserts a semicolon before EOF too, so a file that ends without a trailing newline or `;`
        // still terminates its last statement.
        if (EndsStatement(previous))
        {
            _pending.Enqueue(Create(NirLexer.SEMI, endOfFile));
        }

        _pending.Enqueue(endOfFile);
        return _pending.Dequeue();
    }

    private void ReportUnterminatedString(int endIndex) =>
        Report(DiagnosticCodes.UnterminatedString, "unterminated string literal", _stringStart, Math.Max(1, endIndex - _stringStart));

    private void Report(string code, string message, int start, int length)
    {
        if (_source is null || _diagnostics is null)
        {
            return;
        }

        _diagnostics.Report(code, Severity.Error, message, _source, new TextSpan(start, length));
    }

    private void TrackBracketDepth(int tokenType)
    {
        switch (tokenType)
        {
            case NirLexer.LPAREN:
                _bracketDepth++;
                break;
            case NirLexer.RPAREN:
                _bracketDepth = Math.Max(0, _bracketDepth - 1);
                break;
        }
    }

    /// <summary>
    /// The tokens after which a newline ends a statement. Every time a token that can end an expression or statement
    /// is added to the grammar, add it here and add a lexer test for it.
    /// </summary>
    private static bool EndsStatement(int tokenType) => tokenType is
        NirLexer.IDENTIFIER or
        NirLexer.INT_LITERAL or
        NirLexer.DOUBLE_LITERAL or
        NirLexer.TRUE or
        NirLexer.FALSE or
        NirLexer.RETURN or
        NirLexer.BREAK or
        NirLexer.CONTINUE or
        NirLexer.STRING_END or
        NirLexer.RPAREN or
        NirLexer.RBRACE;

    private static IToken Retype(IToken token, int type)
    {
        if (token is not IWritableToken writable)
        {
            throw new InvalidOperationException($"Expected a writable token from the default token factory, got {token.GetType()}.");
        }

        writable.Type = type;
        return writable;
    }

    /// <summary>Creates an empty synthesized token positioned at the start of <paramref name="at"/>.</summary>
    private IToken Create(int type, IToken at) =>
        TokenFactory.Create(
            Tuple.Create((ITokenSource)this, (ICharStream)InputStream),
            type,
            string.Empty,
            TokenConstants.DefaultChannel,
            at.StartIndex,
            at.StartIndex - 1,
            at.Line,
            at.Column);
}
