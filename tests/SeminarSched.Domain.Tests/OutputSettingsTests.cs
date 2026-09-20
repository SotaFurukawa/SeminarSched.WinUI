using SeminarSched.Domain.Output;

namespace SeminarSched.Domain.Tests;

public sealed class OutputSettingsTests
{
    [Fact]
    public void Default_MatchesPreviousHardcodedRendererColors()
    {
        var settings = OutputSettings.Default;
        Assert.Equal("A4", settings.PaperSize);
        Assert.Equal("landscape", settings.Orientation);
        Assert.Equal("#E8E8E8", settings.ClosedFillHex);
        Assert.Equal("#D9D9D9", settings.UnavailableFillHex);
        Assert.Equal("#000000", settings.GroupFillHex);
        Assert.Equal("{project}-{report}", settings.FileNamePattern);
    }

    [Theory]
    [InlineData("A5")]
    [InlineData("")]
    public void Constructor_RejectsInvalidPaperSize(string paperSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputSettings(paperSize: paperSize));

    [Fact]
    public void Constructor_RejectsInvalidHexColor() =>
        Assert.Throws<ArgumentException>(() => new OutputSettings(groupFillHex: "black"));

    [Fact]
    public void Constructor_RejectsUnknownFileNameToken() =>
        Assert.Throws<ArgumentException>(() => new OutputSettings(fileNamePattern: "{project}-{unknown}"));

    [Fact]
    public void BuildFileName_SubstitutesTokensAndSanitizesInvalidCharacters()
    {
        var settings = new OutputSettings(fileNamePattern: "{project}_{report}_{date}");
        var name = settings.BuildFileName("2026年度夏期講習", "全体時間割", new DateOnly(2026, 7, 20));
        Assert.Equal("2026年度夏期講習_全体時間割_20260720", name);
    }
}
