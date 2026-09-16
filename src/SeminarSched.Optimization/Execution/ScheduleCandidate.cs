using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

public sealed record ScheduleCandidate<TSolution>(
    TSolution Solution,
    ScheduleEvaluation Evaluation,
    OptimizationStrategyKind Strategy,
    TimeSpan Elapsed);
