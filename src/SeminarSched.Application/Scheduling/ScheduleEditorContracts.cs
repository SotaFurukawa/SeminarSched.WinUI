namespace SeminarSched.Application.Scheduling;

public sealed record ScheduleAssignmentItem(
    long Id,
    long LessonRequestId,
    long TeacherId,
    long OpenDateId,
    long TimeSlotId,
    bool IsLocked,
    bool IsManual,
    string Source,
    string Label)
{
    public override string ToString() => $"{Label}　[{(IsManual ? "手動" : "自動")}{(IsLocked ? "・ロック" : "")}]";
}

public interface IScheduleEditorService
{
    Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task AddManualAsync(string projectPath, long lessonRequestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, CancellationToken cancellationToken = default);
    Task RemoveManualAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default);
    Task SetLockedAsync(string projectPath, long assignmentId, bool isLocked, CancellationToken cancellationToken = default);
    Task ResetAutomaticAsync(string projectPath, CancellationToken cancellationToken = default);
}
