using System.Diagnostics;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Execution;

public sealed class ScheduleOptimizer<TInput, TSolution>
{
    // ユーザー報告「持ち時間内に完成しなかった場合、このパーセンテージが減少してしまう。これはおかしい
    // ので、初めから低く見えるようにしてください」への対応（checkpoint96）。延長が構造的に起こり得る
    // 実行では、通常ステージの進捗目盛りをこの割合までしか使わない（残りは延長用に予約しておく）。
    // 延長は名目時間と同じ長さ（最大で名目時間の2倍）なので、0.5（半分）が実際の時間配分と一致する
    // 最も誠実な値。延長が発生しない大多数のケースでは、通常ステージ完了時点で最大50%までしか進まず、
    // 実行終了時に一度だけ100%へ前進する（後退は起きないが、前方向への段差は生じる）。
    private const double ExtensionReservedShare = 0.5;

    private readonly IReadOnlyDictionary<OptimizationStrategyKind, IScheduleStrategy<TInput, TSolution>> _strategies;

    public ScheduleOptimizer(IEnumerable<IScheduleStrategy<TInput, TSolution>> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToDictionary(strategy => strategy.Kind);
    }

    // ユーザー要望「一応2倍の時間まで待つのは目安ではあるが、2倍以上の時間を待ってもいい。既定の
    // 時間になっても終了しなかった場合に、そのまま継続する、という項目を追加してほしい」への対応
    // （checkpoint97）。既定true: 延長（下記）を使い切ってもなお未完成なら、3回目・4回目...と
    // 延長を繰り返し、完成するかユーザーが「中断して現在の結果を採用」を押すまで上限なく粘り続ける。
    // falseなら従来通り延長は1回のみ（名目時間の最大2倍で必ず打ち切る）。既存の呼び出し元
    // （テスト等）を壊さないよう既定値はfalse（末尾の追加引数）にしてあり、実際のアプリからは
    // SqliteScheduleRunServiceがSchedulingPolicy.ContinueBeyondNominalTimeIfIncomplete（既定true）を
    // 明示的に渡す。
    public async Task<OptimizationRunResult<TSolution>> RunAsync(
        TInput input,
        OptimizationProfile profile,
        OptimizationRunControl control,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool continueBeyondNominalTimeIfIncomplete = false)
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

        // ユーザー報告「持ち時間内に完成しなかった場合、このパーセンテージが減少してしまう。これは
        // おかしいので、初めから低く見えるようにしてください」への対応。延長（下記）が構造的に
        // 起こり得る場合、通常ステージの進捗は名目上の目盛り全体（0.0〜1.0）ではなく、その半分
        // （0.0〜0.5）だけを使って報告する。延長が実際に発生した場合は残り半分（0.5〜1.0）を
        // そのまま使えるため、checkpoint91のような「目盛りを後から引き直す」再スケーリングが
        // 一切不要になり、パーセンテージが後退することが無くなる（延長が発生しない多数派のケースでは、
        // 通常ステージ完了時点で最大50%までしか進まず、実行終了時（OptimizationRunState側の
        // !IsRunning判定）に一度で100%へ前進する。後退は無いが前方向への段差は生じる）。
        var canExtend = _strategies.ContainsKey(OptimizationStrategyKind.GrindingNeighborhoodRepair);
        var stageWeightScale = canExtend ? ExtensionReservedShare : 1.0;

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

                // ユーザー要望「一時停止ボタンを作ってほしい」（checkpoint99）への対応。次の戦略を
                // 開始する直前のこの区切りでのみ一時停止を確認する（実行中の1戦略の途中では止まらない。
                // OptimizationRunControlのコメント参照）。待機中に「中断して現在の結果を採用」や
                // 名目時間満了・本当のキャンセルが来た場合は、通常の探索と同じ扱いで抜ける。
                try
                {
                    await control.WaitIfPausedAsync(runCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    (control.AcceptBestToken.IsCancellationRequested || maximumTime.IsCancellationRequested))
                {
                    acceptedEarly = control.AcceptBestToken.IsCancellationRequested;
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
                    weightConsumed * stageWeightScale, strategyWeight * stageWeightScale, strategyBudget, IsStrategyStarting: true));

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
                    weightConsumed * stageWeightScale, strategyWeight * stageWeightScale, strategyBudget, IsStrategyStarting: false));

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
        // Best が無い状態でも安全に呼べる）を実行する。これを「延長」と呼ぶ。
        //
        // checkpoint97（ユーザー要望「2倍以上の時間を待ってもいい。既定の時間になっても終了しな
        // かった場合に、そのまま継続する、という項目を追加してほしい」）: continueBeyondNominal
        // TimeIfIncompleteがfalse（既定）なら、従来通り延長は1回のみ・最大で名目時間の2倍で必ず
        // 打ち切る。trueなら、1回の延長を使い切ってもなお未完成な場合に2回目・3回目...と繰り返し、
        // 完成するかユーザーが「中断して現在の結果を採用」を押すまで上限なく粘り続ける
        // （実データでの調査により、大規模な問題では1回の延長（名目時間と同じ長さ）を使い切っても
        // まだfeasible解を安定して得られないことがあると判明したための対応）。
        // checkpoint97の自己レビューで発覚: continueBeyondNominalTimeIfIncomplete=trueのまま無条件に
        // ループさせると、そもそも時間をいくら与えても解決しない構造的な未配置（対応できる講師が
        // 1人もいない、担当講師優先度5の絞り込みでその講師の空きが恒久的に不足している等）のケースで
        // 無限ループしてしまう（実際にテストスイートで1件、この形の既存テストが本当にハングして発覚：
        // テストのホストプロセスごと強制終了する必要があった）。「時間が足りないだけ」なら追加の
        // 延長で改善するはずなので、直近の延長で一切改善が無かった場合はそこで打ち切る（＝
        // continueBeyondNominalTimeIfIncompleteはあくまで「時間切れで終わらせない」ためのものであり、
        // 「構造的に不可能でも無限に粘る」ためのものではない）。
        // checkpoint97の実データ再確認で判明: 「候補が1件も見つかっていない（bestBeforeExtensionが
        // null）」状態は、「候補はあるが改善できない（bestBeforeExtensionがあるのに変化が無い）」
        // 状態と、粘る価値がまるで違う。後者は既に採用できる結果があるため、1回で見切りを付けても
        // 失うものが無い（今の設計通りpatience=1のままでよい）。しかし前者で1回のみの延長で諦めると
        // 「時間割を作成できませんでした」という何も残らない例外になる、最悪の結果になる。実際、
        // ユーザーの実プロジェクトファイルで標準品質を実行したところ、初期探索6戦略＋延長1回
        // （合計20分、grinding試行だけで十数回）すべてが1件もfeasible解を得られず完全に失敗した一方、
        // 直後に全く同じ設定で再実行しただけで初期探索の1戦略目から複数回連続でfeasible解を得られた
        // （458/458の完全解に近い結果）。この規模の問題ではCP-SATの探索結果に強い実行ごとの
        // ばらつきがあり、「1回全く見つからなかった」だけでは「構造的に不可能」と判断する根拠として
        // 弱すぎることを直接確認した。そのため、候補が1件も無い状態でのみ猶予（patience）を設け、
        // これを使い切るまでは諦めずに追加の延長パスを繰り返す。
        const int NeverFoundAnyCandidatePatienceLimit = 5;

        var wasExtended = false;
        var extensionPassIndex = 0;
        var stagnantExtensionPass = false;
        var consecutiveNeverFoundStagnantPasses = 0;
        var bestBeforeExtension = Best(allCandidates);
        while (!acceptedEarly && !cancellationToken.IsCancellationRequested &&
               (bestBeforeExtension is null || bestBeforeExtension.Evaluation.UnassignedLessons > 0) &&
               _strategies.ContainsKey(OptimizationStrategyKind.GrindingNeighborhoodRepair) &&
               (extensionPassIndex == 0 ||
                (continueBeyondNominalTimeIfIncomplete && !stagnantExtensionPass) ||
                (continueBeyondNominalTimeIfIncomplete && bestBeforeExtension is null &&
                 consecutiveNeverFoundStagnantPasses < NeverFoundAnyCandidatePatienceLimit)))
        {
            // ユーザー要望「一時停止ボタンを作ってほしい」（checkpoint99）への対応。次の延長パスを
            // 開始する直前のこの区切りでのみ一時停止を確認する（実行中の1延長パスの途中では止まらない）。
            try
            {
                await control.WaitIfPausedAsync(runCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                (control.AcceptBestToken.IsCancellationRequested || maximumTime.IsCancellationRequested))
            {
                acceptedEarly = control.AcceptBestToken.IsCancellationRequested;
                break;
            }

            wasExtended = true;
            extensionPassIndex++;
            var hadCandidateBeforeThisPass = bestBeforeExtension is not null;
            var extensionBudget = profile.MaximumDuration;

            // ユーザー報告バグ修正（checkpoint91）: 「残り時間が0になり100%になってもなかなか終わらない」。
            // 当初はStrategyWeightを0のまま報告しており、延長中はEstimateRawの補間式が張り付いて
            // 動かなかった。この修正では「これまでの進捗と延長の持ち分を合計1.0になるよう事後的に
            // 比例配分し直す」方式にしたが、延長開始の瞬間に表示が100%付近から後退する副作用があった。
            //
            // checkpoint96で再設計: 上記の「事後的な再スケーリング」をやめ、延長が構造的に起こり得る
            // 場合は最初から通常ステージの目盛りをExtensionReservedShare（0.5）までしか使わないように
            // した（ステージループ内のprogress報告を参照）。延長1回のみ（continueBeyondNominalTime
            // IfIncomplete=false）ならそのままExtensionReservedShare〜1.0を1回で使い切る。
            //
            // checkpoint97でさらに複数回の延長に対応: 1.0へ近づくが決して到達しない等比数列
            // （1回目=0.5〜0.75、2回目=0.75〜0.875、3回目=0.875〜0.9375...）で目盛りを配分する。
            // 何回目の延長で完成するか事前に分からないため、後退させずに済む唯一の方法。「本当に
            // 終わった」ことの表現（真の100%）は、この関数の外側（OptimizationRunState、実行中で
            // なくなった時点で100%とみなす既存ロジック）に任せる。
            var passStartWeight = continueBeyondNominalTimeIfIncomplete
                ? 1.0 - Math.Pow(ExtensionReservedShare, extensionPassIndex)
                : ExtensionReservedShare;
            var passEndWeight = continueBeyondNominalTimeIfIncomplete
                ? 1.0 - Math.Pow(ExtensionReservedShare, extensionPassIndex + 1)
                : 1.0;
            var extensionShare = passEndWeight - passStartWeight;
            progress?.Report(new OptimizationProgress(
                stopwatch.Elapsed, profile.MaximumDuration, OptimizationStageKind.Extension, OptimizationStrategyKind.GrindingNeighborhoodRepair,
                completed, total, improvementCount, bestBeforeExtension?.Evaluation,
                passStartWeight, extensionShare, extensionBudget, IsStrategyStarting: true, IsExtending: true));

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

            // GrindingNeighborhoodRepairStrategy.ExecuteAsync（GrindingStrategyBase）は、このパスで
            // ヒントより良い候補を1つも見つけられなかった場合に必ずnullを返す契約（ヒントが元々null
            // ＝何も無い状態から一度も何も得られなかった場合も、ヒントはあったが一度も改善できなかった
            // 場合も、どちらもnull）。そのため「今回のパスは何も得られなかった＝これ以上粘っても
            // 無意味」の判定は、この戻り値がnullかどうかだけで一貫して判定できる。
            if (extended is not null)
            {
                allCandidates.Add(extended);
                if (bestBeforeExtension is null || extended.Evaluation.IsBetterThan(bestBeforeExtension.Evaluation))
                {
                    improvementCount++;
                }
            }

            bestBeforeExtension = Best(allCandidates);
            stagnantExtensionPass = extended is null;
            consecutiveNeverFoundStagnantPasses = !hadCandidateBeforeThisPass && stagnantExtensionPass
                ? consecutiveNeverFoundStagnantPasses + 1
                : 0;
            progress?.Report(new OptimizationProgress(
                stopwatch.Elapsed, profile.MaximumDuration, OptimizationStageKind.Extension, OptimizationStrategyKind.GrindingNeighborhoodRepair,
                completed, total, improvementCount, bestBeforeExtension?.Evaluation,
                passEndWeight, extensionShare, extensionBudget, IsStrategyStarting: false, IsExtending: true));
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
