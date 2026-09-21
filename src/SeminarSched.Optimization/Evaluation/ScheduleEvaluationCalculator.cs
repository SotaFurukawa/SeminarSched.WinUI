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

        var spacingPenalty = CalculateSpacingPenalty(problem, solution);

        return new ScheduleEvaluation(
            HardConstraintViolations: 0,
            UnassignedLessons: solution.UnassignedLessons,
            ImportantPreferenceViolations: importantPreferenceViolations,
            MajorPenalty: majorPenalty,
            TeacherGapSlots: 0,
            DistributionPenalty: distributionPenalty,
            SpacingPenalty: spacingPenalty,
            OtherSoftPenalty: otherSoftPenalty,
            ObjectiveValue: solution.ObjectiveValue);
    }

    /// <summary>
    /// Unlike CpSatScheduleSolver's windowed proxy (needed there because the model does not yet know
    /// which days end up selected), a completed solution's placements are concrete data, so this scores
    /// the user's two requests exactly: (1) how far each gap between a student's actual used lesson
    /// days deviates from that student's ideal gap (openDays / totalSessions), and (2) how many times a
    /// student's two chronologically adjacent lessons are for the same subject (same LessonRequest -
    /// in this domain a request is already a unique (student, subject) pair, so "same request" and
    /// "same subject" are equivalent here). Used only to rank already-solved candidates against each
    /// other (e.g. across grinding/LNS attempts); it never drives the CP-SAT search itself.
    /// </summary>
    private static long CalculateSpacingPenalty(ScheduleProblem problem, ScheduleSolution solution)
    {
        var dayOrder = CpSatScheduleSolver.BuildOpenDayOrder(problem);
        if (dayOrder.Count < 2) return 0;
        var rankByDay = dayOrder.Select((id, rank) => (id, rank)).ToDictionary(item => item.id, item => item.rank);

        var allPlacements = solution.Placements.Select(p => (p.StudentId, p.OpenDateId, p.RequestId))
            .Concat(problem.ExistingPlacements.Select(p => (p.StudentId, p.OpenDateId, p.RequestId)))
            .ToArray();

        var totalSessionsByStudent = problem.Demands
            .GroupBy(demand => demand.StudentId)
            .ToDictionary(group => group.Key, group => group.Sum(demand => demand.RequiredSessions));

        long gapDeviationPenalty = 0;
        long subjectAdjacencyPenalty = 0;
        foreach (var group in allPlacements.GroupBy(item => item.StudentId))
        {
            var ordered = group.OrderBy(item => rankByDay.GetValueOrDefault(item.OpenDateId)).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].RequestId == ordered[i - 1].RequestId) subjectAdjacencyPenalty++;

            if (!totalSessionsByStudent.TryGetValue(group.Key, out var totalSessions) || totalSessions < 2) continue;
            var idealGap = (double)dayOrder.Count / totalSessions;
            var usedRanks = ordered.Select(item => rankByDay.GetValueOrDefault(item.OpenDateId)).Distinct().Order().ToArray();
            for (var i = 1; i < usedRanks.Length; i++)
                gapDeviationPenalty += (long)Math.Round(Math.Abs((usedRanks[i] - usedRanks[i - 1]) - idealGap) * 10);
        }

        return gapDeviationPenalty + (subjectAdjacencyPenalty * 20);
    }
}
