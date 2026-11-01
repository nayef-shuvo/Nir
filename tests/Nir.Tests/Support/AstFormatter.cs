using System.Diagnostics;
using System.Text;
using Nir.Compiler.Syntax;

namespace Nir.Tests.Support;

/// <summary>Prints a syntax tree as indented text, one node per line, so tests can compare whole trees.</summary>
internal static class AstFormatter
{
    public static string Format(SyntaxNode node)
    {
        var builder = new StringBuilder();
        Write(builder, node, 0);
        return builder.ToString().TrimEnd('\n');
    }

    private static void Write(StringBuilder builder, SyntaxNode node, int depth)
    {
        builder.Append(' ', depth * 2).Append(Describe(node)).Append('\n');

        foreach (var child in Children(node))
        {
            Write(builder, child, depth + 1);
        }
    }

    private static string Describe(SyntaxNode node) => node switch
    {
        CompilationUnit => "CompilationUnit",
        StructDeclaration s => $"Struct {s.Name.Text}",
        FieldDeclaration f => $"FieldDecl {f.Name.Text}: {f.Type.Text}",
        TupleAliasDeclaration a => $"TupleAlias {a.Name.Text} = {a.Type.Text}",
        TupleLiteralExpression => "TupleLiteral",
        UnionDeclaration u => $"Union {u.Name.Text} = {string.Join(" | ", u.Members.Select(m => m.Text))}",
        MatchExpression => "Match",
        MatchArm a => $"Arm {a.Name.Text}: {a.Type.Text}",
        DestructuringDeclaration d => "Destructuring " + string.Join(", ", d.Names.Select(n => (n.IsMutable ? "mut " : string.Empty) + n.Name.Text)),
        ImplDeclaration i => $"Impl {i.TypeName.Text}",
        StructLiteralExpression l => $"StructLiteral {l.TypeName.Text}",
        FieldInitializer i => $"Init {i.Name.Text}",
        FieldAccessExpression f => $"FieldAccess {f.Field.Text}",
        SelfExpression => "self",
        ReferenceExpression r => r.IsMutable ? "RefMut" : "Ref",
        UsingDirective u => "Using " + string.Join("::", u.Path.Select(p => p.Text)) + Group(u),
        MemberCallExpression m => $"MemberCall {m.Method.Text}",
        FunctionDeclaration f => $"FunctionDeclaration {f.Name.Text}{Signature(f)}",
        ReturnStatement => "Return",
        IfStatement => "If",
        WhileStatement => "While",
        ForStatement f => $"For {f.Variable.Text} in {(f.IsInclusive ? "..=" : "..")}",
        InterpolatedStringExpression => "Interpolated",
        InterpolatedText t => $"Text \"{t.Text}\"",
        InterpolatedHole => "Hole",
        CastExpression c => $"Cast {c.TargetType.Text}",
        BreakStatement => "Break",
        ContinueStatement => "Continue",
        Label l => l.Text,
        Block => "Block",
        ExpressionStatement => "ExpressionStatement",
        CallExpression c => $"CallExpression {c.Callee.Text}",
        StringLiteralExpression s => $"StringLiteral \"{s.Value}\"",
        IntLiteralExpression i => $"IntLiteral {i.Text}",
        DoubleLiteralExpression d => $"DoubleLiteral {d.Text}",
        BoolLiteralExpression b => $"BoolLiteral {(b.Value ? "true" : "false")}",
        VariableDeclaration d => $"VariableDeclaration let {(d.IsMutable ? "mut " : string.Empty)}{d.Name.Text}{(d.DeclaredType is { } t ? ": " + t.Text : string.Empty)}",
        AssignmentStatement a => $"Assignment {TargetText(a.Target)}",
        NameExpression n => $"Name {n.Name.Text}",
        BinaryExpression b => $"Binary {OperatorText.Of(b.Operator)}",
        UnaryExpression u => $"Unary {OperatorText.Of(u.Operator)}",
        ParenthesizedExpression => "Parenthesized",
        _ => throw new UnreachableException(),
    };

    // The condition, the then-block, and the else part under an `Else` line so the two blocks can be told apart.
    private static List<SyntaxNode> IfChildren(IfStatement statement)
    {
        var children = new List<SyntaxNode> { statement.Condition, statement.Then };
        if (statement.ElseBlock is { } block)
        {
            children.Add(new Label("Else", block));
        }
        else if (statement.ElseIf is { } elseIf)
        {
            children.Add(new Label("Else", elseIf));
        }

        return children;
    }

    // A line of the printed tree that is not itself a syntax node.
    private sealed record Label(string Text, SyntaxNode Child) : SyntaxNode(default(Nir.Compiler.Diagnostics.TextSpan));

    // `p`, `self`, `self.pos.x`: an assignment target written out.
    private static string TargetText(Expression target) => target switch
    {
        NameExpression n => n.Name.Text,
        SelfExpression => "self",
        FieldAccessExpression f => TargetText(f.Receiver) + "." + f.Field.Text,
        _ => throw new UnreachableException(),
    };

    // `::{Math, Console}` for a group import; nothing for a plain one.
    private static string Group(UsingDirective directive) =>
        directive.Members.Length == 0 ? string.Empty : "::{" + string.Join(", ", directive.Members.Select(m => m.Text)) + "}";

    // `(a: int, b: int) -> int`; nothing at all for a function without parameters and return type.
    private static string Signature(FunctionDeclaration function)
    {
        var items = function.Parameters.Select(p => $"{p.Name.Text}: {p.Type.Text}").ToList();
        if (function.Receiver is { } receiver)
        {
            items.Insert(0, receiver.IsMutable ? "&mut self" : "&self");
        }

        var parameters = items.Count == 0 ? string.Empty : "(" + string.Join(", ", items) + ")";
        var returnType = function.ReturnType is { } type ? " -> " + type.Text : string.Empty;

        return parameters + returnType;
    }

    private static IEnumerable<SyntaxNode> Children(SyntaxNode node) => node switch
    {
        CompilationUnit u => u.Usings.Cast<SyntaxNode>().Concat(u.Structs).Concat(u.TupleAliases).Concat(u.Unions).Concat(u.Impls).Concat(u.Functions),
        TupleAliasDeclaration => [],
        UnionDeclaration => [],
        MatchExpression m => [m.Scrutinee, .. m.Arms],
        MatchArm a => [a.Block is { } block ? block : a.Value!],
        TupleLiteralExpression t => t.Elements,
        DestructuringDeclaration d => [d.Initializer],
        StructDeclaration s => s.Fields,
        FieldDeclaration => [],
        ImplDeclaration i => i.Methods,
        StructLiteralExpression l => l.Fields,
        FieldInitializer i => [i.Value],
        FieldAccessExpression f => [f.Receiver],
        SelfExpression => [],
        ReferenceExpression r => [r.Operand],
        UsingDirective => [],
        MemberCallExpression m => [m.Receiver, .. m.Arguments],
        FunctionDeclaration f => [f.Body],
        Block b => b.Statements,
        ExpressionStatement s => [s.Expression],
        CallExpression c => c.Arguments,
        ReturnStatement r => r.Value is { } value ? [value] : [],
        IfStatement i => IfChildren(i),
        WhileStatement w => [w.Condition, w.Body],
        ForStatement f => [f.Start, f.End, f.Body],
        InterpolatedStringExpression i => i.Parts,
        InterpolatedText => [],
        InterpolatedHole h => [h.Expression],
        CastExpression c => [c.Operand],
        BreakStatement or ContinueStatement => [],
        Label l => [l.Child],
        VariableDeclaration d => [d.Initializer],
        AssignmentStatement a => [a.Value],
        NameExpression => [],
        BinaryExpression b => [b.Left, b.Right],
        UnaryExpression u => [u.Operand],
        ParenthesizedExpression p => [p.Inner],
        StringLiteralExpression or IntLiteralExpression or DoubleLiteralExpression or BoolLiteralExpression => [],
        _ => throw new UnreachableException(),
    };
}
