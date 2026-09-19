using System.Xml.Linq;

namespace SeminarSched.Architecture.Tests;

public sealed class RepositoryPolicyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CentralVersion_IsCurrentBetaVersion()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.Equal("0.3.0", document.Descendants("VersionPrefix").Single().Value);
        Assert.Equal("beta", document.Descendants("VersionSuffix").Single().Value);
    }

    [Fact]
    public void GitIgnore_ProtectsRuntimeData()
    {
        var gitIgnore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));

        Assert.Contains("*.jukuschedule", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("*.xlsx", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("*.db", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("*.pdf", gitIgnore, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
