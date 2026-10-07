namespace SeminarSched.Reporting.Layout;

public sealed record OverviewCard(string Grade, string SubjectShortName, string Student, bool OneToOneRequired = false, bool IsLocked = false, bool IsManual = false);

/// <summary>IsGroupLesson=trueの場合、この講師はこのコマの時間帯に集団授業を担当しており、
/// レンダラー側は個別指導のカードの代わりに黒塗り「集団」表示にする（Cardsは常に空）。
/// ユーザー要望（checkpoint155）「集団授業と個別指導の間の空き時間の最小値」にマイナス値を設定した
/// 場合、個別指導がこの講師の集団授業の開講時間そのものと重なった状態のまま配置されることがある
/// （本来は数分ずらして運用する前提の許容範囲）。HasOverlapWarning=trueはこの状態を示し、
/// レンダラー側はカードを黄色で警告表示する（Cardsは通常どおり中身を持つ）。</summary>
public sealed record OverviewCell(string SlotLabel, IReadOnlyList<OverviewCard> Cards, bool Unavailable = false, bool IsGroupLesson = false, bool HasOverlapWarning = false);

public sealed record OverviewTeacherColumn(string TeacherName, IReadOnlyList<OverviewCell> Cells);

/// <summary>IsClosed=trueの場合、Teachersは空でありレンダラー側は休校日専用の列（縦書きラベル・黒塗り）
/// として扱う。ユーザー要望（checkpoint105）「休校日の列を追加。例えば、7月27日、7月29日の間に
/// 7月28日の休校日があった場合、その間に列を追加」への対応。</summary>
public sealed record OverviewDay(DateOnly Date, IReadOnlyList<OverviewTeacherColumn> Teachers, bool IsClosed = false);

public sealed record OverviewWeek(DateOnly SundayStart, IReadOnlyList<OverviewDay> Days);

public sealed record OverviewAssignment(DateOnly Date, string Teacher, string SlotLabel, string Grade, string SubjectShortName, string Student, bool OneToOneRequired = false, bool IsLocked = false, bool IsManual = false);

public sealed record OverviewUnavailability(DateOnly Date, string Teacher, string SlotLabel);

/// <summary>集団授業の担当講師が、その授業時間帯と重なるコマを担当していることを示す1件
/// （呼び出し側が自由な開始・終了時刻とコマの時刻を突き合わせ済みで、コマ単位に解決してから渡す）。</summary>
public sealed record OverviewGroupLessonCell(DateOnly Date, string Teacher, string SlotLabel);

public sealed record OverviewGrid(IReadOnlyList<string> SlotLabels, IReadOnlyList<OverviewWeek> Weeks);

public static class OverviewGridLayout
{
    /// <summary>
    /// Python版6.1の全体時間割仕様: 日曜始まり・土曜終わりの週単位、休校日を除いて日付を横に並べ、
    /// その日に出勤予定（＝その日に配置がある）講師だけを列として表示する。配置が無いコマのうち、
    /// その講師がその日その時間帯に出勤不可(TeacherUnavailability)であるセルはUnavailable=trueとする。
    /// Python版の実出力では日程範囲内の週は（該当日・講師が1件も無い週も含めて）すべてsheetとして
    /// 生成される（「対象となる開校日・出勤予定講師がありません」のplaceholder表示）ため、Days.Countが
    /// 0の週もWeeksへ含める。
    /// </summary>
    public static OverviewGrid Build(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> openDates, IReadOnlyList<string> slotLabels, IReadOnlyList<OverviewAssignment> assignments, IReadOnlyList<OverviewUnavailability>? unavailabilities = null, IReadOnlyList<OverviewGroupLessonCell>? groupLessons = null)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        var byDate = assignments.ToLookup(a => a.Date);
        var unavailableCells = (unavailabilities ?? []).Select(u => (u.Date, u.Teacher, u.SlotLabel)).ToHashSet();
        var groupLessonsByDate = (groupLessons ?? []).ToLookup(g => g.Date);
        var firstSunday = start.AddDays(-(int)start.DayOfWeek);
        var lastSaturday = end.AddDays(6 - (int)end.DayOfWeek);
        var weeks = new List<OverviewWeek>();
        for (var weekStart = firstSunday; weekStart <= lastSaturday; weekStart = weekStart.AddDays(7))
        {
            var days = new List<OverviewDay>();
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                if (date < start || date > end) continue;
                if (!openDates.Contains(date))
                {
                    // 講習期間内([start,end])だが開講日ではない日＝休校日として、通常の講師列の
                    // 代わりに専用の休校日列を1つ挟む（IsClosed=true、Teachersは空）。
                    days.Add(new OverviewDay(date, [], IsClosed: true));
                    continue;
                }
                var dayAssignments = byDate[date].ToArray();
                var dayGroupLessons = groupLessonsByDate[date].ToArray();
                var teachers = dayAssignments.Select(a => a.Teacher).Concat(dayGroupLessons.Select(g => g.Teacher)).Distinct().OrderBy(t => t, StringComparer.Ordinal)
                    .Select(teacherName =>
                    {
                        var cells = slotLabels.Select(slot =>
                        {
                            var cards = dayAssignments.Where(a => a.Teacher == teacherName && a.SlotLabel == slot)
                                .Select(a => new OverviewCard(a.Grade, a.SubjectShortName, a.Student, a.OneToOneRequired, a.IsLocked, a.IsManual)).ToArray();
                            var overlapsGroupLesson = dayGroupLessons.Any(g => g.Teacher == teacherName && g.SlotLabel == slot);
                            var isGroupLesson = cards.Length == 0 && overlapsGroupLesson;
                            // ユーザー要望（checkpoint155）。個別指導のカードがあり、かつ同じコマがこの講師の
                            // 集団授業の開講時間（gap=0の文字どおりの時間帯）とも重なっている＝マイナスの
                            // 空き時間設定により重なりを許容されたまま配置された状態。警告として扱う。
                            var hasOverlapWarning = cards.Length > 0 && overlapsGroupLesson;
                            return new OverviewCell(slot, cards, cards.Length == 0 && !isGroupLesson && unavailableCells.Contains((date, teacherName, slot)), isGroupLesson, hasOverlapWarning);
                        }).ToArray();
                        return new OverviewTeacherColumn(teacherName, cells);
                    }).ToArray();
                days.Add(new OverviewDay(date, teachers));
            }
            weeks.Add(new OverviewWeek(weekStart, days));
        }
        return new OverviewGrid(slotLabels, weeks);
    }
}
