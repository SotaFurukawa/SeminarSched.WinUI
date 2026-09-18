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

public sealed record OpenDateOption(long Id, string Label);

public sealed record BoardSlotRow(long TimeSlotId, string Label, int SortOrder);

public sealed record BoardTeacherColumn(long TeacherId, string Label);

public sealed record BoardCard(long AssignmentId, long TeacherId, long TimeSlotId, long StudentId, string StudentLabel, string SubjectLabel, bool IsManual, bool IsLocked)
{
    public override string ToString() => $"{StudentLabel}　{SubjectLabel}{(IsManual ? "　[手動]" : "")}{(IsLocked ? "　[ロック]" : "")}";
}

public sealed record BoardCell(long TimeSlotId, long TeacherId, bool Blocked, IReadOnlyList<BoardCard> Cards);

public sealed record ScheduleBoard(IReadOnlyList<BoardSlotRow> Slots, IReadOnlyList<BoardTeacherColumn> Teachers, IReadOnlyList<BoardCell> Cells)
{
    public BoardCell? Cell(long timeSlotId, long teacherId) => Cells.FirstOrDefault(c => c.TimeSlotId == timeSlotId && c.TeacherId == teacherId);
}

public sealed record UnplacedSessionOption(long LessonRequestId, string Label, int Remaining)
{
    public override string ToString() => $"{Label}　(残り{Remaining}回)";
}

public sealed record AssignmentSnapshotRow(long Id, long LessonRequestId, long TeacherId, long OpenDateId, long TimeSlotId, bool IsLocked, string Source, int SessionIndex, long? OptimizationRunId, bool IsManual, string Note);

public sealed record TeacherUnavailabilitySnapshotRow(long TeacherId, long OpenDateId, long TimeSlotId);

public sealed record ScheduleSnapshot(IReadOnlyList<AssignmentSnapshotRow> Assignments, IReadOnlyList<TeacherUnavailabilitySnapshotRow> TeacherUnavailabilities);

public interface IScheduleEditorService
{
    Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task AddManualAsync(string projectPath, long lessonRequestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, CancellationToken cancellationToken = default);
    Task RemoveManualAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default);
    Task SetLockedAsync(string projectPath, long assignmentId, bool isLocked, CancellationToken cancellationToken = default);
    Task ResetAutomaticAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenDateOption>> GetOpenDatesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ScheduleBoard> GetBoardAsync(string projectPath, long openDateId, IReadOnlyCollection<long> extraTeacherIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnplacedSessionOption>> GetUnplacedSessionsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task MoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableAsync(string projectPath, long teacherId, long openDateId, long timeSlotId, bool unavailable, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableManyAsync(string projectPath, long openDateId, IReadOnlyCollection<(long TeacherId, long TimeSlotId)> targets, bool unavailable, CancellationToken cancellationToken = default);
    Task<ScheduleSnapshot> CaptureSnapshotAsync(string projectPath, CancellationToken cancellationToken = default);
    Task RestoreSnapshotAsync(string projectPath, ScheduleSnapshot snapshot, CancellationToken cancellationToken = default);
}
