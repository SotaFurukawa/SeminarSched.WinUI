using SeminarSched.Optimization.Core;
using SeminarSched.Optimization.Evaluation;

namespace SeminarSched.Optimization.Tests;

public sealed class ScheduleEvaluationCalculatorTests
{
    // ユーザー要望（間隔の均等化・科目の交互配置）は、単一のCP-SAT解の中ではwindowベースの近似で
    // 目的関数へ反映しているが（CpSatScheduleSolverTests参照）、grinding/LNS戦略が複数の解を比較して
    // 「より良い解」を選ぶ際に使うScheduleEvaluationにもこの品質差が反映されていないと、CP-SAT内部の
    // 目的関数改善が戦略間の解選択で無視されてしまう。ここでは既に確定した2つの具体的な解（同じ
    // 問題・同じ配置数・同じ講師選好スコア）を直接比較し、間隔が均等・科目が交互な方がSpacingPenalty
    // で優れていることを検証する。
    [Fact]
    public void Evaluate_ScoresEvenlySpacedInterleavedSolutionBetterThanClusteredOneOnSpacingPenalty()
    {
        var demands = new[] { new LessonDemand(1, 10, 4, 0), new LessonDemand(2, 10, 4, 0) };
        var candidates = Enumerable.Range(1, 16)
            .SelectMany(day => new[]
            {
                new PlacementCandidate(1, 10, 100, day, 1, day, 1),
                new PlacementCandidate(2, 10, 200, day, 2, day, 2),
            })
            .ToArray();
        var problem = new ScheduleProblem(demands, candidates);

        var interleaved = new ScheduleSolution(
            new long[] { 1, 3, 5, 7, 9, 11, 13, 15 }
                .Select((day, i) => i % 2 == 0
                    ? new SchedulePlacement(1, 10, 100, day, 1)
                    : new SchedulePlacement(2, 10, 200, day, 2))
                .ToArray(),
            0, 0, TimeSpan.Zero);

        var clustered = new ScheduleSolution(
            new long[] { 1, 2, 3, 4, 13, 14, 15, 16 }
                .Select((day, i) => i < 4
                    ? new SchedulePlacement(1, 10, 100, day, 1)
                    : new SchedulePlacement(2, 10, 200, day, 2))
                .ToArray(),
            0, 0, TimeSpan.Zero);

        var interleavedEvaluation = ScheduleEvaluationCalculator.Evaluate(problem, interleaved);
        var clusteredEvaluation = ScheduleEvaluationCalculator.Evaluate(problem, clustered);

        Assert.True(
            interleavedEvaluation.SpacingPenalty < clusteredEvaluation.SpacingPenalty,
            $"expected interleaved ({interleavedEvaluation.SpacingPenalty}) < clustered ({clusteredEvaluation.SpacingPenalty})");
        Assert.True(interleavedEvaluation.IsBetterThan(clusteredEvaluation));
    }

    [Fact]
    public void Evaluate_SingleSessionRequest_DoesNotThrowAndHasZeroSpacingPenalty()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0)],
            [new PlacementCandidate(1, 10, 100, 1, 1, 1, 1)]);
        var solution = new ScheduleSolution([new SchedulePlacement(1, 10, 100, 1, 1)], 0, 0, TimeSpan.Zero);

        var evaluation = ScheduleEvaluationCalculator.Evaluate(problem, solution);

        Assert.Equal(0, evaluation.SpacingPenalty);
    }
}
