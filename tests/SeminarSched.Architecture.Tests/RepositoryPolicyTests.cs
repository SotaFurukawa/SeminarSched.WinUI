using System.Xml.Linq;

namespace SeminarSched.Architecture.Tests;

public sealed class RepositoryPolicyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ユーザーの明示指示により、このcheckpointでv1.0.0（VersionSuffix無し）としてリリースした。
    // 以前はテスト名・アサーションとも「ベータ版の値と一致するか」だけを見ていたが、v1.0.0では
    // VersionSuffixが空文字列になるため、それに合わせて更新した。
    [Fact]
    public void CentralVersion_MatchesCurrentRelease()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.Equal("1.4.4", document.Descendants("VersionPrefix").Single().Value);
        Assert.Equal("", document.Descendants("VersionSuffix").Single().Value);
    }

    // ユーザー報告バグ修正（checkpoint113）: Package.appxmanifestのIdentity/Versionは単なる静的な
    // XML属性で、GenerateAppxPackageOnBuildはこれをそのままパッケージ化するだけでDirectory.Build.props
    // のAppxPackageVersionからは自動反映されない（New-MsixPackage.ps1で明示的に同期するよう修正済み）。
    // v0.9.1〜v0.13.1のすべてのmsixが内部的に同じVersion="0.9.0.0"のまま出荷され続けていたのに、
    // 誰も気付けなかった原因はこのファイルとDirectory.Build.propsの一致を検証するテストが無かった
    // ことなので、再発防止のため追加する。
    [Fact]
    public void PackageAppxManifest_IdentityVersionMatchesCentralVersion()
    {
        var version = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"))
            .Descendants("VersionPrefix").Single().Value;
        var manifest = XDocument.Load(Path.Combine(RepositoryRoot, "src", "SeminarSched.WinUI", "Package.appxmanifest"));
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identityVersion = manifest.Descendants(ns + "Identity").Single().Attribute("Version")!.Value;

        Assert.Equal($"{version}.0", identityVersion);
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
