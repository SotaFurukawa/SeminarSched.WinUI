using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Scheduling;

/// <summary>
/// <see cref="UnassignedDueToRegularTeacherPriority"/> and <see cref="UnassignedWithNoQualifiedTeacher"/>
/// are diagnostic subsets of <see cref="UnassignedLessons"/> (see
/// SqliteScheduleRunService.DiagnoseUnassignedDemands), not additional counts on top of it - every
/// unassigned session falls into at most one of these two buckets, or into neither (a generic "lost the
/// competition for a scarce slot within the given time" case that has no single attributable cause).
/// </summary>
public sealed record ScheduleRunSummary(
    int PlacedLessons,
    int UnassignedLessons,
    TimeSpan Elapsed,
    string StrategyLabel = "",
    bool WasExtended = false,
    int UnassignedDueToRegularTeacherPriority = 0,
    int UnassignedWithNoQualifiedTeacher = 0);

public interface IScheduleRunService
{
    Task<ScheduleRunSummary> RunAsync(string projectPath, TimeSpan maximumDuration, CancellationToken cancellationToken = default);

    Task<ScheduleRunSummary> RunAsync(
        string projectPath,
        OptimizationProfile profile,
        OptimizationRunControl control,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
