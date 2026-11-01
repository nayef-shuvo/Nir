using System.Text.RegularExpressions;
using Nir.Tests.Support;

namespace Nir.Tests;

/// <summary>
/// The programs in <c>samples/</c> are documentation, so they must keep working: the programs and apps compile, the ones in
/// <c>errors/</c> fail with the code their first line says, and the ones in <c>crashes/</c> compile (they fail only when run).
/// </summary>
public partial class SamplesTests
{
    private static readonly string _samples = Path.Combine(RepositoryRoot.Find(), "samples");

    public static TheoryData<string> Programs => Files("*.nir", "apps/*.nir", "crashes/*.nir");

    public static TheoryData<string> ErrorPrograms => Files("errors/*.nir");

    [Theory]
    [MemberData(nameof(Programs))]
    public void A_sample_program_compiles(string relativePath)
    {
        var result = TestHost.Compile(File.ReadAllText(Path.Combine(_samples, relativePath)), relativePath);

        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
    }

    [Theory]
    [MemberData(nameof(ErrorPrograms))]
    public void An_error_sample_fails_with_the_code_it_announces(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(_samples, relativePath));
        var announced = ExpectedCode().Match(text.Split('\n')[0]);

        var result = TestHost.Compile(text, relativePath);

        Assert.False(result.Succeeded);
        if (announced.Success)
        {
            Assert.Contains(result.Diagnostics, d => d.Code == announced.Value);
        }
    }

    private static TheoryData<string> Files(params string[] patterns)
    {
        var data = new TheoryData<string>();
        foreach (var pattern in patterns)
        {
            var directory = Path.Combine(_samples, Path.GetDirectoryName(pattern) ?? string.Empty);
            foreach (var file in Directory.GetFiles(directory, Path.GetFileName(pattern)).Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetRelativePath(_samples, file));
            }
        }

        return data;
    }

    [GeneratedRegex(@"NIR\d{4}")]
    private static partial Regex ExpectedCode();
}
