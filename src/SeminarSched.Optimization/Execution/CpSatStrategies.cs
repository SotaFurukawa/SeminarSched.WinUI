using System.Diagnostics;
using SeminarSched.Optimization.Core;
using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

/// <summary>
/// Shared plumbing for every CP-SAT-backed strategy: run the solver with whatever
/// <see cref="CpSatSolveOptions"/> the concrete strategy asks for, and turn the result into a
/// <see cref="ScheduleCandidate{TSolution}"/> - or null if this attempt found nothing usable
/// (Infeasible/ModelInvalid/Unknown), which <see cref="ScheduleOptimizer{TInput,TSolution}"/>
/// treats as "this strategy contributed no candidate" rather than aborting the whole run.
/// Cancellation (strategy timeout, user cancel, or "accept current best") is left to propagate
/// as OperationCanceledException, which the optimizer already handles separately.
/// </summary>
public abstract class CpSatStrategyBase : IScheduleStrategy<ScheduleProblem, ScheduleSolution>
{
    public abstract OptimizationStrategyKind Kind { get; }

    protected abstract CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context);

    public async Task<ScheduleCandidate<ScheduleSolution>?> ExecuteAsync(
        StrategyContext<ScheduleProblem, ScheduleSolution> context,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var options = BuildOptions(context);
        try
        {
            var solution = await new CpSatScheduleSolver().SolveAsync(context.Input, options, cancellationToken).ConfigureAwait(false);
            var evaluation = ScheduleEvaluationCalculator.Evaluate(context.Input, solution);
            return new ScheduleCandidate<ScheduleSolution>(solution, evaluation, Kind, watch.Elapsed);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // Measured directly against a real 57-student/83-request import (41,575 candidate variables):
    // num_search_workers:1 (true single-threaded search) never even reached a feasible solution in
    // 40s, while num_search_workers:0 (the solver's own "auto" choice, what every strategy used
    // before this multi-strategy rewrite) solved it in 32.4s, and explicit counts matching the
    // machine's core count did as well or better (23-31s). So no strategy here ever asks for a
    // single worker - the "diversity" between strategies comes from the seed and search_branching
    // parameters instead, never from artificially crippling parallelism.
    protected static int DefaultWorkerCount => Math.Max(1, Environment.ProcessorCount);

    protected static CpSatSolveOptions ColdStart(StrategyContext<ScheduleProblem, ScheduleSolution> context, int seed, string? searchBranching = null) =>
        new(context.TimeBudget, seed, DefaultWorkerCount, searchBranching);

    /// <summary>
    /// Warm-starts from the previous stage's best candidate, if any. Falls back to a plain cold
    /// start with the given seed when there is no hint yet (e.g. every strategy in the very first
    /// stage, or a stage whose predecessor produced no usable candidate at all).
    /// </summary>
    protected static CpSatSolveOptions WarmStart(StrategyContext<ScheduleProblem, ScheduleSolution> context, int seed, IReadOnlySet<long>? freeRequestIds = null) =>
        context.Hint is null
            ? new CpSatSolveOptions(context.TimeBudget, seed, DefaultWorkerCount)
            : new CpSatSolveOptions(context.TimeBudget, seed, DefaultWorkerCount, Hint: context.Hint.Solution, FreeRequestIds: freeRequestIds);
}

/// <summary>Plain CP-SAT, the safe baseline every quality level starts from.</summary>
public sealed class StandardCpSatStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.StandardCpSat;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) => ColdStart(context, seed: 1);
}

/// <summary>Same model, different random seed - a classic multi-start diversification.</summary>
public sealed class SeededCpSatAStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.SeededCpSatA;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) => ColdStart(context, seed: 7);
}

public sealed class SeededCpSatBStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.SeededCpSatB;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) => ColdStart(context, seed: 42);
}

public sealed class SeededCpSatCStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.SeededCpSatC;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) => ColdStart(context, seed: 99);
}

/// <summary>
/// Same model, but CP-SAT's PORTFOLIO_SEARCH branching (a mix of several internal heuristics)
/// instead of the default AUTOMATIC_SEARCH - a genuinely different decision strategy, not just a
/// different seed.
/// </summary>
public sealed class AlternateDecisionStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.AlternateDecision;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        ColdStart(context, seed: 1, searchBranching: "PORTFOLIO_SEARCH");
}

/// <summary>Takes the best candidate found so far and gives it a full fresh search pass, warm-started from the hint.</summary>
public sealed class MultiStageStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.MultiStage;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        WarmStart(context, seed: 1);
}

/// <summary>Focused re-solve warm-started from the hint.</summary>
public sealed class HintImprovementStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.HintImprovement;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        WarmStart(context, seed: 13);
}

/// <summary>
/// Large Neighborhood Search: hard-fixes every request to the hint's decision except a random
/// ~25% "neighborhood", which CP-SAT is free to re-optimize from scratch. Always feasible (the
/// frozen requests already satisfied every hard constraint in the hint), and lets the solver spend
/// its whole time budget on a small slice of the problem instead of the whole thing.
/// </summary>
public sealed class NeighborhoodRepairStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepair;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context)
    {
        if (context.Hint is null) return ColdStart(context, seed: 21);
        var demandIds = context.Input.Demands.Select(demand => demand.RequestId).ToArray();
        var freeCount = Math.Max(1, demandIds.Length / 4);
        var random = new Random(21);
        var freeRequestIds = demandIds.OrderBy(_ => random.Next()).Take(freeCount).ToHashSet();
        return WarmStart(context, seed: 21, freeRequestIds: freeRequestIds);
    }
}

/// <summary>Last-stage attempt: hint warm start, no freezing, one more full pass at the whole problem.</summary>
public sealed class FinalPolishingStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.FinalPolishing;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        WarmStart(context, seed: 5);
}
