namespace SeminarSched.Reporting.Layout;

public enum HandoutDayKind { Open, ClosedDay, OutOfRange }

public sealed record HandoutDayColumn(DateOnly Date, HandoutDayKind Kind);
public sealed record HandoutSlotRow(string SlotLabel, IReadOnlyList<string?> LessonTextByDay);
public sealed record HandoutWeekBlock(DateOnly SundayStart, bool FullyClosed, IReadOnlyList<HandoutDayColumn> Days, IReadOnlyList<HandoutSlotRow> SlotRows);

/// <summary>
/// Python版distribution_builder.pyの生徒配布用個人calendar(9列A:I、月・曜日・日付の見出し行＋コマ行)相当の
/// 日曜始まり週block一覧を作る。生徒配布・講師配布（学年順）・講師配布（講師別）の3レポートが共通で使う。
/// 週全体が休校日の週はFullyClosed=trueとし、呼び出し側は1行の「休校日」帯へ描画する。
/// Python版は範囲外・休校日が連続する列をまとめて1セルへ結合するが、ここでは列ごとに同じ文言を
/// 繰り返す簡略版とする（列見出し・コマ・科目などのテキスト内容自体はPython版と同一）。
/// </summary>
public static class HandoutPageLayout
{
    public static IReadOnlyList<HandoutWeekBlock> Build(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> openDates, IReadOnlyList<string> slotLabels, IReadOnlyDictionary<(DateOnly Date, string SlotLabel), string> lessonTextByDateSlot)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        var firstSunday = start.AddDays(-(int)start.DayOfWeek);
        var lastSaturday = end.AddDays(6 - (int)end.DayOfWeek);
        var weeks = new List<HandoutWeekBlock>();
        for (var weekStart = firstSunday; weekStart <= lastSaturday; weekStart = weekStart.AddDays(7))
        {
            var days = new List<HandoutDayColumn>(7);
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                var kind = date < start || date > end ? HandoutDayKind.OutOfRange : openDates.Contains(date) ? HandoutDayKind.Open : HandoutDayKind.ClosedDay;
                days.Add(new HandoutDayColumn(date, kind));
            }
            var fullyClosed = days.All(d => d.Kind != HandoutDayKind.Open);
            var slotRows = fullyClosed
                ? Array.Empty<HandoutSlotRow>()
                : slotLabels.Select(slot => new HandoutSlotRow(slot, days.Select(d => d.Kind == HandoutDayKind.Open && lessonTextByDateSlot.TryGetValue((d.Date, slot), out var text) ? text : null).ToArray())).ToArray();
            weeks.Add(new HandoutWeekBlock(weekStart, fullyClosed, days, slotRows));
        }
        return weeks;
    }
}
