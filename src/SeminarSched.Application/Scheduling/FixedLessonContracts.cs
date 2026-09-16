namespace SeminarSched.Application.Scheduling;

public sealed record LessonRequestOption(long Id, long StudentId, string Label);
public sealed record TeacherOption(long Id, string Label);
public sealed record ScheduleSlotOption(long OpenDateId, long TimeSlotId, string Label);
public sealed record FixedLesson(long Id, long LessonRequestId, long TeacherId, long OpenDateId, long TimeSlotId, string Label);

public interface IFixedLessonService
{
    Task<IReadOnlyList<LessonRequestOption>> GetRequestsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherOption>> GetTeachersAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleSlotOption>> GetSlotsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FixedLesson>> GetFixedLessonsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task AddAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task RemoveAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default);
}
