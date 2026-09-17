namespace SeminarSched.Reporting.Layout;

public sealed record OverviewCard(string Grade, string SubjectShortName, string Student);

public sealed record OverviewCell(string SlotLabel, IReadOnlyList<OverviewCard> Cards);

public sealed record OverviewTeacherColumn(string TeacherName, IReadOnlyList<OverviewCell> Cells);

public sealed record OverviewDay(DateOnly Date, IReadOnlyList<OverviewTeacherColumn> Teachers);

public sealed record OverviewWeek(DateOnly SundayStart, IReadOnlyList<OverviewDay> Days);

public sealed record OverviewAssignment(DateOnly Date, string Teacher, string SlotLabel, string Grade, string SubjectShortName, string Student);

public sealed record OverviewGrid(IReadOnlyList<string> SlotLabels, IReadOnlyList<OverviewWeek> Weeks);

public static class OverviewGridLayout
{
    /// <summary>
    /// Python版6.1の全体時間割仕様: 日曜始まり・土曜終わりの週単位、休校日を除いて日付を横に並べ、
    /// その日に出勤予定（＝その日に配置がある）講師だけを列として表示する。
    /// </summary>
    public static OverviewGrid Build(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> openDates, IReadOnlyList<string> slotLabels, IReadOnlyList<OverviewAssignment> assignments)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        var byDate = assignments.ToLookup(a => a.Date);
        var firstSunday = start.AddDays(-(int)start.DayOfWeek);
        var lastSaturday = end.AddDays(6 - (int)end.DayOfWeek);
        var weeks = new List<OverviewWeek>();
        for (var weekStart = firstSunday; weekStart <= lastSaturday; weekStart = weekStart.AddDays(7))
        {
            var days = new List<OverviewDay>();
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                if (date < start || date > end || !openDates.Contains(date)) continue;
                var dayAssignments = byDate[date].ToArray();
                var teachers = dayAssignments.Select(a => a.Teacher).Distinct().OrderBy(t => t, StringComparer.Ordinal)
                    .Select(teacherName =>
                    {
                        var cells = slotLabels.Select(slot => new OverviewCell(
                            slot,
                            dayAssignments.Where(a => a.Teacher == teacherName && a.SlotLabel == slot)
                                .Select(a => new OverviewCard(a.Grade, a.SubjectShortName, a.Student)).ToArray()))
                            .ToArray();
                        return new OverviewTeacherColumn(teacherName, cells);
                    }).ToArray();
                days.Add(new OverviewDay(date, teachers));
            }
            if (days.Count > 0) weeks.Add(new OverviewWeek(weekStart, days));
        }
        return new OverviewGrid(slotLabels, weeks);
    }
}
