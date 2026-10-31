using System.Collections.Immutable;
using System.Diagnostics;
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
        if (name is null || body is null)
        {
            return null;
        }

        var parameters = (context.parameterList()?.parameter() ?? [])
            .Select(BuildParameter)
            .OfType<Parameter>()
            .ToImmutableArray();

        return new FunctionDeclaration(name, parameters, BuildIdentifier(context.returnType), body, SpanOf(context));
    }

    private static Parameter? BuildParameter(NirParser.ParameterContext context)
    {
        var name = BuildIdentifier(context.name);
        var type = BuildIdentifier(context.type);

        return name is null || type is null ? null : new Parameter(name, type, SpanOf(context));
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
        if (context.variableDeclaration() is { } declaration)
        {
            return BuildVariableDeclaration(declaration);
        }

        if (context.assignmentStatement() is { } assignment)
        {
            return BuildAssignment(assignment);
        }

        if (context.returnStatement() is { } returnStatement)
        {
            return new ReturnStatement(BuildExpression(returnStatement.value), SpanOf(returnStatement));
        }

        var expression = BuildExpression(context.expression());
        return expression is null ? null : new ExpressionStatement(expression, SpanOf(context));
    }

    private VariableDeclaration? BuildVariableDeclaration(NirParser.VariableDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        var initializer = BuildExpression(context.value);
        if (name is null || initializer is null)
        {
            return null;
        }

        return new VariableDeclaration(name, context.MUT() is not null, BuildIdentifier(context.declaredType), initializer, SpanOf(context));
    }

    private AssignmentStatement? BuildAssignment(NirParser.AssignmentStatementContext context)
    {
        var target = BuildIdentifier(context.target);
        var value = BuildExpression(context.value);

        return target is null || value is null ? null : new AssignmentStatement(target, value, SpanOf(context));
    }

    private Expression? BuildExpression(NirParser.ExpressionContext? context) => context switch
    {
        null => null,
        NirParser.UnaryExpressionContext u => BuildUnary(u),
        NirParser.MultiplicativeExpressionContext m => BuildBinary(m, m.left, m.op, m.right),
        NirParser.AdditiveExpressionContext a => BuildBinary(a, a.left, a.op, a.right),
        NirParser.RelationalExpressionContext r => BuildBinary(r, r.left, r.op, r.right),
        NirParser.EqualityExpressionContext e => BuildBinary(e, e.left, e.op, e.right),
        NirParser.CallExprContext c => c.callExpression() is { } call ? BuildCall(call) : null,
        NirParser.ParenthesizedExpressionContext p => BuildExpression(p.inner) is { } inner ? new ParenthesizedExpression(inner, SpanOf(p)) : null,
        NirParser.StringExprContext s => s.stringLiteral() is { } literal ? BuildStringLiteral(literal) : null,
        NirParser.IntExprContext i => new IntLiteralExpression(i.INT_LITERAL().GetText(), SpanOf(i)),
        NirParser.DoubleExprContext d => new DoubleLiteralExpression(d.DOUBLE_LITERAL().GetText(), SpanOf(d)),
        NirParser.TrueExprContext t => new BoolLiteralExpression(true, SpanOf(t)),
        NirParser.FalseExprContext f => new BoolLiteralExpression(false, SpanOf(f)),
        NirParser.NameExprContext n => BuildIdentifier(n.IDENTIFIER().Symbol) is { } name ? new NameExpression(name, SpanOf(n)) : null,

        // After a syntax error ANTLR can leave a bare ExpressionContext with no alternative chosen. It is not a bug.
        _ => null,
    };

    private UnaryExpression? BuildUnary(NirParser.UnaryExpressionContext context) =>
        BuildExpression(context.operand) is { } operand
            ? new UnaryExpression(UnaryOperator.Negate, operand, SpanOf(context))
            : null;

    private BinaryExpression? BuildBinary(
        NirParser.ExpressionContext context,
        NirParser.ExpressionContext? leftContext,
        IToken op,
        NirParser.ExpressionContext? rightContext)
    {
        var left = BuildExpression(leftContext);
        var right = BuildExpression(rightContext);

        return left is null || right is null ? null : new BinaryExpression(left, ToBinaryOperator(op), right, SpanOf(context));
    }

    private static BinaryOperator ToBinaryOperator(IToken token) => token.Type switch
    {
        NirParser.PLUS => BinaryOperator.Add,
        NirParser.MINUS => BinaryOperator.Subtract,
        NirParser.STAR => BinaryOperator.Multiply,
        NirParser.SLASH => BinaryOperator.Divide,
        NirParser.PERCENT => BinaryOperator.Remainder,
        NirParser.LT => BinaryOperator.Less,
        NirParser.LE => BinaryOperator.LessOrEqual,
        NirParser.GT => BinaryOperator.Greater,
        NirParser.GE => BinaryOperator.GreaterOrEqual,
        NirParser.EQEQ => BinaryOperator.Equal,
        NirParser.NEQ => BinaryOperator.NotEqual,
        _ => throw new UnreachableException(),
    };

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
            : new Identifier(token!.Text, SpanOf(token));

    // ANTLR conjures tokens it expected but did not find (error recovery); they have no source position.
    private static bool IsMissing(IToken? token) => token is null || token.StartIndex < 0;

    private static TextSpan SpanOf(IToken token) =>
        new(token.StartIndex, token.StopIndex - token.StartIndex + 1);

    private static TextSpan SpanOf(ParserRuleContext context)
    {
        var start = Math.Max(0, context.Start.StartIndex);
        var end = context.Stop is { } stop ? stop.StopIndex + 1 : start;
        return new TextSpan(start, Math.Max(0, end - start));
    }
}
