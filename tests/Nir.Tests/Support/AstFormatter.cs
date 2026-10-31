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
        FunctionDeclaration f => $"FunctionDeclaration {f.Name.Text}",
        Block => "Block",
        ExpressionStatement => "ExpressionStatement",
        CallExpression c => $"CallExpression {c.Callee.Text}",
        StringLiteralExpression s => $"StringLiteral \"{s.Value}\"",
        IntLiteralExpression i => $"IntLiteral {i.Text}",
        DoubleLiteralExpression d => $"DoubleLiteral {d.Text}",
        BoolLiteralExpression b => $"BoolLiteral {(b.Value ? "true" : "false")}",
        VariableDeclaration d => $"VariableDeclaration let {(d.IsMutable ? "mut " : string.Empty)}{d.Name.Text}{(d.DeclaredType is { } t ? ": " + t.Text : string.Empty)}",
        AssignmentStatement a => $"Assignment {a.Target.Text}",
        NameExpression n => $"Name {n.Name.Text}",
        BinaryExpression b => $"Binary {OperatorText.Of(b.Operator)}",
        UnaryExpression u => $"Unary {OperatorText.Of(u.Operator)}",
        ParenthesizedExpression => "Parenthesized",
        _ => throw new UnreachableException(),
    };

    private static IEnumerable<SyntaxNode> Children(SyntaxNode node) => node switch
    {
        CompilationUnit u => u.Functions,
        FunctionDeclaration f => [f.Body],
        Block b => b.Statements,
        ExpressionStatement s => [s.Expression],
        CallExpression c => c.Arguments,
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
