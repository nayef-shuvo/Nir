using Nir.Compiler.Diagnostics;
using Nir.Tests.Support;

namespace Nir.Tests.Programs;

/// <summary>
/// Golden program tests. Each program is <c>Programs/&lt;slice&gt;/&lt;name&gt;.nir</c> plus exactly one of
/// <c>&lt;name&gt;.stdout</c> (it must compile, run and print exactly that) or <c>&lt;name&gt;.diag</c>
/// (it must fail to compile with exactly these <c>line:col CODE</c> lines, in order).
/// </summary>
internal static class ProgramCases
{
    public static TheoryData<string> Names(string slice)
    {
        var data = new TheoryData<string>();
        var directory = Path.Combine(AppContext.BaseDirectory, "Programs", slice);
        foreach (var file in Directory.GetFiles(directory, "*.nir").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    public static void Check(string slice, string name)
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Programs", slice, name);
        var stdoutPath = basePath + ".stdout";
        var diagPath = basePath + ".diag";
        Assert.True(File.Exists(stdoutPath) ^ File.Exists(diagPath), $"{name}: exactly one of .stdout and .diag must exist");

        var source = File.ReadAllText(basePath + ".nir");

        if (File.Exists(stdoutPath))
        {
            var (stdout, stderr, exitCode) = TestHost.Run(source);

            Assert.Equal(File.ReadAllText(stdoutPath), stdout);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal(0, exitCode);
        }
        else
        {
            var result = TestHost.Compile(source, name + ".nir");

            Assert.False(result.Succeeded, $"{name}: expected compile errors but it compiled");
            Assert.Equal(
                File.ReadAllLines(diagPath).Where(line => line.Length > 0),
                result.Diagnostics.Select(Describe));
        }
    }

    private static string Describe(Diagnostic diagnostic)
    {
        var (line, column) = diagnostic.Source.GetLineAndColumn(diagnostic.Span.Start);
        return $"{line}:{column} {diagnostic.Code}";
    }
}
