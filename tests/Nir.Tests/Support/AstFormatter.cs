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
        _ => throw new UnreachableException(),
    };

    private static IEnumerable<SyntaxNode> Children(SyntaxNode node) => node switch
    {
        CompilationUnit u => u.Functions,
        FunctionDeclaration f => [f.Body],
        Block b => b.Statements,
        ExpressionStatement s => [s.Expression],
        CallExpression c => c.Arguments,
        StringLiteralExpression => [],
        _ => throw new UnreachableException(),
    };
}
