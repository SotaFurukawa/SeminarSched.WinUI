using SeminarSched.Reporting.Layout;

namespace SeminarSched.Infrastructure.Tests;

public sealed class WeeklyCalendarLayoutTests
{
    [Fact]
    public void Build_SpansSundayToSaturdayAroundTheProjectRange()
    {
        var weeks = WeeklyCalendarLayout.Build(new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 25), new Dictionary<DateOnly, IReadOnlyList<string>>());
        var week = Assert.Single(weeks);
        Assert.Equal(new DateOnly(2026, 7, 19), week.SundayStart);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(new DateOnly(2026, 7, 25), week.Days[6].Date);
    }

    [Fact]
    public void Build_PlacesLinesOnTheirDateAndLeavesOtherDaysEmpty()
    {
        var lines = new Dictionary<DateOnly, IReadOnlyList<string>> { [new DateOnly(2026, 7, 22)] = ["数学 田中t"] };
        var week = Assert.Single(WeeklyCalendarLayout.Build(new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 22), lines));
        Assert.Equal("数学 田中t", Assert.Single(week.Days.Single(d => d.Date == new DateOnly(2026, 7, 22)).Lines));
        Assert.Empty(week.Days.Single(d => d.Date == new DateOnly(2026, 7, 21)).Lines);
    }

    [Fact]
    public void BuildStudentLabels_UsesSurnameOnlyUnlessThereIsACollision()
    {
        var labels = WeeklyCalendarLayout.BuildStudentLabels(["田中 太郎", "山田 花子"]);
        Assert.Equal("田中", labels["田中 太郎"]);
        Assert.Equal("山田", labels["山田 花子"]);
    }

    [Fact]
    public void BuildStudentLabels_AppendsGivenNameInitialWhenSurnamesCollide()
    {
        var labels = WeeklyCalendarLayout.BuildStudentLabels(["田中 太郎", "田中 次郎"]);
        Assert.Equal("田中太", labels["田中 太郎"]);
        Assert.Equal("田中次", labels["田中 次郎"]);
    }
}
