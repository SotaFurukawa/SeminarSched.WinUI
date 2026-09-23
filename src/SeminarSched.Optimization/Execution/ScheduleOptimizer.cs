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

        // ユーザー指示「指定時間内に足りない場合がある。そういった場合は途中で中断するのではなく、
        // 少し時間を要していますといった警告を出して、続行してください」への対応。名目時間（全ステージ）
        // を使い切っても結果が未完成（未配置が残っている、または1件も解が得られていない）な場合、
        // ユーザーが「中断して現在の結果を採用」を押していない限り、名目時間と同じ長さだけ追加で
        // grinding戦略（GrindingNeighborhoodRepairStrategyはヒントが無くても必ず1回は試行するため、
        // Best が無い状態でも安全に呼べる）を実行する。これを「延長」と呼び、無限に粘り続けないよう
        // 最大でも名目時間の2倍で必ず打ち切る（1回のみ延長し、延長後もなお未完成なら諦めてそのまま返す）。
        var wasExtended = false;
        var bestBeforeExtension = Best(allCandidates);
        if (!acceptedEarly && !cancellationToken.IsCancellationRequested &&
            (bestBeforeExtension is null || bestBeforeExtension.Evaluation.UnassignedLessons > 0) &&
            _strategies.ContainsKey(OptimizationStrategyKind.GrindingNeighborhoodRepair))
        {
            wasExtended = true;
            var extensionBudget = profile.MaximumDuration;

            // ユーザー報告バグ修正: 「残り時間が0になり100%になってもなかなか終わらない」。延長開始時点で
            // weightConsumedは既に（ほぼ）1.0＝100%まで積み上がっているため、StrategyWeightを0のまま
            // 報告すると、延長中はEstimateRawの補間式 (ProgressWeight + fraction*StrategyWeight) が
            // ProgressWeightのまま張り付いて動かず、ずっと100%表示のまま実際には粘り続けてしまっていた
            // （進捗ではなく事実上「名目時間との比較」に戻ってしまっていた）。延長を「計画全体がもう1単位
            // 増えた」とみなして目盛りを引き直す: これまでの進捗(weightConsumed)と延長の持ち分を
            // 合計1.0になるよう比例配分し直し、延長中の実経過時間に応じて滑らかにその範囲内で
            // 100%まで進むようにする。延長が実際に始まった場合、表示は一度100%付近から後退することに
            // なるが、これは「少し時間を要しています」の警告と一緒に見せることで、進捗が正しく
            // 巻き戻ったことを示す（ずっと100%のまま固まって見えるより正確で誠実）。
            var extensionShare = 1.0;
            var rescaledBaseWeight = weightConsumed / (weightConsumed + extensionShare);
            var rescaledExtensionShare = extensionShare / (weightConsumed + extensionShare);
            progress?.Report(new OptimizationProgress(
                stopwatch.Elapsed, profile.MaximumDuration, OptimizationStageKind.Extension, OptimizationStrategyKind.GrindingNeighborhoodRepair,
                completed, total, improvementCount, bestBeforeExtension?.Evaluation,
                rescaledBaseWeight, rescaledExtensionShare, extensionBudget, IsStrategyStarting: true, IsExtending: true));

            using var extensionTimeout = new CancellationTokenSource(extensionBudget);
            using var extensionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, extensionTimeout.Token, control.AcceptBestToken);
            ScheduleCandidate<TSolution>? extended = null;
            try
            {
                extended = await _strategies[OptimizationStrategyKind.GrindingNeighborhoodRepair].ExecuteAsync(
                    new StrategyContext<TInput, TSolution>(input, extensionBudget, bestBeforeExtension, progress),
                    extensionCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                (control.AcceptBestToken.IsCancellationRequested || extensionTimeout.IsCancellationRequested))
            {
                acceptedEarly = control.AcceptBestToken.IsCancellationRequested;
            }

            if (extended is not null)
            {
                allCandidates.Add(extended);
                if (bestBeforeExtension is null || extended.Evaluation.IsBetterThan(bestBeforeExtension.Evaluation))
                {
                    improvementCount++;
                }
            }

            var bestAfterExtension = Best(allCandidates);
            progress?.Report(new OptimizationProgress(
                stopwatch.Elapsed, profile.MaximumDuration, OptimizationStageKind.Extension, OptimizationStrategyKind.GrindingNeighborhoodRepair,
                completed, total, improvementCount, bestAfterExtension?.Evaluation,
                rescaledBaseWeight + rescaledExtensionShare, rescaledExtensionShare, extensionBudget, IsStrategyStarting: false, IsExtending: true));
        }

        return new OptimizationRunResult<TSolution>(
            Best(allCandidates),
            allCandidates.OrderBy(candidate => candidate.Evaluation).ToArray(),
            improvementCount,
            acceptedEarly,
            stoppedForStagnation,
            stopwatch.Elapsed,
            wasExtended);
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
