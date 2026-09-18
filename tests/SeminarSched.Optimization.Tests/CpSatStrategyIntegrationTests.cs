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
}
