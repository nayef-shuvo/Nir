using System.Collections.Immutable;
using Antlr4.Runtime;
using Nir.Compiler.Diagnostics;
using Nir.Compiler.Syntax;

namespace Nir.Compiler.Parsing;

/// <summary>
/// Converts an ANTLR parse tree into our own syntax tree. This is the only place that reads ANTLR contexts.
/// After a syntax error the parse tree can have missing parts; those are skipped instead of throwing.
/// </summary>
internal sealed class AstBuilder(SourceText source)
{
    public CompilationUnit Build(NirParser.CompilationUnitContext context)
    {
        var functions = context.functionDeclaration()
            .Select(BuildFunction)
            .OfType<FunctionDeclaration>()
            .ToImmutableArray();

        return new CompilationUnit(functions, new TextSpan(0, source.Text.Length));
    }

    private FunctionDeclaration? BuildFunction(NirParser.FunctionDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        var body = BuildBlock(context.body);

        return name is null || body is null ? null : new FunctionDeclaration(name, body, SpanOf(context));
    }

    private Block? BuildBlock(NirParser.BlockContext? context)
    {
        var statementList = context?.statementList();
        if (context is null || statementList is null)
        {
            return null;
        }

        var statements = statementList.statement()
            .Select(BuildStatement)
            .OfType<Statement>()
            .ToImmutableArray();

        return new Block(statements, SpanOf(context));
    }

    private Statement? BuildStatement(NirParser.StatementContext context)
    {
        var expression = BuildExpression(context.expression());
        return expression is null ? null : new ExpressionStatement(expression, SpanOf(context));
    }

    private Expression? BuildExpression(NirParser.ExpressionContext? context)
    {
        if (context is null)
        {
            return null;
        }

        if (context.callExpression() is { } call)
        {
            return BuildCall(call);
        }

        return context.stringLiteral() is { } literal ? BuildStringLiteral(literal) : null;
    }

    private CallExpression? BuildCall(NirParser.CallExpressionContext context)
    {
        var callee = BuildIdentifier(context.callee);
        if (callee is null)
        {
            return null;
        }

        var arguments = (context.argumentList()?.expression() ?? [])
            .Select(BuildExpression)
            .OfType<Expression>()
            .ToImmutableArray();

        return new CallExpression(callee, arguments, SpanOf(context));
    }

    private static StringLiteralExpression BuildStringLiteral(NirParser.StringLiteralContext context) =>
        new(context.STRING_TEXT()?.GetText() ?? string.Empty, SpanOf(context));

    private static Identifier? BuildIdentifier(IToken? token) =>
        IsMissing(token)
            ? null
            : new Identifier(token!.Text, new TextSpan(token.StartIndex, token.StopIndex - token.StartIndex + 1));

    // ANTLR conjures tokens it expected but did not find (error recovery); they have no source position.
    private static bool IsMissing(IToken? token) => token is null || token.StartIndex < 0;

    private static TextSpan SpanOf(ParserRuleContext context)
    {
        var start = Math.Max(0, context.Start.StartIndex);
        var end = context.Stop is { } stop ? stop.StopIndex + 1 : start;
        return new TextSpan(start, Math.Max(0, end - start));
    }
}
