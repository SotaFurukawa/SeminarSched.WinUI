using SeminarSched.Optimization.Evaluation;

namespace SeminarSched.Optimization.Tests;

public sealed class ScheduleEvaluationTests
{
    private static readonly ScheduleEvaluation Baseline = new(0, 0, 2, 10, 4, 20, 30, 100);

    [Fact]
    public void HardViolation_DominatesEverySoftImprovement()
    {
        var invalidButOtherwisePerfect = new ScheduleEvaluation(1, 0, 0, 0, 0, 0, 0, 0);

        Assert.True(Baseline.IsBetterThan(invalidButOtherwisePerfect));
    }

    [Fact]
    public void UnassignedLesson_DominatesObjectiveValue()
    {
        var lowerObjectiveWithUnassignedLesson = Baseline with
        {
            UnassignedLessons = 1,
            ImportantPreferenceViolations = 0,
            MajorPenalty = 0,
            TeacherGapSlots = 0,
            DistributionPenalty = 0,
            OtherSoftPenalty = 0,
            ObjectiveValue = 0,
        };

        Assert.True(Baseline.IsBetterThan(lowerObjectiveWithUnassignedLesson));
    }

    [Fact]
    public void LaterMetric_BreaksTieOnlyAfterEarlierMetricsMatch()
    {
        var improvedGaps = Baseline with { TeacherGapSlots = 3, ObjectiveValue = 10_000 };

        Assert.True(improvedGaps.IsBetterThan(Baseline));
    }
}
