using SeminarSched.Optimization.Core;
using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Tests;

public sealed class CpSatStrategyIntegrationTests
{
    [Fact]
    public async Task RunAsync_WithAllNineRealStrategies_ProducesValidCompleteSchedule()
    {
        var demands = Enumerable.Range(1, 5)
            .Select(i => new LessonDemand(i, 10 + i, RequiredSessions: 2, AlreadyFixedSessions: 0))
            .ToArray();
        var candidates = demands
            .SelectMany(demand => Enumerable.Range(1, 4)
                .SelectMany(day => new[] { 100L, 101L }
                    .Select(teacherId => new PlacementCandidate(demand.RequestId, demand.StudentId, teacherId, day, 1, day, 1))))
            .ToArray();
        var problem = new ScheduleProblem(demands, candidates);

        var strategies = new IScheduleStrategy<ScheduleProblem, ScheduleSolution>[]
        {
            new StandardCpSatStrategy(), new SeededCpSatAStrategy(), new SeededCpSatBStrategy(), new SeededCpSatCStrategy(),
            new AlternateDecisionStrategy(), new MultiStageStrategy(), new HintImprovementStrategy(),
            new NeighborhoodRepairStrategy(), new FinalPolishingStrategy(),
        };
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(12),
            [
                new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 0.5, 2,
                    [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA, OptimizationStrategyKind.SeededCpSatB,
                     OptimizationStrategyKind.SeededCpSatC, OptimizationStrategyKind.AlternateDecision]),
                new OptimizationStageDefinition(OptimizationStageKind.CandidateAdvancement, 0.3, 1,
                    [OptimizationStrategyKind.MultiStage, OptimizationStrategyKind.HintImprovement]),
                new OptimizationStageDefinition(OptimizationStageKind.NeighborhoodRepair, 0.1, 1, [OptimizationStrategyKind.NeighborhoodRepair]),
                new OptimizationStageDefinition(OptimizationStageKind.FinalPolishing, 0.1, 1, [OptimizationStrategyKind.FinalPolishing]),
            ]);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(strategies);
        using var control = new OptimizationRunControl();
        var result = await optimizer.RunAsync(problem, profile, control);

        Assert.NotNull(result.Best);
        Assert.Equal(0, result.Best!.Evaluation.UnassignedLessons);
        Assert.Equal(0, result.Best.Evaluation.HardConstraintViolations);
        ScheduleSolutionValidator.Validate(problem, result.Best.Solution);
    }

    // 高品質・最高品質向けに追加した近傍再探索B〜E・最終仕上げ探索Bが、実際にCP-SATへ配線されて
    // 問題なく動くことを確認する（品質カタログでは1つのNeighborhoodRepairステージに複数並べて使う
    // 想定のため、ここでも同じステージへまとめて並べる）。
    [Fact]
    public async Task RunAsync_WithAdditionalNeighborhoodRepairAndFinalPolishingVariants_ProducesValidCompleteSchedule()
    {
        var demands = Enumerable.Range(1, 5)
            .Select(i => new LessonDemand(i, 10 + i, RequiredSessions: 2, AlreadyFixedSessions: 0))
            .ToArray();
        var candidates = demands
            .SelectMany(demand => Enumerable.Range(1, 4)
                .SelectMany(day => new[] { 100L, 101L }
                    .Select(teacherId => new PlacementCandidate(demand.RequestId, demand.StudentId, teacherId, day, 1, day, 1))))
            .ToArray();
        var problem = new ScheduleProblem(demands, candidates);

        var strategies = new IScheduleStrategy<ScheduleProblem, ScheduleSolution>[]
        {
            new StandardCpSatStrategy(),
            new NeighborhoodRepairStrategy(), new NeighborhoodRepairBStrategy(), new NeighborhoodRepairCStrategy(),
            new NeighborhoodRepairDStrategy(), new NeighborhoodRepairEStrategy(),
            new FinalPolishingStrategy(), new FinalPolishingBStrategy(),
        };
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Highest, "test", "test", "test",
            TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(14),
            [
                new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 0.3, 1, [OptimizationStrategyKind.StandardCpSat]),
                new OptimizationStageDefinition(OptimizationStageKind.NeighborhoodRepair, 0.5, 1,
                    [OptimizationStrategyKind.NeighborhoodRepair, OptimizationStrategyKind.NeighborhoodRepairB, OptimizationStrategyKind.NeighborhoodRepairC,
                     OptimizationStrategyKind.NeighborhoodRepairD, OptimizationStrategyKind.NeighborhoodRepairE]),
                new OptimizationStageDefinition(OptimizationStageKind.FinalPolishing, 0.2, 1,
                    [OptimizationStrategyKind.FinalPolishing, OptimizationStrategyKind.FinalPolishingB]),
            ]);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(strategies);
        using var control = new OptimizationRunControl();
        var result = await optimizer.RunAsync(problem, profile, control);

        Assert.NotNull(result.Best);
        Assert.Equal(0, result.Best!.Evaluation.UnassignedLessons);
        Assert.Equal(0, result.Best.Evaluation.HardConstraintViolations);
        ScheduleSolutionValidator.Validate(problem, result.Best.Solution);
    }

    // ユーザー報告: 最高品質(名目60分)でも数分で終わることが多い。固定数の部分修復戦略を並べる
    // だけでは、各試行がCP-SATの証明済み最適解到達により数秒〜数十秒で終わるため、名目時間を
    // 使い切れない。GrindingNeighborhoodRepairStrategyは、割り当てられた持ち時間を使い切るまで
    // 独立した試行を繰り返すことで、この問題に対処する。ここでは、たっぷりした時間を与えた場合に
    // 実際に「即座に1回返す」のではなく、時間を使って試行を重ねることを検証する
    // （直接の試行回数は内部状態のため、実測経過時間が数百ミリ秒程度の1回の解決よりずっと長い
    // ことで間接的に確認する）。
    [Fact]
    public async Task GrindingNeighborhoodRepairStrategy_SpendsCloseToItsFullTimeBudgetInsteadOfReturningAfterOneAttempt()
    {
        var demand = new LessonDemand(1, 10, 1, 0);
        var candidates = new[] { new PlacementCandidate(1, 10, 100, 1, 1) };
        var problem = new ScheduleProblem([demand], candidates);
        // hintのObjectiveValueは実際に一度解いて得たもの（手組みの適当な値だと、正しく計算された
        // 試行結果のほうが「良い」と誤判定されてしまう）。
        var hintSolution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(2));
        var hint = new ScheduleCandidate<ScheduleSolution>(hintSolution, ScheduleEvaluationCalculator.Evaluate(problem, hintSolution), OptimizationStrategyKind.StandardCpSat, TimeSpan.Zero);

        var strategy = new GrindingNeighborhoodRepairStrategy();
        var budget = TimeSpan.FromSeconds(8);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await strategy.ExecuteAsync(new StrategyContext<ScheduleProblem, ScheduleSolution>(problem, budget, hint, Progress: null), CancellationToken.None);
        watch.Stop();

        // この1受講希望・1候補だけの問題ではhintが既に最適なので、どの試行も改善できず戦略はnullを
        // 返す（=「新しい候補は無かった」）が、それでも持ち時間の大半を試行に費やしているはずで、
        // 一瞬で（数百ミリ秒未満で）返ってはいけない。
        Assert.Null(result);
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(4), $"expected grinding to keep attempting for a meaningful fraction of its 8s budget, only took {watch.Elapsed}");
        Assert.True(watch.Elapsed <= TimeSpan.FromSeconds(9), $"expected grinding to respect its time budget, took {watch.Elapsed}");
    }

    [Fact]
    public async Task GrindingNeighborhoodRepairStrategy_MakesAtLeastOneAttemptEvenWithATinyBudget()
    {
        var demand = new LessonDemand(1, 10, 1, 0);
        var candidates = new[] { new PlacementCandidate(1, 10, 100, 1, 1) };
        var problem = new ScheduleProblem([demand], candidates);

        var strategy = new GrindingNeighborhoodRepairStrategy();
        // No hint: even a budget under the strategy's internal "minimum useful remainder" threshold
        // must still make its first attempt (a cold start), or it would silently never contribute.
        var result = await strategy.ExecuteAsync(new StrategyContext<ScheduleProblem, ScheduleSolution>(problem, TimeSpan.FromSeconds(2), Hint: null, Progress: null), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Solution.Placements);
        ScheduleSolutionValidator.Validate(problem, result.Solution);
    }

    // ユーザー指示「指定時間内に足りない場合がある。そういった場合は途中で中断するのではなく、少し
    // 時間を要していますといった警告を出して、続行してください」を検証する。1受講希望が2回分必要
    // だが候補は1コマ分しか無い、構造的に必ず1件未配置が残る問題（延長しても解決はしないが、
    // 延長フェーズ自体が実際に走り、IsExtending付きの進捗が開始・終了の両方で報告されることを
    // 検証する。延長しても直らないケースでも、Bestはそのまま返る＝無限に粘り続けて固まったりしない）。
    [Fact]
    public async Task RunAsync_ExtendsPastNominalDurationWhenResultIsIncompleteAndReportsIsExtending()
    {
        var demand = new LessonDemand(1, 10, RequiredSessions: 2, AlreadyFixedSessions: 0);
        var candidates = new[] { new PlacementCandidate(1, 10, 100, 1, 1) };
        var problem = new ScheduleProblem([demand], candidates);

        var strategies = new IScheduleStrategy<ScheduleProblem, ScheduleSolution>[]
        {
            new StandardCpSatStrategy(), new GrindingNeighborhoodRepairStrategy(),
        };
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1, [OptimizationStrategyKind.StandardCpSat])]);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(strategies);
        using var control = new OptimizationRunControl();
        var extendingReports = new List<OptimizationProgress>();
        var progress = new Progress<OptimizationProgress>(p => { if (p.IsExtending) extendingReports.Add(p); });

        var result = await optimizer.RunAsync(problem, profile, control, progress);

        Assert.True(result.WasExtended);
        var startReport = Assert.Single(extendingReports, p => p.IsStrategyStarting);
        var endReport = Assert.Single(extendingReports, p => !p.IsStrategyStarting);
        // ユーザー報告バグ修正:「残り時間が0になり100%になってもなかなか終わらない」。延長開始時点で
        // 既にProgressWeightが100%相当のままStrategyWeightが0だと、延長中ずっと100%表示に張り付いて
        // 動かなくなる（元の不具合）。延長は目盛りを引き直すため、開始時点のProgressWeightは100%未満
        // （まだ延長分の作業が残っている）で、かつStrategyWeightは0より大きい（延長の経過に応じて
        // 実際に100%まで動く余地がある）はずで、延長完了時点ではちょうど100%（ProgressWeight+
        // StrategyWeight=1.0）に達しているはず。
        Assert.True(startReport.ProgressWeight < 0.999, $"expected extension start to leave room to progress, got ProgressWeight={startReport.ProgressWeight}");
        Assert.True(startReport.StrategyWeight > 0, $"expected a non-zero StrategyWeight so the percentage can actually move during the extension, got {startReport.StrategyWeight}");
        Assert.Equal(1.0, endReport.ProgressWeight, precision: 6);
        Assert.NotNull(result.Best);
        Assert.Equal(1, result.Best!.Evaluation.UnassignedLessons);
    }

    // ユーザー要望「2倍以上の時間を待ってもいい。既定の時間になっても終了しなかった場合に、そのまま
    // 継続する、という項目を追加してほしい」（checkpoint97）の安全装置を検証する。上のテストと全く
    // 同じ構造的に絶対解決しない問題（1受講希望が2回分必要だが候補は1コマ分しか無い）で
    // continueBeyondNominalTimeIfIncomplete:trueを指定した場合、これが無ければ延長を無限に繰り返し
    // 続けてしまう（実際に実装時、この形の別テストがテストスイートを本当にハングさせて発覚した）。
    // 改善が得られない延長パスが続いたら打ち切る仕組み（stagnantExtensionPass）により、有限回数の
    // 延長パスで終わることを検証する。
    [Fact]
    public async Task RunAsync_StopsRepeatingExtensionWhenStructurallyNeverCompletableEvenWithContinueBeyondNominalTime()
    {
        var demand = new LessonDemand(1, 10, RequiredSessions: 2, AlreadyFixedSessions: 0);
        var candidates = new[] { new PlacementCandidate(1, 10, 100, 1, 1) };
        var problem = new ScheduleProblem([demand], candidates);

        var strategies = new IScheduleStrategy<ScheduleProblem, ScheduleSolution>[]
        {
            new StandardCpSatStrategy(), new GrindingNeighborhoodRepairStrategy(),
        };
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1, [OptimizationStrategyKind.StandardCpSat])]);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(strategies);
        using var control = new OptimizationRunControl();
        var extensionStartReports = new List<OptimizationProgress>();
        var progress = new Progress<OptimizationProgress>(p => { if (p.IsExtending && p.IsStrategyStarting) extensionStartReports.Add(p); });

        // このテスト自体がタイムアウトせず完了すること自体が、無限ループしていないことの直接的な証拠。
        var result = await optimizer.RunAsync(problem, profile, control, progress, continueBeyondNominalTimeIfIncomplete: true)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.WasExtended);
        Assert.NotNull(result.Best);
        Assert.Equal(1, result.Best!.Evaluation.UnassignedLessons);
        // 1回目は必ず試す（改善の余地があるかもしれない）が、1回も改善しなければすぐ打ち切るはずなので、
        // 延長パス数はごく少数（目安2回程度）に収まるはず。
        Assert.True(extensionStartReports.Count <= 3, $"expected the stagnation circuit breaker to stop extension quickly, got {extensionStartReports.Count} passes");
    }

    // 延長しなくても最初のステージだけで完成した（未配置0件の）場合は、延長フェーズ自体が
    // 一切走らないことを確認する（延長は「まだ足りない場合」だけの機能であるべき）。
    [Fact]
    public async Task RunAsync_DoesNotExtendWhenTheInitialStagesAlreadyProduceACompleteSchedule()
    {
        var demand = new LessonDemand(1, 10, RequiredSessions: 1, AlreadyFixedSessions: 0);
        var candidates = new[] { new PlacementCandidate(1, 10, 100, 1, 1) };
        var problem = new ScheduleProblem([demand], candidates);

        var strategies = new IScheduleStrategy<ScheduleProblem, ScheduleSolution>[]
        {
            new StandardCpSatStrategy(), new GrindingNeighborhoodRepairStrategy(),
        };
        // CIの実行機が混雑していると、この程度の自明なモデルでもCP-SATの起動・presolveが短い持ち時間
        // に間に合わないことがある（2026-09-24、CIで実際に発生: RunAsync_PreservesUnlockedManualPlacement
        // で以前に確認したのと同種の、コードの不具合ではなく一時的なタイミングの問題）ため、余裕を持たせる。
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1, [OptimizationStrategyKind.StandardCpSat])]);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(strategies);
        using var control = new OptimizationRunControl();

        var result = await optimizer.RunAsync(problem, profile, control);

        Assert.False(result.WasExtended);
        Assert.Equal(0, result.Best!.Evaluation.UnassignedLessons);
    }
}
