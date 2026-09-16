namespace SeminarSched.Optimization.Execution;

public sealed record OptimizationRunResult<TSolution>(
    ScheduleCandidate<TSolution>? Best,
    IReadOnlyList<ScheduleCandidate<TSolution>> Candidates,
    int ImprovementCount,
    bool AcceptedEarly,
    bool StoppedForStagnation,
    TimeSpan Elapsed);
