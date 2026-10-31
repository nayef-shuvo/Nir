using System.Text.Json;

namespace Nir.Compiler.Emit;

/// <summary>
/// Writes the <c>.runtimeconfig.json</c> that <c>dotnet x.dll</c> needs. Without it the host treats the program as
/// self-contained and fails to start (verified in the T1.1 spike).
/// </summary>
internal static class RuntimeConfigWriter
{
    public static void Write(string path)
    {
        var major = Environment.Version.Major;
        var minor = Environment.Version.Minor;

        var config = new
        {
            runtimeOptions = new
            {
                tfm = $"net{major}.{minor}",
                framework = new { name = "Microsoft.NETCore.App", version = $"{major}.{minor}.0" },
            },
        };

        File.WriteAllText(path, JsonSerializer.Serialize(config));
    }
}
