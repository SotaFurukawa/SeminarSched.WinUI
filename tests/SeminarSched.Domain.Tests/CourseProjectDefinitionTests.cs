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
}
