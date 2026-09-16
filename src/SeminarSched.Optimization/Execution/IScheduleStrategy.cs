using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

public interface IScheduleStrategy<TInput, TSolution>
{
    OptimizationStrategyKind Kind { get; }

    Task<ScheduleCandidate<TSolution>?> ExecuteAsync(
        StrategyContext<TInput, TSolution> context,
        CancellationToken cancellationToken);
}
