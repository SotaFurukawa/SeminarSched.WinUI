using System.Diagnostics;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

public sealed class ScheduleOptimizer<TInput, TSolution>
{
    private readonly IReadOnlyDictionary<OptimizationStrategyKind, IScheduleStrategy<TInput, TSolution>> _strategies;

    public ScheduleOptimizer(IEnumerable<IScheduleStrategy<TInput, TSolution>> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToDictionary(strategy => strategy.Kind);
    }

    public async Task<OptimizationRunResult<TSolution>> RunAsync(
        TInput input,
        OptimizationProfile profile,
        OptimizationRunControl control,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(control);
        ValidateStrategies(profile);

        var stopwatch = Stopwatch.StartNew();
        var allCandidates = new List<ScheduleCandidate<TSolution>>();
        var advancing = new List<ScheduleCandidate<TSolution>>();
        var improvementCount = 0;
        var lastImprovement = TimeSpan.Zero;
        var completed = 0;
        var total = profile.Stages.Sum(stage => stage.Strategies.Count);
        var acceptedEarly = false;
        var stoppedForStagnation = false;

        using var maximumTime = new CancellationTokenSource(profile.MaximumDuration);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, maximumTime.Token, control.AcceptBestToken);

        var weightConsumed = 0.0;

        foreach (var stage in profile.Stages)
        {
            var stageBudget = TimeSpan.FromTicks((long)(profile.MaximumDuration.Ticks * stage.BudgetShare));
            var strategyBudget = TimeSpan.FromTicks(stageBudget.Ticks / stage.Strategies.Count);
            var strategyWeight = stage.BudgetShare / stage.Strategies.Count;
            var stageCandidates = new List<ScheduleCandidate<TSolution>>();

            foreach (var kind in stage.Strategies)
            {
                if (control.AcceptBestToken.IsCancellationRequested)
                {
                    acceptedEarly = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (maximumTime.IsCancellationRequested)
                {
                    break;
                }

                // stageCandidates（このステージ内で既に得られた候補）も見ることで、同じステージ内で
                // 後から実行される戦略が、直前の戦略の改善結果をhintとして引き継げるようにする
                // （advancingはステージ完了時にしか更新されないため、これが無いとステージ内の
                // 複数戦略が全員ステージ開始時点の古いhintのまま warm-start してしまっていた）。
                var previousBest = Best(stageCandidates.Concat(advancing.Count > 0 ? advancing : allCandidates));
                progress?.Report(new OptimizationProgress(
                    stopwatch.Elapsed, profile.MaximumDuration, stage.Kind, kind, completed, total,
                    improvementCount, previousBest?.Evaluation,
                    weightConsumed, strategyWeight, strategyBudget, IsStrategyStarting: true));

                using var strategyTimeout = new CancellationTokenSource(strategyBudget);
                using var strategyCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    runCancellation.Token, strategyTimeout.Token);
                ScheduleCandidate<TSolution>? candidate = null;
                try
                {
                    candidate = await _strategies[kind].ExecuteAsync(
                        new StrategyContext<TInput, TSolution>(input, strategyBudget, previousBest, progress),
                        strategyCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    strategyTimeout.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested &&
                    !control.AcceptBestToken.IsCancellationRequested)
                {
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    (control.AcceptBestToken.IsCancellationRequested || maximumTime.IsCancellationRequested))
                {
                    acceptedEarly = control.AcceptBestToken.IsCancellationRequested;
                }

                completed++;
                weightConsumed += strategyWeight;
                if (candidate is not null)
                {
                    allCandidates.Add(candidate);
                    stageCandidates.Add(candidate);
                    if (previousBest is null || candidate.Evaluation.IsBetterThan(previousBest.Evaluation))
                    {
                        improvementCount++;
                        lastImprovement = stopwatch.Elapsed;
                    }
                }

                var currentBest = Best(allCandidates);
                progress?.Report(new OptimizationProgress(
                    stopwatch.Elapsed, profile.MaximumDuration, stage.Kind, kind, completed, total,
                    improvementCount, currentBest?.Evaluation,
                    weightConsumed, strategyWeight, strategyBudget, IsStrategyStarting: false));

                if (currentBest is not null && stopwatch.Elapsed - lastImprovement >= profile.StagnationTimeout)
                {
                    stoppedForStagnation = true;
                    break;
                }
            }

            advancing = advancing
                .Concat(stageCandidates)
                .OrderBy(candidate => candidate.Evaluation)
                .Take(stage.CandidatesToAdvance)
                .ToList();

            if (acceptedEarly || stoppedForStagnation || maximumTime.IsCancellationRequested)
            {
                break;
            }
        }

        return new OptimizationRunResult<TSolution>(
            Best(allCandidates),
            allCandidates.OrderBy(candidate => candidate.Evaluation).ToArray(),
            improvementCount,
            acceptedEarly,
            stoppedForStagnation,
            stopwatch.Elapsed);
    }

    private void ValidateStrategies(OptimizationProfile profile)
    {
        var missing = profile.Stages.SelectMany(stage => stage.Strategies).Distinct()
            .Where(kind => !_strategies.ContainsKey(kind)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Strategies are not registered: {string.Join(", ", missing)}");
        }
    }

    private static ScheduleCandidate<TSolution>? Best(IEnumerable<ScheduleCandidate<TSolution>> candidates) =>
        candidates.OrderBy(candidate => candidate.Evaluation).FirstOrDefault();
}
