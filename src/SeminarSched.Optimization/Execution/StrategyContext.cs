namespace SeminarSched.Optimization.Execution;

public sealed record StrategyContext<TInput, TSolution>(
    TInput Input,
    TimeSpan TimeBudget,
    ScheduleCandidate<TSolution>? Hint,
    IProgress<OptimizationProgress>? Progress);
