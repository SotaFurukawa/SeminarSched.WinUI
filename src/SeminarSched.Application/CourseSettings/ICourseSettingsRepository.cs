using SeminarSched.Domain.CourseSettings;

namespace SeminarSched.Application.CourseSettings;

public interface ICourseSettingsRepository
{
    Task<IReadOnlyList<TimeSlot>> GetTimeSlotsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<TimeSlot> SaveTimeSlotAsync(string projectPath, TimeSlot slot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseDay>> GetCourseDaysAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveCourseDayAsync(string projectPath, CourseDay day, CancellationToken cancellationToken = default);
}
