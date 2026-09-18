using SeminarSched.Optimization.Core;

namespace SeminarSched.Optimization.Evaluation;

/// <summary>
/// Turns a solved <see cref="ScheduleSolution"/> into the lexicographic <see cref="ScheduleEvaluation"/>
/// tuple that <see cref="Execution.ScheduleOptimizer{TInput,TSolution}"/> uses to rank candidates from
/// different strategies against each other. Every CP-SAT-sourced solution already satisfies every hard
/// constraint structurally (the model cannot produce an invalid one), so HardConstraintViolations is
/// always 0 here; it exists in the tuple for future non-CP-SAT strategies that might not guarantee that.
/// </summary>
public static class ScheduleEvaluationCalculator
{
    public static ScheduleEvaluation Evaluate(ScheduleProblem problem, ScheduleSolution solution)
    {
        var placementsByRequest = solution.Placements.ToLookup(placement => placement.RequestId);

        var importantPreferenceViolations = 0;
        foreach (var demand in problem.Demands.Where(demand => demand.RegularTeacherId is not null && demand.RegularTeacherPriority >= 2))
        {
            var minimum = CpSatScheduleSolver.MinimumRegularTeacherSessions(demand.RequiredSessions, demand.RegularTeacherPriority);
            var remaining = Math.Max(0, minimum - demand.FixedRegularTeacherSessions);
            if (remaining == 0) continue;
            var actual = placementsByRequest[demand.RequestId].Count(placement => placement.TeacherId == demand.RegularTeacherId);
            if (actual < remaining) importantPreferenceViolations += remaining - actual;
        }

        var candidatesByKey = problem.Candidates.ToDictionary(candidate =>
            (candidate.RequestId, candidate.StudentId, candidate.TeacherId, candidate.OpenDateId, candidate.TimeSlotId));
        long majorPenalty = 0;
        long otherSoftPenalty = 0;
        foreach (var placement in solution.Placements)
        {
            if (!candidatesByKey.TryGetValue((placement.RequestId, placement.StudentId, placement.TeacherId, placement.OpenDateId, placement.TimeSlotId), out var candidate))
                continue;
            majorPenalty += candidate.PreferencePenalty;
            otherSoftPenalty -= candidate.AvailabilityPreference;
        }

        var studentDayGroups = solution.Placements
            .Select(placement => (placement.StudentId, placement.OpenDateId))
            .Concat(problem.ExistingPlacements.Select(placement => (placement.StudentId, placement.OpenDateId)))
            .GroupBy(pair => pair)
            .ToArray();
        var totalSessions = studentDayGroups.Sum(group => group.Count());
        var distinctStudentDays = studentDayGroups.Length;
        var distributionPenalty = Math.Max(0, totalSessions - distinctStudentDays);

        return new ScheduleEvaluation(
            HardConstraintViolations: 0,
            UnassignedLessons: solution.UnassignedLessons,
            ImportantPreferenceViolations: importantPreferenceViolations,
            MajorPenalty: majorPenalty,
            TeacherGapSlots: 0,
            DistributionPenalty: distributionPenalty,
            OtherSoftPenalty: otherSoftPenalty,
            ObjectiveValue: solution.ObjectiveValue);
    }
}
