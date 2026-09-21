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
/// <remarks>
/// <paramref name="seed"/> picks both the random neighborhood (which ~25% of requests are freed) and
/// the CP-SAT random_seed, so each of the sibling NeighborhoodRepairB/C/D/E strategies below explores
/// a genuinely different neighborhood - repeating the same seed would just re-solve the identical
/// sub-problem. High/Highest list several of these in one NeighborhoodRepair stage (see
/// OptimizationProfileCatalog) specifically so a longer nominal time budget buys more independent
/// random-restart attempts, not just a longer wait on the same one.
/// </remarks>
public abstract class NeighborhoodRepairStrategyBase(int seed) : CpSatStrategyBase
{
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context)
    {
        if (context.Hint is null) return ColdStart(context, seed);
        var demandIds = context.Input.Demands.Select(demand => demand.RequestId).ToArray();
        var freeCount = Math.Max(1, demandIds.Length / 4);
        var random = new Random(seed);
        var freeRequestIds = demandIds.OrderBy(_ => random.Next()).Take(freeCount).ToHashSet();
        return WarmStart(context, seed, freeRequestIds: freeRequestIds);
    }
}

public sealed class NeighborhoodRepairStrategy() : NeighborhoodRepairStrategyBase(seed: 21)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepair;
}

public sealed class NeighborhoodRepairBStrategy() : NeighborhoodRepairStrategyBase(seed: 22)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepairB;
}

public sealed class NeighborhoodRepairCStrategy() : NeighborhoodRepairStrategyBase(seed: 23)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepairC;
}

public sealed class NeighborhoodRepairDStrategy() : NeighborhoodRepairStrategyBase(seed: 24)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepairD;
}

public sealed class NeighborhoodRepairEStrategy() : NeighborhoodRepairStrategyBase(seed: 25)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.NeighborhoodRepairE;
}

/// <summary>
/// Unlike the fixed-seed one-shot strategies above, which each make exactly one attempt and return as
/// soon as CP-SAT proves that one (sub-)problem optimal, a "grinding" strategy keeps making fresh
/// independent attempts - each its own full CP-SAT solve - back to back until its entire allotted
/// <see cref="StrategyContext{TInput,TSolution}.TimeBudget"/> is actually used up, always keeping the
/// best candidate found across every attempt as the hint for the next one. A single one-shot attempt
/// (or even five of them) typically finishes in seconds once CP-SAT proves optimality, so a fixed-size
/// list of strategies still returns well under a quality level's nominal duration; this is the
/// mechanism that actually spends "give it 30 more minutes" on more genuine search instead of ending
/// early with time unused. <see cref="FreezeToRandomNeighborhood"/> picks which of the two grinding
/// variants a subclass is: true = Large Neighborhood Search (freeze ~75% of requests to the hint,
/// only the solver only re-optimizes a fresh random ~25% each attempt); false = a full, unconstrained
/// re-solve of the whole problem each attempt (for a last-stage "final polish" that isn't restricted
/// to any neighborhood).
/// </summary>
public abstract class GrindingStrategyBase(bool freezeToRandomNeighborhood) : IScheduleStrategy<ScheduleProblem, ScheduleSolution>
{
    public abstract OptimizationStrategyKind Kind { get; }

    // 1回の試行に持ち時間を全部使わせず、必ず複数回試せるように短く切る（最短10秒、最大60秒）。
    private static readonly TimeSpan MinimumAttemptBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumAttemptBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumUsefulRemainder = TimeSpan.FromSeconds(3);

    public async Task<ScheduleCandidate<ScheduleSolution>?> ExecuteAsync(
        StrategyContext<ScheduleProblem, ScheduleSolution> context,
        CancellationToken cancellationToken)
    {
        var overallWatch = Stopwatch.StartNew();
        var perAttemptBudget = TimeSpan.FromSeconds(Math.Clamp(context.TimeBudget.TotalSeconds / 6, MinimumAttemptBudget.TotalSeconds, MaximumAttemptBudget.TotalSeconds));
        ScheduleCandidate<ScheduleSolution>? best = context.Hint;
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = context.TimeBudget - overallWatch.Elapsed;
            // 最初の1回は、持ち時間がどれほど短くても必ず試す（さもないと、ステージのbudgetShare配分
            // 次第でこの戦略に3秒未満しか渡らなかった場合に一度も試さずcontext.Hintをそのまま
            // 返すだけになってしまう）。2回目以降は、中途半端な残り時間で新規solveを始めて
            // オーバーヘッドだけ食う（モデル構築・ソルバー初期化）のを避けるため、ある程度の
            // 残り時間が無ければ打ち切る。
            if (attempt > 0 && remaining < MinimumUsefulRemainder) break;
            if (remaining <= TimeSpan.Zero) break;
            var attemptBudget = remaining < perAttemptBudget ? remaining : perAttemptBudget;
            // 単純増加のseedだけだと近傍選択(Randomの内部状態)が予測しやすくなりすぎるため、
            // 試行回数と経過ミリ秒を混ぜてばらけさせる（暗号強度は不要、探索の多様性だけが目的）。
            var seed = unchecked(97 + attempt * 733 + (int)(overallWatch.ElapsedMilliseconds % 9973));
            try
            {
                var watch = Stopwatch.StartNew();
                var options = BuildOptions(context.Input, attemptBudget, seed, best);
                var solution = await new CpSatScheduleSolver().SolveAsync(context.Input, options, cancellationToken).ConfigureAwait(false);
                var evaluation = ScheduleEvaluationCalculator.Evaluate(context.Input, solution);
                var candidate = new ScheduleCandidate<ScheduleSolution>(solution, evaluation, Kind, watch.Elapsed);
                if (best is null || candidate.Evaluation.IsBetterThan(best.Evaluation)) best = candidate;
            }
            catch (InvalidOperationException)
            {
                // このneighborhoodでは解なし（滅多に起きないはずだが、hintそのものが常にfeasibleなので
                // 理論上は起きない - 念のためのガード）。次の試行で再挑戦する。
            }
            attempt++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        // context.Hintそのものを一度も改善できなかった場合はnullを返す（このステージからの新規貢献は
        // 無かった、とScheduleOptimizerに正しく伝える。hintを「新しい候補」として返すと同じ解が
        // 何重にも二重計上されてしまう）。
        return ReferenceEquals(best, context.Hint) ? null : best;
    }

    private CpSatSolveOptions BuildOptions(ScheduleProblem input, TimeSpan budget, int seed, ScheduleCandidate<ScheduleSolution>? hint)
    {
        var workerCount = Math.Max(1, Environment.ProcessorCount);
        if (hint is null) return new CpSatSolveOptions(budget, seed, workerCount);
        if (!freezeToRandomNeighborhood) return new CpSatSolveOptions(budget, seed, workerCount, Hint: hint.Solution);
        var demandIds = input.Demands.Select(demand => demand.RequestId).ToArray();
        var freeCount = Math.Max(1, demandIds.Length / 4);
        var random = new Random(seed);
        var freeRequestIds = demandIds.OrderBy(_ => random.Next()).Take(freeCount).ToHashSet();
        return new CpSatSolveOptions(budget, seed, workerCount, Hint: hint.Solution, FreeRequestIds: freeRequestIds);
    }
}

public sealed class GrindingNeighborhoodRepairStrategy() : GrindingStrategyBase(freezeToRandomNeighborhood: true)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.GrindingNeighborhoodRepair;
}

public sealed class GrindingFinalPolishingStrategy() : GrindingStrategyBase(freezeToRandomNeighborhood: false)
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.GrindingFinalPolishing;
}

/// <summary>Last-stage attempt: hint warm start, no freezing, one more full pass at the whole problem.</summary>
public sealed class FinalPolishingStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.FinalPolishing;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        WarmStart(context, seed: 5);
}

/// <summary>Second final-pass attempt with a different seed, for Highest only (see OptimizationProfileCatalog).</summary>
public sealed class FinalPolishingBStrategy : CpSatStrategyBase
{
    public override OptimizationStrategyKind Kind => OptimizationStrategyKind.FinalPolishingB;
    protected override CpSatSolveOptions BuildOptions(StrategyContext<ScheduleProblem, ScheduleSolution> context) =>
        WarmStart(context, seed: 6);
}
