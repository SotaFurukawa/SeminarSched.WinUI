using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

public sealed record OptimizationProgress(
    TimeSpan Elapsed,
    TimeSpan MaximumTime,
    OptimizationStageKind Stage,
    OptimizationStrategyKind Strategy,
    int StrategiesCompleted,
    int StrategiesTotal,
    int ImprovementCount,
    ScheduleEvaluation? BestEvaluation);
