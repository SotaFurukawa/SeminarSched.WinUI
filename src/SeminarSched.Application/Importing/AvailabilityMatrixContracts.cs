namespace SeminarSched.Application.Importing;

public enum AvailabilityEntityKind { Student, Teacher }

public sealed record AvailabilityEntityOption(long Id, string Label);

// ユーザー要望（checkpoint111）「可用性の手動編集について、これをカレンダーで変更することはできないか。
// ...複数日付・複数コマへ一括適用というのは撤廃し、ここは一人一人入力していく形で」への対応。
// 選択中の1名（生徒 or 講師）について、講習期間内の全開講日×その日の全コマの現在値を1回でまとめて
// 取得する（日付ごとに個別クエリを繰り返す旧GetDayMatrixAsyncの方式だと、開講日数が多いプロジェクトで
// 低速なPCでは体感できる遅延になりうるため）。
public sealed record AvailabilityCalendarSlot(long TimeSlotId, string Label, int Level);
public sealed record AvailabilityCalendarDay(long OpenDateId, DateOnly Date, IReadOnlyList<AvailabilityCalendarSlot> Slots);
public sealed record AvailabilityCalendar(IReadOnlyList<AvailabilityCalendarDay> Days);

public interface IAvailabilityMatrixService
{
    Task<IReadOnlyList<AvailabilityEntityOption>> GetEntitiesAsync(string projectPath, AvailabilityEntityKind kind, CancellationToken cancellationToken = default);
    Task<AvailabilityCalendar> GetCalendarAsync(string projectPath, AvailabilityEntityKind kind, long entityId, CancellationToken cancellationToken = default);
    Task SetLevelAsync(string projectPath, AvailabilityEntityKind kind, long entityId, long openDateId, long timeSlotId, int level, CancellationToken cancellationToken = default);
}
