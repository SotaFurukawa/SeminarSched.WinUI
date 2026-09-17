using SeminarSched.Reporting.Layout;

namespace SeminarSched.Infrastructure.Tests;

public sealed class OverviewGridLayoutTests
{
    [Fact]
    public void Build_ExcludesClosedDaysAndOnlyShowsTeachersWithAssignmentsThatDay()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20), new(2026, 7, 22) };
        var assignments = new[]
        {
            new OverviewAssignment(new DateOnly(2026, 7, 20), "田中t", "1限 09:00-10:00", "中2", "数", "山田"),
        };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22), openDates, ["1限 09:00-10:00"], assignments);

        var week = Assert.Single(grid.Weeks);
        Assert.Equal(2, week.Days.Count);
        var monday = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 20));
        var teacher = Assert.Single(monday.Teachers);
        Assert.Equal("田中t", teacher.TeacherName);
        var card = Assert.Single(Assert.Single(teacher.Cells).Cards);
        Assert.Equal("山田", card.Student);

        var wednesday = week.Days.Single(d => d.Date == new DateOnly(2026, 7, 22));
        Assert.Empty(wednesday.Teachers);
    }

    [Fact]
    public void Build_OmitsClosedDayEvenWhenWithinTheWeek()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20) };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 19), new DateOnly(2026, 7, 25), openDates, [], []);
        var week = Assert.Single(grid.Weeks);
        var day = Assert.Single(week.Days);
        Assert.Equal(new DateOnly(2026, 7, 20), day.Date);
    }
}
