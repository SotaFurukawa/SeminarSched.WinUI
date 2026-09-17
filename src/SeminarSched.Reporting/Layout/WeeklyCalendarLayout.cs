namespace SeminarSched.Reporting.Layout;

public sealed record CalendarCell(DateOnly Date, IReadOnlyList<string> Lines);

public sealed record CalendarWeek(DateOnly SundayStart, IReadOnlyList<CalendarCell> Days);

public static class WeeklyCalendarLayout
{
    public static readonly string[] WeekdayHeaders = ["日", "月", "火", "水", "木", "金", "土"];

    public static IReadOnlyList<CalendarWeek> Build(DateOnly start, DateOnly end, IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> linesByDate)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        var firstSunday = start.AddDays(-(int)start.DayOfWeek);
        var lastSaturday = end.AddDays(6 - (int)end.DayOfWeek);
        var weeks = new List<CalendarWeek>();
        for (var weekStart = firstSunday; weekStart <= lastSaturday; weekStart = weekStart.AddDays(7))
        {
            var days = new List<CalendarCell>(7);
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                days.Add(new CalendarCell(date, linesByDate.TryGetValue(date, out var lines) ? lines : []));
            }
            weeks.Add(new CalendarWeek(weekStart, days));
        }
        return weeks;
    }

    /// <summary>
    /// Python版の表記規則: 表示は姓のみ。同姓の生徒が複数いる場合だけ名の先頭1文字を付けて区別する。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildStudentLabels(IEnumerable<string> fullNames)
    {
        var names = fullNames.Distinct().ToArray();
        var parsed = names.Select(name =>
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (Name: name, Surname: parts.Length > 0 ? parts[0] : name, Given: parts.Length > 1 ? string.Concat(parts[1..]) : "");
        }).ToArray();
        var surnameCounts = parsed.GroupBy(x => x.Surname).ToDictionary(g => g.Key, g => g.Count());
        return parsed.ToDictionary(
            x => x.Name,
            x => surnameCounts[x.Surname] > 1 && x.Given.Length > 0 ? x.Surname + x.Given[..1] : x.Surname);
    }
}
