using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nir.Compiler.Language;
using Nir.Tests.Support;

namespace Nir.Tests;

/// <summary>
/// Keeps the VS Code highlighting in step with the language: every keyword of the lexer grammar and every built-in type name must be
/// matched by some rule of <c>editors/vscode-nir/syntaxes/nir.tmLanguage.json</c>. (Adding a keyword to the lexer and forgetting the
/// extension is what left <c>try</c>, <c>catch</c> and <c>anyerror</c> uncoloured.)
/// </summary>
public partial class VsCodeGrammarTests
{
    private static readonly string _root = RepositoryRoot.Find();

    public static TheoryData<string> Words
    {
        get
        {
            var words = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(_root, "src", "Nir.Compiler", "Parsing", "NirLexer.g4")))
            {
                if (KeywordRule().Match(line) is { Success: true } match)
                {
                    words.Add(match.Groups[1].Value);
                }
            }

            foreach (var constant in typeof(TypeNames).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (constant.GetRawConstantValue() is string name && LettersOnly().IsMatch(name))
                {
                    words.Add(name);
                }
            }

            words.Add(ReservedNames.SelfValue);
            var data = new TheoryData<string>();
            foreach (var word in words)
            {
                data.Add(word);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Words))]
    public void Every_keyword_and_builtin_type_is_highlighted(string word)
    {
        var grammar = File.ReadAllText(Path.Combine(_root, "editors", "vscode-nir", "syntaxes", "nir.tmLanguage.json"));
        var patterns = new List<string>();
        Collect(JsonDocument.Parse(grammar).RootElement, patterns);

        Assert.Contains(patterns, pattern => Regex.IsMatch(word, pattern));
    }

    // Every "match" value in the grammar, wherever it is nested.
    private static void Collect(JsonElement element, List<string> patterns)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "match" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        patterns.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        Collect(property.Value, patterns);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, patterns);
                }

                break;
        }
    }

    // `FN: 'fn';` in the lexer grammar: a keyword made of letters only.
    [GeneratedRegex(@"^[A-Z_]+:\s*'([a-z]+)'\s*;")]
    private static partial Regex KeywordRule();

    [GeneratedRegex(@"^[a-z]+$")]
    private static partial Regex LettersOnly();
}
