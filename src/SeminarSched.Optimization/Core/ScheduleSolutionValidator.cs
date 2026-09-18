namespace SeminarSched.Optimization.Core;

public static class ScheduleSolutionValidator
{
    public static void Validate(ScheduleProblem problem, ScheduleSolution solution)
    {
        var candidatesByKey = problem.Candidates.ToDictionary(CandidateKey);
        foreach (var placement in solution.Placements)
        {
            if (!candidatesByKey.ContainsKey(PlacementKey(placement)))
                throw new InvalidDataException("Solver returned a placement outside the candidate set.");
        }

        var studentOccupancy = solution.Placements.Select(placement => (placement.StudentId, placement.OpenDateId, placement.TimeSlotId))
            .Concat(problem.ExistingPlacements.Select(placement => (placement.StudentId, placement.OpenDateId, placement.TimeSlotId)));
        if (studentOccupancy.GroupBy(item => item).Any(group => group.Count() > 1))
            throw new InvalidDataException("Student collision detected.");

        foreach (var demand in problem.Demands)
        {
            var count = solution.Placements.Count(placement => placement.RequestId == demand.RequestId) + demand.AlreadyFixedSessions;
            if (count > demand.RequiredSessions)
                throw new InvalidDataException("A lesson request was over-assigned.");
        }

        var teacherLoads = solution.Placements.Select(placement =>
        {
            var candidate = candidatesByKey[PlacementKey(placement)];
            return (placement.TeacherId, placement.OpenDateId, placement.TimeSlotId, Load: candidate.OneToOneRequired ? 2 : 1);
        }).Concat(problem.ExistingPlacements.Select(placement =>
            (placement.TeacherId, placement.OpenDateId, placement.TimeSlotId, Load: placement.OneToOneRequired ? 2 : 1)));
        if (teacherLoads.GroupBy(item => (item.TeacherId, item.OpenDateId, item.TimeSlotId)).Any(group => group.Sum(item => item.Load) > 2))
            throw new InvalidDataException("Teacher capacity violation detected.");

        // Regular-teacher minimums are enforced by CpSatScheduleSolver as a penalized soft target
        // (AddRegularTeacherMinimums), not a hard requirement - two demands for the same student
        // can each look individually achievable yet still collide, so a solution that falls short
        // of one or more targets is a valid, structurally-correct outcome and not checked here.
        ValidateStudentSequences(problem, solution, candidatesByKey);

        var expected = problem.Demands.Sum(demand => Math.Max(
            0,
            demand.RequiredSessions - demand.AlreadyFixedSessions - solution.Placements.Count(placement => placement.RequestId == demand.RequestId)));
        if (expected != solution.UnassignedLessons)
            throw new InvalidDataException("Unassigned lesson count is inconsistent.");
    }

    private static void ValidateStudentSequences(
        ScheduleProblem problem,
        ScheduleSolution solution,
        IReadOnlyDictionary<(long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId), PlacementCandidate> candidatesByKey)
    {
        var demandByRequest = problem.Demands.ToDictionary(demand => demand.RequestId);
        var selected = solution.Placements.Select(placement =>
        {
            var candidate = candidatesByKey[PlacementKey(placement)];
            return (placement.RequestId, placement.StudentId, placement.OpenDateId, candidate.SlotOrder);
        });
        var all = selected.Concat(problem.ExistingPlacements.Select(placement =>
            (placement.RequestId, placement.StudentId, placement.OpenDateId, placement.SlotOrder)));

        foreach (var day in all.GroupBy(placement => (placement.StudentId, placement.OpenDateId)))
        {
            var occupied = day.Select(placement => placement.SlotOrder).Distinct().Order().ToArray();
            var maximum = day.Select(placement => demandByRequest[placement.RequestId].MaxConsecutiveSlots).Min();
            var allowGap = day.All(placement => demandByRequest[placement.RequestId].AllowGap);
            var slotOrders = problem.AvailableSlots
                .Where(slot => slot.OpenDateId == day.Key.OpenDateId)
                .Select(slot => slot.SlotOrder)
                .Distinct()
                .Order()
                .ToArray();

            for (var start = 0; start + maximum < slotOrders.Length; start++)
            {
                if (slotOrders.Skip(start).Take(maximum + 1).Count(occupied.Contains) > maximum)
                    throw new InvalidDataException("Maximum consecutive lesson count was exceeded.");
            }

            if (!allowGap && occupied.Length > 1)
            {
                var first = Array.IndexOf(slotOrders, occupied[0]);
                var last = Array.IndexOf(slotOrders, occupied[^1]);
                if (first >= 0 && last >= 0 && slotOrders.Skip(first).Take(last - first + 1).Any(order => !occupied.Contains(order)))
                    throw new InvalidDataException("A disallowed gap was detected.");
            }
        }
    }

    private static (long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId) CandidateKey(PlacementCandidate candidate) =>
        (candidate.RequestId, candidate.StudentId, candidate.TeacherId, candidate.OpenDateId, candidate.TimeSlotId);

    private static (long RequestId, long StudentId, long TeacherId, long OpenDateId, long TimeSlotId) PlacementKey(SchedulePlacement placement) =>
        (placement.RequestId, placement.StudentId, placement.TeacherId, placement.OpenDateId, placement.TimeSlotId);
}
