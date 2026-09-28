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

    // ユーザー要望（checkpoint112）「集団授業のクラスに担当講師（任意）を割り当て...割り当てられた
    // 講師の全体時間割の該当コマは『集団』と表示される」への対応の検証。この講師はその日に個別の
    // Assignmentを1件も持たないため、集団授業のセルが無ければそもそも列自体に出てこない点も含めて
    // 確認する。
    [Fact]
    public void Build_AddsAColumnForATeacherWhoOnlyHasAGroupLessonThatDayAndMarksTheOverlappingCell()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20) };
        var groupLessons = new[] { new OverviewGroupLessonCell(new DateOnly(2026, 7, 20), "佐藤t", "1限 09:00-10:00") };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20), openDates, ["1限 09:00-10:00"], [], null, groupLessons);

        var day = Assert.Single(Assert.Single(grid.Weeks).Days);
        var teacher = Assert.Single(day.Teachers);
        Assert.Equal("佐藤t", teacher.TeacherName);
        var cell = Assert.Single(teacher.Cells);
        Assert.True(cell.IsGroupLesson);
        Assert.False(cell.Unavailable);
        Assert.Empty(cell.Cards);
    }

    // TeacherUnavailability由来のOverviewUnavailabilityと同じ(日付,講師,コマ)が同時に渡された場合
    // （担当講師の割り当てはTeacherUnavailabilityへも自動反映されるため実際に起こりうる）、集団授業の
    // 表示が優先され、灰色の勤務不可表示にはならないことを確認する。
    [Fact]
    public void Build_PrefersGroupLessonOverGenericUnavailableForTheSameCell()
    {
        var openDates = new HashSet<DateOnly> { new(2026, 7, 20) };
        var unavailabilities = new[] { new OverviewUnavailability(new DateOnly(2026, 7, 20), "佐藤t", "1限 09:00-10:00") };
        var groupLessons = new[] { new OverviewGroupLessonCell(new DateOnly(2026, 7, 20), "佐藤t", "1限 09:00-10:00") };
        var grid = OverviewGridLayout.Build(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20), openDates, ["1限 09:00-10:00"], [], unavailabilities, groupLessons);

        var cell = Assert.Single(Assert.Single(Assert.Single(Assert.Single(grid.Weeks).Days).Teachers).Cells);
        Assert.True(cell.IsGroupLesson);
        Assert.False(cell.Unavailable);
    }
}
