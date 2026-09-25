using SeminarSched.Reporting.Layout;

namespace SeminarSched.Infrastructure.Tests;

public sealed class OverviewGridLayoutTests
{
    [Fact]
    public void Build_MarksNonOpenDatesWithinRangeAsClosedAndOnlyShowsTeachersWithAssignmentsThatDay()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20), new(2026, 7, 22) };
        var assignments = new[]
        {
            new OverviewAssignment(new DateOnly(2026, 7, 20), "田中t", "1限 09:00-10:00", "中2", "数", "山田"),
        };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22), openDates, ["1限 09:00-10:00"], assignments);

        var week = Assert.Single(grid.Weeks);
        Assert.Equal(3, week.Days.Count);
        var monday = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 20));
        Assert.False(monday.IsClosed);
        var teacher = Assert.Single(monday.Teachers);
        Assert.Equal("田中t", teacher.TeacherName);
        var card = Assert.Single(Assert.Single(teacher.Cells).Cards);
        Assert.Equal("山田", card.Student);

        var tuesday = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 21));
        Assert.True(tuesday.IsClosed);
        Assert.Empty(tuesday.Teachers);

        var wednesday = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 22));
        Assert.False(wednesday.IsClosed);
        Assert.Empty(wednesday.Teachers);
    }

    [Fact]
    public void Build_MarksNonOpenDateWithinTheWeekAsClosedInsteadOfOmittingIt()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20) };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 19), new DateOnly(2026, 7, 25), openDates, [], []);
        var week = Assert.Single(grid.Weeks);
        Assert.Equal(7, week.Days.Count);
        var openDay = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 20));
        Assert.False(openDay.IsClosed);
        Assert.All(week.Days.Where(d => d.Date != new DateOnly(2026, 7, 20)), d => Assert.True(d.IsClosed));
    }
}
