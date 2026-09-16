namespace SeminarSched.Application.Scheduling;

public sealed record ScheduleRunSummary(int PlacedLessons,int UnassignedLessons,TimeSpan Elapsed);
public interface IScheduleRunService
{
    Task<ScheduleRunSummary> RunAsync(string projectPath,TimeSpan maximumDuration,CancellationToken cancellationToken=default);
}
