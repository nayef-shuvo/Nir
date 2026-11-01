namespace Nir.Tests.Support;

/// <summary>Finds the repository folder (the one with <c>Nir.slnx</c>) from the test output folder, for tests that read files such as samples.</summary>
internal static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nir.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Nir.slnx was not found above the test directory.");
    }
}
