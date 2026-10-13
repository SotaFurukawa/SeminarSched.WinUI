using SeminarSched.Application;
using SeminarSched.Infrastructure.Updates;

namespace SeminarSched.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseParserTests
{
    private static readonly ApplicationVersion Current = ApplicationVersion.Parse("0.15.0-beta");

    [Fact]
    public void Parse_NewerTag_ReturnsAvailableWithParsedVersionAndUrl()
    {
        var json = """
            {"tag_name": "v0.16.0", "html_url": "https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/v0.16.0"}
            """;

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.True(result.Succeeded);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new ApplicationVersion(0, 16, 0, null), result.LatestVersion);
        Assert.Equal("https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/v0.16.0", result.ReleaseUrl);
    }

    [Fact]
    public void Parse_SameOrOlderTag_ReturnsUpToDate()
    {
        var json = """{"tag_name": "v0.15.0", "html_url": "https://example.test/v0.15.0"}""";

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.True(result.Succeeded);
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public void Parse_MissingHtmlUrl_FallsBackToConstructedReleaseUrl()
    {
        var json = """{"tag_name": "v0.16.0"}""";

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/v0.16.0", result.ReleaseUrl);
    }

    // ユーザー指示（「ダウンロードページを開くボタンを作るのではなく、アップデートを行うボタンを
    // 作る」）への対応。assetsからSetup.exeの直接ダウンロードURLを見つけられることを確認する。
    [Fact]
    public void Parse_AssetsContainExeInstaller_ReturnsInstallerDownloadUrl()
    {
        var json = """
            {
                "tag_name": "v0.16.0",
                "html_url": "https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/v0.16.0",
                "assets": [
                    {"name": "SeminarSched.WinUI-0.16.0-x64.msix", "browser_download_url": "https://example.test/msix"},
                    {"name": "SeminarSched.WinUI.cer", "browser_download_url": "https://example.test/cer"},
                    {"name": "SeminarSched.WinUI-Setup-0.16.0.exe", "browser_download_url": "https://example.test/setup.exe"}
                ]
            }
            """;

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("https://example.test/setup.exe", result.InstallerDownloadUrl);
    }

    [Fact]
    public void Parse_NoAssets_InstallerDownloadUrlIsNull()
    {
        var json = """{"tag_name": "v0.16.0"}""";

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.Null(result.InstallerDownloadUrl);
    }

    [Fact]
    public void Parse_AssetsWithoutExe_InstallerDownloadUrlIsNull()
    {
        var json = """
            {
                "tag_name": "v0.16.0",
                "assets": [
                    {"name": "SeminarSched.WinUI-0.16.0-x64.msix", "browser_download_url": "https://example.test/msix"}
                ]
            }
            """;

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.Null(result.InstallerDownloadUrl);
    }

    [Theory]
    [InlineData("""{"tag_name": null}""")]
    [InlineData("""{"other_field": "value"}""")]
    [InlineData("""{"tag_name": ""}""")]
    public void Parse_MissingOrEmptyTagName_ReturnsFailure(string json)
    {
        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_MalformedTagName_ReturnsFailure()
    {
        var json = """{"tag_name": "not-a-version"}""";

        var result = GitHubReleaseParser.Parse(json, Current);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }
}
