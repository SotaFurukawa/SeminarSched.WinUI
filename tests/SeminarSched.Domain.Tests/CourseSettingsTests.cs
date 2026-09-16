using SeminarSched.Domain.CourseSettings;

namespace SeminarSched.Domain.Tests;

public sealed class CourseSettingsTests
{
    [Fact]
    public void TimeSlot_NormalizesAndValidatesRange()
    {
        var slot = new TimeSlot(0, " 1 ", " 1限 ", new TimeOnly(9, 0), new TimeOnly(10, 20), 1);
        Assert.Equal("1", slot.Code);
        Assert.Equal("1限", slot.DisplayName);
        Assert.Throws<ArgumentException>(() => new TimeSlot(0, "1", "1限", new TimeOnly(10, 0), new TimeOnly(10, 0), 1));
    }
}
