using SeminarSched.Application;

namespace SeminarSched.Application.Tests;

public sealed class ApplicationVersionTests
{
    [Fact]
    public void Parse_BetaVersion_FormatsDisplayVersion()
    {
        var version = ApplicationVersion.Parse("0.0.0-beta");

        Assert.Equal("0.0.0-beta", version.SemanticVersion);
        Assert.Equal("v0.0.0 (beta)", version.DisplayVersion);
    }

    [Fact]
    public void Parse_IgnoresBuildMetadata()
    {
        var version = ApplicationVersion.Parse("0.1.2-beta+abc123");

        Assert.Equal(new ApplicationVersion(0, 1, 2, "beta"), version);
    }

    [Fact]
    public void Parse_EmptyValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ApplicationVersion.Parse(""));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.2")]
    public void Parse_MalformedValue_ThrowsFormatException(string value)
    {
        Assert.Throws<FormatException>(() => ApplicationVersion.Parse(value));
    }

    [Theory]
    [InlineData("0.16.0", "0.15.0", true)]
    [InlineData("0.15.1", "0.15.0", true)]
    [InlineData("1.0.0", "0.15.0", true)]
    [InlineData("0.15.0", "0.15.0", false)]
    [InlineData("0.14.0", "0.15.0", false)]
    public void IsNewerThan_ComparesMajorMinorPatchOnly(string candidate, string current, bool expectedNewer)
    {
        var candidateVersion = ApplicationVersion.Parse(candidate);
        var currentVersion = ApplicationVersion.Parse(current);

        Assert.Equal(expectedNewer, candidateVersion.IsNewerThan(currentVersion));
    }

    [Fact]
    public void IsNewerThan_IgnoresPrereleaseTag()
    {
        var candidate = ApplicationVersion.Parse("0.15.0-beta");
        var current = ApplicationVersion.Parse("0.15.0-alpha");

        Assert.False(candidate.IsNewerThan(current));
    }
}
