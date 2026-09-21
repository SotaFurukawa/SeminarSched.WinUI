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
}
