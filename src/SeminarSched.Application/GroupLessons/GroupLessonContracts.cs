using SeminarSched.Domain.GroupLessons;

namespace SeminarSched.Application.GroupLessons;

public sealed record GroupLessonSessionOption(long Id, long ClassId, long OpenDateId, long TimeSlotId, string DateLabel, string SlotLabel)
{
    public override string ToString() => $"{DateLabel} {SlotLabel}";
}

public sealed record GroupLessonEnrollmentCandidate(long StudentId, string ExternalId, string Name, string Grade, bool Enrolled)
{
    public string Display => $"{ExternalId}　{Name}　（{Grade}）";
    public override string ToString() => $"{(Enrolled ? "✓ " : "")}{Display}";
}

public interface IGroupLessonService
{
    Task<IReadOnlyList<GroupLessonClass>> GetClassesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<GroupLessonClass> SaveClassAsync(string projectPath, GroupLessonClass value, CancellationToken cancellationToken = default);
    Task DeleteClassAsync(string projectPath, long classId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupLessonSessionOption>> GetSessionsAsync(string projectPath, long classId, CancellationToken cancellationToken = default);
    Task AddSessionAsync(string projectPath, long classId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task RemoveSessionAsync(string projectPath, long sessionId, CancellationToken cancellationToken = default);

    // allowOtherGradesがfalseの場合はvalue.Gradeと一致する生徒のみ、trueの場合は全学年の生徒を返す。
    Task<IReadOnlyList<GroupLessonEnrollmentCandidate>> GetEnrollmentCandidatesAsync(string projectPath, long classId, CancellationToken cancellationToken = default);
    Task SetEnrollmentAsync(string projectPath, long classId, long studentId, bool enrolled, CancellationToken cancellationToken = default);
}
