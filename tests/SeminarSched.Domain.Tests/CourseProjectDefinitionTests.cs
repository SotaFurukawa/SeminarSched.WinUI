using SeminarSched.Domain.Projects;

namespace SeminarSched.Domain.Tests;

public sealed class CourseProjectDefinitionTests
{
    [Fact]
    public void Create_SummerProject_GeneratesJapaneseTitle()
    {
        var project = CourseProjectDefinition.Create(
            2026,
            CourseSeason.Summer,
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 8, 31));

        Assert.Equal("2026夏期講習", project.Title);
    }

    [Fact]
    public void Create_EndBeforeStart_RejectsDefinition()
    {
        Assert.Throws<ArgumentException>(() => CourseProjectDefinition.Create(
            2026,
            CourseSeason.Winter,
            new DateOnly(2026, 12, 25),
            new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Create_OtherSeasonWithCustomName_UsesCustomNameInTitle()
    {
        var project = CourseProjectDefinition.Create(
            2026,
            CourseSeason.Other,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            customSeasonName: "GW特別講習");

        Assert.Equal("2026GW特別講習", project.Title);
        Assert.Equal("GW特別講習", project.CustomSeasonName);
    }

    [Fact]
    public void Create_OtherSeasonWithoutCustomName_RejectsDefinition()
    {
        // アプリのUI（OtherSeasonNameBox）は未入力時に空文字列を渡すため、その経路を再現する。
        Assert.Throws<ArgumentException>(() => CourseProjectDefinition.Create(
            2026,
            CourseSeason.Other,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            customSeasonName: ""));
    }
}
