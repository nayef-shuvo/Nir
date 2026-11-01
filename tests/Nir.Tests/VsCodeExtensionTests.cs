using System.Text.Json;
using Nir.Tests.Support;

namespace Nir.Tests;

/// <summary>The VS Code extension manifest must point at files that exist (a missing icon makes the package fail to build).</summary>
public class VsCodeExtensionTests
{
    private static readonly string _extension = Path.Combine(RepositoryRoot.Find(), "editors", "vscode-nir");

    [Fact]
    public void Every_file_the_manifest_names_exists()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_extension, "package.json")));
        var root = manifest.RootElement;
        var paths = new List<string> { root.GetProperty("icon").GetString()! };

        var contributes = root.GetProperty("contributes");
        foreach (var language in contributes.GetProperty("languages").EnumerateArray())
        {
            paths.Add(language.GetProperty("configuration").GetString()!);
            var icon = language.GetProperty("icon");
            paths.Add(icon.GetProperty("light").GetString()!);
            paths.Add(icon.GetProperty("dark").GetString()!);
        }

        paths.AddRange(contributes.GetProperty("grammars").EnumerateArray().Select(grammar => grammar.GetProperty("path").GetString()!));

        Assert.All(paths, path => Assert.True(File.Exists(Path.Combine(_extension, path)), $"{path} does not exist"));
    }

    [Fact]
    public void The_file_icon_is_the_main_logo_mark()
    {
        var logo = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "logo", "nir-mark.svg"));
        var icon = File.ReadAllText(Path.Combine(_extension, "images", "file-icon.svg"));

        Assert.Equal(logo, icon);
    }
}
