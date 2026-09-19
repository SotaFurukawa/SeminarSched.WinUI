using SeminarSched.Domain.MasterData;

namespace SeminarSched.Domain.Tests;

public sealed class MasterDataTests
{
    [Fact]
    public void Student_NormalizesTextAndRejectsInvalidMaximum()
    {
        var student = new Student(0, " S-001 ", " 架空 生徒 ", " 中2 ", 3, note: " memo ");
        Assert.Equal("S-001", student.ExternalId);
        Assert.Equal("架空 生徒", student.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Student(0, "S", "Name", "中2", 0));
    }

    [Fact]
    public void Subject_RequiresPositiveSortOrderAndShortAbbreviation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Subject(0, "MATH", "数学", "数", "中学", 0));
        Assert.Throws<ArgumentException>(() => new Subject(0, "MATH", "数学", "12345678901", "中学", 1));
    }

    [Fact]
    public void RegularLessonPriority_IsOneThroughFive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegularLessonProfile(0, 1, 1, null, 6));
    }

    [Theory]
    [InlineData("中2", "中3", false)]
    [InlineData("小6", "中1", false)]
    [InlineData("高2", "高3", false)]
    [InlineData("高3", "既卒", true)]
    [InlineData("既卒", "既卒", false)]
    [InlineData("不明", "不明", false)]
    public void GradeAdvancement_AdvancesOneYearAndGraduatesAfterHigh3(string current, string expected, bool expectedGraduate)
    {
        var (grade, becameGraduate) = GradeAdvancement.Advance(current);
        Assert.Equal(expected, grade);
        Assert.Equal(expectedGraduate, becameGraduate);
    }

    [Theory]
    [InlineData("数学", null, "数")]
    [InlineData("高校・数学IA", "", "数")]
    [InlineData("算数", null, "算")]
    [InlineData("英語", "", "英")]
    [InlineData("独自の科目", null, "目")]
    [InlineData("", null, "科")]
    public void SubjectAbbreviation_FallsBackToKeywordOrLastCharacterWhenShortNameIsEmpty(string displayName, string? shortName, string expected)
    {
        Assert.Equal(expected, SubjectAbbreviation.Resolve(displayName, shortName));
    }

    [Fact]
    public void SubjectAbbreviation_PrefersExplicitShortNameWhenPresent()
    {
        Assert.Equal("数", SubjectAbbreviation.Resolve("数学", "数"));
    }
}
