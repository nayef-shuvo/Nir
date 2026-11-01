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
internal sealed class AstBuilder(SourceText source, DiagnosticBag diagnostics)
{
    public CompilationUnit Build(NirParser.CompilationUnitContext context)
    {
        var items = context.topLevelItem();
        var usings = items
            .Select(item => item.usingDirective())
            .OfType<NirParser.UsingDirectiveContext>()
            .Select(BuildUsing)
            .OfType<UsingDirective>()
            .ToImmutableArray();
        var functions = items
            .Select(item => item.functionDeclaration())
            .OfType<NirParser.FunctionDeclarationContext>()
            .Select(BuildFunction)
            .OfType<FunctionDeclaration>()
            .ToImmutableArray();

        var structs = items
            .Select(item => item.structDeclaration())
            .OfType<NirParser.StructDeclarationContext>()
            .Select(BuildStruct)
            .OfType<StructDeclaration>()
            .ToImmutableArray();
        var aliases = items
            .Select(item => item.tupleAliasDeclaration())
            .OfType<NirParser.TupleAliasDeclarationContext>()
            .Select(BuildTupleAlias)
            .OfType<TupleAliasDeclaration>()
            .ToImmutableArray();
        var unions = items
            .Select(item => item.unionDeclaration())
            .OfType<NirParser.UnionDeclarationContext>()
            .Select(BuildUnion)
            .OfType<UnionDeclaration>()
            .ToImmutableArray();
        var impls = items
            .Select(item => item.implDeclaration())
            .OfType<NirParser.ImplDeclarationContext>()
            .Select(BuildImpl)
            .OfType<ImplDeclaration>()
            .ToImmutableArray();

        return new CompilationUnit(usings, structs, aliases, unions, impls, functions, new TextSpan(0, source.Text.Length));
    }

    private static StructDeclaration? BuildStruct(NirParser.StructDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        if (name is null)
        {
            return null;
        }

        var fields = (context.fieldList()?.structField() ?? [])
            .Select(field => (Name: BuildIdentifier(field.name), Type: BuildTypeSyntax(field.type), Context: field))
            .Where(f => f.Name is not null && f.Type is not null)
            .Select(f => new FieldDeclaration(f.Name!, f.Type!, SpanOf(f.Context)))
            .ToImmutableArray();

        return new StructDeclaration(name, fields, SpanOf(context));
    }

    private TupleAliasDeclaration? BuildTupleAlias(NirParser.TupleAliasDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        var type = BuildTupleType(context.tupleType());
        return name is null || type is null ? null : new TupleAliasDeclaration(name, type, SpanOf(context));
    }

    private static UnionDeclaration? BuildUnion(NirParser.UnionDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        var members = context.unionMember().Select(member => BuildTypeSyntax(member.type)).ToList();
        return name is null || members.Any(m => m is null) ? null : new UnionDeclaration(name, [.. members.OfType<TypeSyntax>()], SpanOf(context));
    }

    private static TypeSyntax? BuildTypeSyntax(NirParser.TypeSyntaxContext? context)
    {
        if (context is null)
        {
            return null;
        }

        if (context.tupleType() is { } tuple)
        {
            return BuildTupleType(tuple);
        }

        var name = BuildIdentifier(context.name);
        return name is null ? null : new TypeReference(name, BorrowKind.None, SpanOf(context));
    }

    private static TupleTypeSyntax? BuildTupleType(NirParser.TupleTypeContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var elements = ImmutableArray.CreateBuilder<TupleTypeElement>();
        foreach (var element in context.tupleTypeElement())
        {
            var type = BuildTypeSyntax(element.type);
            if (type is null)
            {
                return null;
            }

            elements.Add(new TupleTypeElement(BuildIdentifier(element.name), type, SpanOf(element)));
        }

        return new TupleTypeSyntax(elements.ToImmutable(), SpanOf(context));
    }

    private ImplDeclaration? BuildImpl(NirParser.ImplDeclarationContext context)
    {
        var typeName = BuildIdentifier(context.typeName);
        if (typeName is null)
        {
            return null;
        }

        var methods = context.functionDeclaration()
            .Select(BuildFunction)
            .OfType<FunctionDeclaration>()
            .ToImmutableArray();

        return new ImplDeclaration(typeName, methods, SpanOf(context));
    }

    private static UsingDirective? BuildUsing(NirParser.UsingDirectiveContext context)
    {
        var path = context.importPath();
        if (path is null)
        {
            return null;
        }

        var parts = BuildIdentifiers(path.IDENTIFIER());
        var members = path.importGroup() is { } group ? BuildIdentifiers(group.IDENTIFIER()) : [];

        return parts.IsEmpty ? null : new UsingDirective(parts, members, SpanOf(context));
    }

    private static ImmutableArray<Identifier> BuildIdentifiers(Antlr4.Runtime.Tree.ITerminalNode[] nodes) =>
        [.. nodes.Select(node => BuildIdentifier(node.Symbol)).OfType<Identifier>()];

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

        var receiver = context.parameterList()?.receiverParameter() is { } r ? new Receiver(r.MUT() is not null, SpanOf(r)) : null;

        return new FunctionDeclaration(name, receiver, parameters, BuildTypeSyntax(context.returnType), body, SpanOf(context));
    }

    private static Parameter? BuildParameter(NirParser.ParameterContext context)
    {
        var name = BuildIdentifier(context.name);
        var type = BuildParameterType(context.type);

        return name is null || type is null ? null : new Parameter(name, type, SpanOf(context));
    }

    private static TypeSyntax? BuildParameterType(NirParser.ParameterTypeContext? context)
    {
        if (context is null)
        {
            return null;
        }

        if (context.typeSyntax() is { } plain)
        {
            return BuildTypeSyntax(plain);
        }

        var name = BuildIdentifier(context.name);
        if (name is null)
        {
            return null;
        }

        return new TypeReference(name, context.MUT() is null ? BorrowKind.Shared : BorrowKind.Mutable, SpanOf(context));
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
        if (context.destructuringDeclaration() is { } destructuring)
        {
            return BuildDestructuring(destructuring);
        }

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

        if (context.ifStatement() is { } ifStatement)
        {
            return BuildIf(ifStatement);
        }

        if (context.whileStatement() is { } whileStatement)
        {
            var condition = BuildExpression(whileStatement.condition);
            var body = BuildBlock(whileStatement.body);
            return condition is null || body is null ? null : new WhileStatement(condition, body, SpanOf(whileStatement));
        }

        if (context.forStatement() is { } forStatement)
        {
            return BuildFor(forStatement);
        }

        if (context.breakStatement() is { } breakStatement)
        {
            return new BreakStatement(SpanOf(breakStatement));
        }

        if (context.continueStatement() is { } continueStatement)
        {
            return new ContinueStatement(SpanOf(continueStatement));
        }

        var expression = BuildExpression(context.expression());
        return expression is null ? null : new ExpressionStatement(expression, SpanOf(context));
    }

    private IfStatement? BuildIf(NirParser.IfStatementContext context)
    {
        var condition = BuildExpression(context.condition);
        var then = BuildBlock(context.then);
        if (condition is null || then is null)
        {
            return null;
        }

        var elseBlock = context.elseBlock is { } block ? BuildBlock(block) : null;
        var elseIf = context.elseIf is { } nested ? BuildIf(nested) : null;
        return new IfStatement(condition, then, elseBlock, elseIf, SpanOf(context));
    }

    private ForStatement? BuildFor(NirParser.ForStatementContext context)
    {
        var variable = BuildIdentifier(context.variable);
        var start = BuildExpression(context.start);
        var end = BuildExpression(context.end);
        var body = BuildBlock(context.body);
        if (variable is null || start is null || end is null || body is null)
        {
            return null;
        }

        return new ForStatement(variable, start, end, context.op.Type == NirParser.DOTDOTEQ, body, SpanOf(context));
    }

    private VariableDeclaration? BuildVariableDeclaration(NirParser.VariableDeclarationContext context)
    {
        var name = BuildIdentifier(context.name);
        var initializer = BuildExpression(context.value);
        if (name is null || initializer is null)
        {
            return null;
        }

        return new VariableDeclaration(name, context.MUT() is not null, BuildTypeSyntax(context.declaredType), initializer, SpanOf(context));
    }

    private DestructuringDeclaration? BuildDestructuring(NirParser.DestructuringDeclarationContext context)
    {
        var initializer = BuildExpression(context.value);
        var names = ImmutableArray.CreateBuilder<DestructuredName>();
        foreach (var element in context.destructuredName())
        {
            if (BuildIdentifier(element.name) is not { } name)
            {
                return null;
            }

            names.Add(new DestructuredName(name, element.MUT() is not null, SpanOf(element)));
        }

        return initializer is null ? null : new DestructuringDeclaration(names.ToImmutable(), initializer, SpanOf(context));
    }

    private AssignmentStatement? BuildAssignment(NirParser.AssignmentStatementContext context)
    {
        var target = BuildAssignmentTarget(context.target);
        var value = BuildExpression(context.value);

        return target is null || value is null ? null : new AssignmentStatement(target, value, SpanOf(context));
    }

    // `a`, `self`, `a.b.c`: a name or `self` followed by field accesses, built left to right.
    private static Expression? BuildAssignmentTarget(NirParser.AssignmentTargetContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var tokens = context.children.OfType<Antlr4.Runtime.Tree.ITerminalNode>()
            .Select(node => node.Symbol)
            .Where(token => token.Type is NirParser.IDENTIFIER or NirParser.SELF or NirParser.INT_LITERAL or NirParser.DOUBLE_LITERAL)
            .ToList();
        if (tokens.Count == 0 || IsMissing(tokens[0]))
        {
            return null;
        }

        var first = tokens[0];
        Expression target = first.Type == NirParser.SELF
            ? new SelfExpression(SpanOf(first))
            : new NameExpression(new Identifier(first.Text, SpanOf(first)), SpanOf(first));

        foreach (var token in tokens.Skip(1))
        {
            if (IsMissing(token))
            {
                return null;
            }

            target = AddFieldAccesses(target, token);
        }

        return target;
    }

    // `.name` or `.0` after a receiver. The lexer reads `.0.1` as `.` and the number 0.1, so a DOUBLE_LITERAL is two indexes.
    private static FieldAccessExpression AddFieldAccesses(Expression receiver, IToken token)
    {
        var access = (FieldAccessExpression?)null;
        var start = token.StartIndex;
        foreach (var part in token.Text.Split('.'))
        {
            var span = new TextSpan(receiver.Span.Start, start + part.Length - receiver.Span.Start);
            access = new FieldAccessExpression(receiver, new Identifier(part, new TextSpan(start, part.Length)), span);
            receiver = access;
            start += part.Length + 1;
        }

        return access!;
    }

    private Expression? BuildExpression(NirParser.ExpressionContext? context) => context switch
    {
        null => null,
        NirParser.MemberCallExpressionContext m => BuildMemberCall(m),
        NirParser.FieldAccessExpressionContext f => BuildFieldAccess(f),
        NirParser.TupleIndexExpressionContext t => BuildExpression(t.receiver) is { } receiver && !IsMissing(t.index) ? AddFieldAccesses(receiver, t.index) : null,
        NirParser.TupleLiteralExprContext t => BuildTupleLiteral(t),
        NirParser.MatchExprContext m => BuildMatch(m),
        NirParser.ReferenceExpressionContext r => BuildReference(r),
        NirParser.SelfExprContext s => new SelfExpression(SpanOf(s)),
        NirParser.StructLiteralExprContext l => BuildStructLiteral(l),
        NirParser.UnaryExpressionContext u => BuildUnary(u),
        NirParser.MultiplicativeExpressionContext m => BuildBinary(m, m.left, m.op, m.right),
        NirParser.AdditiveExpressionContext a => BuildBinary(a, a.left, a.op, a.right),
        NirParser.RelationalExpressionContext r => BuildBinary(r, r.left, r.op, r.right),
        NirParser.EqualityExpressionContext e => BuildBinary(e, e.left, e.op, e.right),
        NirParser.LogicalAndExpressionContext a => BuildBinary(a, a.left, a.op, a.right),
        NirParser.LogicalOrExpressionContext o => BuildBinary(o, o.left, o.op, o.right),
        NirParser.CallExprContext c => c.callExpression() is { } call ? BuildCall(call) : null,
        NirParser.ParenthesizedExpressionContext p => BuildExpression(p.inner) is { } inner ? new ParenthesizedExpression(inner, SpanOf(p)) : null,
        NirParser.StringExprContext s => s.stringLiteral() is { } literal ? BuildStringLiteral(literal) : null,
        NirParser.InterpolatedStringExprContext i => i.interpolatedString() is { } interpolated ? BuildInterpolated(interpolated) : null,
        NirParser.CastExpressionContext c => BuildCast(c),
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
            ? new UnaryExpression(context.op.Type == NirParser.BANG ? UnaryOperator.Not : UnaryOperator.Negate, operand, SpanOf(context))
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
        NirParser.AMPAMP => BinaryOperator.And,
        NirParser.PIPEPIPE => BinaryOperator.Or,
        _ => throw new UnreachableException(),
    };

    private FieldAccessExpression? BuildFieldAccess(NirParser.FieldAccessExpressionContext context)
    {
        var receiver = BuildExpression(context.receiver);
        var field = BuildIdentifier(context.field);
        return receiver is null || field is null ? null : new FieldAccessExpression(receiver, field, SpanOf(context));
    }

    private MatchExpression? BuildMatch(NirParser.MatchExprContext context)
    {
        var scrutinee = BuildExpression(context.scrutinee);
        var arms = context.matchArm().Select(BuildMatchArm).ToList();
        return scrutinee is null || arms.Count == 0 || arms.Any(a => a is null)
            ? null
            : new MatchExpression(scrutinee, [.. arms.OfType<MatchArm>()], SpanOf(context));
    }

    private MatchArm? BuildMatchArm(NirParser.MatchArmContext context)
    {
        var name = BuildIdentifier(context.name);
        var type = BuildTypeSyntax(context.type);
        var block = context.body is null ? null : BuildBlock(context.body);
        var value = context.value is null ? null : BuildExpression(context.value);
        return name is null || type is null || (block is null && value is null) ? null : new MatchArm(name, type, block, value, SpanOf(context));
    }

    private TupleLiteralExpression? BuildTupleLiteral(NirParser.TupleLiteralExprContext context)
    {
        var elements = context._elements.Select(BuildExpression).ToList();
        return elements.Any(e => e is null) ? null : new TupleLiteralExpression([.. elements.OfType<Expression>()], SpanOf(context));
    }

    private ReferenceExpression? BuildReference(NirParser.ReferenceExpressionContext context)
    {
        var operand = BuildExpression(context.operand);
        return operand is null ? null : new ReferenceExpression(operand, context.MUT() is not null, SpanOf(context));
    }

    private StructLiteralExpression? BuildStructLiteral(NirParser.StructLiteralExprContext context)
    {
        var typeName = BuildIdentifier(context.typeName);
        if (typeName is null)
        {
            return null;
        }

        var fields = (context.fieldInitializerList()?.fieldInitializer() ?? [])
            .Select(initializer => (Name: BuildIdentifier(initializer.name), Value: BuildExpression(initializer.value), Context: initializer))
            .Where(f => f.Name is not null && f.Value is not null)
            .Select(f => new FieldInitializer(f.Name!, f.Value!, SpanOf(f.Context)))
            .ToImmutableArray();

        return new StructLiteralExpression(typeName, fields, SpanOf(context));
    }

    private MemberCallExpression? BuildMemberCall(NirParser.MemberCallExpressionContext context)
    {
        var receiver = BuildExpression(context.receiver);
        var method = BuildIdentifier(context.method);
        if (receiver is null || method is null)
        {
            return null;
        }

        return new MemberCallExpression(receiver, method, BuildArguments(context.argumentList()), SpanOf(context));
    }

    private ImmutableArray<Expression> BuildArguments(NirParser.ArgumentListContext? list) =>
        [.. (list?.expression() ?? []).Select(BuildExpression).OfType<Expression>()];

    private CallExpression? BuildCall(NirParser.CallExpressionContext context)
    {
        var parts = context.callee is { } name ? BuildIdentifiers(name.IDENTIFIER()) : [];
        if (parts.IsEmpty)
        {
            return null;
        }

        var callee = new QualifiedName(parts, SpanOf(context.callee));

        return new CallExpression(callee, BuildArguments(context.argumentList()), SpanOf(context));
    }

    private StringLiteralExpression BuildStringLiteral(NirParser.StringLiteralContext context)
    {
        var text = context.STRING_TEXT()?.Symbol;
        return new StringLiteralExpression(Decode(text, interpolated: false), SpanOf(context));
    }

    private InterpolatedStringExpression BuildInterpolated(NirParser.InterpolatedStringContext context)
    {
        var parts = ImmutableArray.CreateBuilder<InterpolatedStringPart>();
        foreach (var part in context.interpolatedPart())
        {
            switch (part)
            {
                case NirParser.InterpolatedTextContext text when text.INTERP_TEXT()?.Symbol is { } token:
                    parts.Add(new InterpolatedText(Decode(token, interpolated: true), SpanOf(token)));
                    break;
                case NirParser.InterpolatedHoleContext hole when BuildExpression(hole.expression()) is { } expression:
                    parts.Add(new InterpolatedHole(expression, SpanOf(hole)));
                    break;
            }
        }

        return new InterpolatedStringExpression(parts.ToImmutable(), SpanOf(context));
    }

    private CastExpression? BuildCast(NirParser.CastExpressionContext context)
    {
        var operand = BuildExpression(context.operand);
        var target = BuildIdentifier(context.targetType);
        return operand is null || target is null ? null : new CastExpression(operand, target, SpanOf(context));
    }

    // Decodes the escapes of a text token, reporting each unknown one at its place in the source.
    private string Decode(IToken? token, bool interpolated)
    {
        if (token is null || IsMissing(token))
        {
            return string.Empty;
        }

        return StringEscapes.Decode(token.Text, interpolated, (offset, length, text) =>
            diagnostics.Report(
                DiagnosticCodes.UnknownEscape,
                Severity.Error,
                $"unknown escape sequence '{text}'",
                source,
                new TextSpan(token.StartIndex + offset, length)));
    }

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
