using SeminarSched.Reporting.Models;

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
    /// （Python版person_names.pyのcompact_person_name_map相当）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildStudentLabels(IEnumerable<string> fullNames) => BuildCompactNameLookup(fullNames);

    /// <summary>
    /// 講師名は同姓であっても常に姓のみで表示する（ユーザー指示: 講師名は全て苗字のみ）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildTeacherLabels(IEnumerable<string> fullNames) =>
        fullNames.Distinct().ToDictionary(name => name, Surname);

    /// <summary>姓のみを取り出す（"姓 名"形式を前提。空白が無ければ全体を姓として扱う）。</summary>
    public static string Surname(string fullName)
    {
        var spaceIndex = fullName.IndexOf(' ');
        return spaceIndex < 0 ? fullName : fullName[..spaceIndex];
    }

    /// <summary>
    /// ユーザー要望（checkpoint112）「集団授業の担当講師のその時間を『集団』と表示してほしい」への
    /// 対応。集団授業の担当講師の授業時間帯（コマに縛られない自由な開始・終了時刻）と、各コマの時刻
    /// 定義を突き合わせ、時間帯が重なるコマをOverviewGroupLessonCellとして解決する。全体時間割・
    /// 講師配布ページの両方（Excel・PDF）で同じ解決結果を使う。
    /// </summary>
    public static IReadOnlyList<OverviewGroupLessonCell> ResolveOverviewGroupLessonCells(ScheduleReport report, IReadOnlyDictionary<string, string> teacherLabels)
    {
        var result = new List<OverviewGroupLessonCell>();
        foreach (var attendance in report.GroupLessonTeacherAttendances)
        {
            var teacherLabel = teacherLabels[attendance.Teacher];
            foreach (var slot in report.SlotDefinitions)
            {
                var slotStart = TimeOnly.ParseExact(slot.StartTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var slotEnd = TimeOnly.ParseExact(slot.EndTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                if (attendance.StartTime < slotEnd && slotStart < attendance.EndTime)
                    result.Add(new OverviewGroupLessonCell(attendance.Date, teacherLabel, slot.Label));
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildCompactNameLookup(IEnumerable<string> fullNames)
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
