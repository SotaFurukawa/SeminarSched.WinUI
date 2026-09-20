using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

/// <summary>
/// <see cref="ProgressWeight"/>/<see cref="StrategyWeight"/>/<see cref="StrategyBudget"/>/
/// <see cref="IsStrategyStarting"/> exist so callers (WinUI's OptimizationRunState) can compute a
/// percentage from actual planned work completed, instead of elapsed-time-vs-MaximumTime. The latter
/// makes little sense here: CP-SAT frequently proves a (sub-)problem optimal and returns well before
/// its allotted time slice is used, so a run can finish in a couple of minutes even at the "Highest"
/// preset's 60-minute nominal budget - dividing elapsed time by that nominal total then jumps from a
/// low single-digit percentage straight to 100% the instant the run ends, with nothing in between.
/// Each stage's <see cref="Profiles.OptimizationStageDefinition.BudgetShare"/> is instead treated as
/// that stage's share of "1.0 unit of total work", split evenly across its strategies; ProgressWeight
/// is the cumulative share considered done as of this report (before this strategy starts, or
/// including it once it finishes), and StrategyWeight/StrategyBudget let the caller interpolate
/// smoothly between reports while a single strategy is still running.
/// </summary>
public sealed record OptimizationProgress(
    TimeSpan Elapsed,
    TimeSpan MaximumTime,
    OptimizationStageKind Stage,
    OptimizationStrategyKind Strategy,
    int StrategiesCompleted,
    int StrategiesTotal,
    int ImprovementCount,
    ScheduleEvaluation? BestEvaluation,
    double ProgressWeight = 0,
    double StrategyWeight = 0,
    TimeSpan StrategyBudget = default,
    bool IsStrategyStarting = false);
