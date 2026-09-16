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
}
