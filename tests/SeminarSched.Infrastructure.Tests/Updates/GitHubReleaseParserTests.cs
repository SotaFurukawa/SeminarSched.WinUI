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
