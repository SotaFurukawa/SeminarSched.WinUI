using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Scheduling;

public sealed record ScheduleRunSummary(int PlacedLessons, int UnassignedLessons, TimeSpan Elapsed, string StrategyLabel = "");

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
