namespace SeminarSched.Application.Scheduling;

public sealed record LessonRequestOption(long Id, long StudentId, string Label);
public sealed record TeacherOption(long Id, string Label);
public sealed record ScheduleSlotOption(long OpenDateId, long TimeSlotId, string Label);

/// <summary>
/// 時間割編集フォーム向けの選択肢一覧（生徒・科目・講師・日時）。Python版に合わせ、事前確定は
/// 専用の永続状態を持たず、<see cref="IScheduleEditorService.AddManualAsync"/>にisLocked:trueを
/// 渡すのと同じ「手動配置＋ロック」として扱う（<see cref="ScheduleEditorContracts"/>参照）。
/// </summary>
public interface IFixedLessonService
{
    Task<IReadOnlyList<LessonRequestOption>> GetRequestsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherOption>> GetTeachersAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleSlotOption>> GetSlotsAsync(string projectPath, CancellationToken cancellationToken = default);
}
