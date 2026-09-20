using SeminarSched.Optimization.Core;
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
}
