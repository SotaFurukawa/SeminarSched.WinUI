using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;

namespace SeminarSched.Optimization.Core;

public sealed class CpSatScheduleSolver
{
    public Task<ScheduleSolution> SolveAsync(
        ScheduleProblem problem,
        TimeSpan maximumDuration,
        int randomSeed = 1,
        CancellationToken cancellationToken = default)
    {
        if (maximumDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        return Task.Run(() => Solve(problem, maximumDuration, randomSeed, cancellationToken), cancellationToken);
    }

    private static ScheduleSolution Solve(ScheduleProblem problem, TimeSpan maximum, int seed, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var model = new CpModel();
        var variables = problem.Candidates
            .Select((candidate, index) => (candidate, variable: model.NewBoolVar($"x_{index}")))
            .ToArray();

        foreach (var group in variables.GroupBy(item => item.candidate.RequestId))
        {
            var demand = problem.Demands.Single(item => item.RequestId == group.Key);
            model.Add(LinearExpr.Sum(group.Select(item => item.variable)) <= Math.Max(0, demand.RequiredSessions - demand.AlreadyFixedSessions));
        }

        foreach (var group in variables.GroupBy(item => (item.candidate.StudentId, item.candidate.OpenDateId, item.candidate.TimeSlotId)))
            model.Add(LinearExpr.Sum(group.Select(item => item.variable)) <= 1);

        foreach (var group in variables.GroupBy(item => (item.candidate.TeacherId, item.candidate.OpenDateId, item.candidate.TimeSlotId)))
        {
            var weighted = group.Select(item => LinearExpr.Term(item.variable, item.candidate.OneToOneRequired ? 2 : 1));
            var fixedLoad = problem.ExistingPlacements
                .Where(item => item.TeacherId == group.Key.TeacherId && item.OpenDateId == group.Key.OpenDateId && item.TimeSlotId == group.Key.TimeSlotId)
                .Sum(item => item.OneToOneRequired ? 2 : 1);
            model.Add(LinearExpr.Sum(weighted) <= 2 - fixedLoad);
        }

        var regularTeacherShortfallTerms = AddRegularTeacherMinimums(model, problem, variables).ToArray();
        AddStudentConsecutiveAndGapConstraints(model, problem, variables);

        // Assignment count dominates every soft penalty, so a prettier timetable can never
        // replace an otherwise assignable lesson with an unassigned lesson. Day-spread ranks
        // above teacher-preference matching per the Python reference's lexicographic order
        // (v1.9.5 objectives.py), so it is weighted above the *100 preference term but stays
        // far below the 1,000,000-per-placement floor. The regular-teacher shortfall penalty
        // (see AddRegularTeacherMinimums) ranks between day-spread and the placement floor.
        var objectiveTerms = variables.Select(item =>
            LinearExpr.Term(item.variable, 1_000_000L - (item.candidate.PreferencePenalty * 100L) + item.candidate.AvailabilityPreference))
            .Concat(BuildDayDispersionTerms(model, variables))
            .Concat(regularTeacherShortfallTerms);
        model.Maximize(LinearExpr.Sum(objectiveTerms));

        var solver = new CpSolver
        {
            StringParameters = $"max_time_in_seconds:{maximum.TotalSeconds.ToString(CultureInfo.InvariantCulture)} random_seed:{seed} num_search_workers:0",
        };
        using var registration = cancellationToken.Register(solver.StopSearch);
        var status = solver.Solve(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (status is not CpSolverStatus.Optimal and not CpSolverStatus.Feasible)
            throw new InvalidOperationException($"時間割を作成できませんでした: {status}");

        var placements = variables
            .Where(item => solver.BooleanValue(item.variable))
            .Select(item => new SchedulePlacement(
                item.candidate.RequestId,
                item.candidate.StudentId,
                item.candidate.TeacherId,
                item.candidate.OpenDateId,
                item.candidate.TimeSlotId))
            .ToArray();
        var unassigned = problem.Demands.Sum(demand => Math.Max(
            0,
            demand.RequiredSessions - demand.AlreadyFixedSessions - placements.Count(item => item.RequestId == demand.RequestId)));
        var selectedCandidates = variables.Where(item => solver.BooleanValue(item.variable)).Select(item => item.candidate).ToArray();
        var objective = (unassigned * 1_000_000L) + selectedCandidates.Sum(item => (item.PreferencePenalty * 100L) - item.AvailabilityPreference);
        var solution = new ScheduleSolution(placements, unassigned, objective, watch.Elapsed);
        ScheduleSolutionValidator.Validate(problem, solution);
        return solution;
    }

    private const long DayDispersionWeight = 10_000L;

    /// <summary>
    /// For each student, rewards using more distinct days rather than concentrating a student's
    /// several lesson-request sessions onto as few days as possible (Python v1.9.5 objectives.py
    /// tier 3: "同一日への過度な集中を抑制").
    /// </summary>
    private static IEnumerable<LinearExpr> BuildDayDispersionTerms(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        var index = 0;
        foreach (var group in variables.GroupBy(item => (item.candidate.StudentId, item.candidate.OpenDateId)))
        {
            var dayVariables = group.Select(item => item.variable).ToArray();
            if (dayVariables.Length <= 1)
            {
                yield return LinearExpr.Term(dayVariables[0], DayDispersionWeight);
                continue;
            }
            var dayUsed = model.NewBoolVar($"day_used_{index++}");
            model.AddMaxEquality(dayUsed, dayVariables);
            yield return LinearExpr.Term(dayUsed, DayDispersionWeight);
        }
    }

    private const long RegularTeacherShortfallWeight = 100_000L;

    /// <summary>
    /// A student's several regular-teacher requirements (one per subject) each independently look
    /// satisfiable from their own candidate pool, but the pools are not independent: two subjects
    /// sharing the same student compete for the same limited (date, slot) capacity, and a fixed
    /// regular teacher's own availability is shared across every student assigned to them. Forcing
    /// every demand's minimum as a hard lower bound (as a naive per-demand cap-at-candidate-count
    /// guard alone would do) can therefore still make the whole model Infeasible once two demands'
    /// forced slots collide - confirmed against a real 57-student/16-teacher import where every
    /// student had a regular teacher recorded and about a quarter had two. So each shortfall below
    /// the achievable minimum is tracked with a slack variable and only penalized in the objective,
    /// never forced, keeping a schedule findable even when not every regular-teacher target fits.
    /// </summary>
    private static IEnumerable<LinearExpr> AddRegularTeacherMinimums(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        foreach (var demand in problem.Demands.Where(item => item.RegularTeacherId is not null && item.RegularTeacherPriority >= 2))
        {
            var requiredMinimum = MinimumRegularTeacherSessions(demand.RequiredSessions, demand.RegularTeacherPriority);
            var remainingMinimum = Math.Max(0, requiredMinimum - demand.FixedRegularTeacherSessions);
            if (remainingMinimum == 0) continue;
            var regular = variables
                .Where(item => item.candidate.RequestId == demand.RequestId && item.candidate.TeacherId == demand.RegularTeacherId)
                .Select(item => item.variable)
                .ToArray();
            var achievable = Math.Min(remainingMinimum, regular.Length);
            if (achievable == 0) continue;
            var shortfall = model.NewIntVar(0, achievable, $"regular_shortfall_{demand.RequestId}");
            model.Add(LinearExpr.Sum(regular) + shortfall >= achievable);
            yield return LinearExpr.Term(shortfall, -RegularTeacherShortfallWeight);
        }
    }

    private static void AddStudentConsecutiveAndGapConstraints(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        var demandByRequest = problem.Demands.ToDictionary(item => item.RequestId);
        var studentDays = variables.Select(item => (item.candidate.StudentId, item.candidate.OpenDateId))
            .Concat(problem.ExistingPlacements.Select(item => (item.StudentId, item.OpenDateId)))
            .Distinct();
        foreach (var studentDayKey in studentDays)
        {
            var studentDay = variables.Where(item => item.candidate.StudentId == studentDayKey.StudentId && item.candidate.OpenDateId == studentDayKey.OpenDateId).ToArray();
            var fixedForDay = problem.ExistingPlacements.Where(item => item.StudentId == studentDayKey.StudentId && item.OpenDateId == studentDayKey.OpenDateId).ToArray();
            var involvedRequests = studentDay.Select(item => item.candidate.RequestId).Concat(fixedForDay.Select(item => item.RequestId)).Distinct();
            var maximum = involvedRequests.Select(requestId => demandByRequest[requestId].MaxConsecutiveSlots).DefaultIfEmpty(2).Min();
            var allowGap = involvedRequests.All(requestId => demandByRequest[requestId].AllowGap);
            var slotOrders = problem.AvailableSlots.Where(slot => slot.OpenDateId == studentDayKey.OpenDateId).Select(slot => slot.SlotOrder).Distinct().Order().ToArray();
            var occupancy = slotOrders.Select(slotOrder => new
            {
                Variables = studentDay.Where(item => item.candidate.SlotOrder == slotOrder).Select(item => item.variable).ToArray(),
                Fixed = fixedForDay.Any(item => item.SlotOrder == slotOrder) ? 1 : 0,
            }).ToArray();
            for (var start = 0; start + maximum < occupancy.Length; start++)
            {
                var window = occupancy.Skip(start).Take(maximum + 1).ToArray();
                model.Add(LinearExpr.Sum(window.SelectMany(item => item.Variables)) <= maximum - window.Sum(item => item.Fixed));
            }

            if (allowGap) continue;
            for (var first = 0; first < occupancy.Length; first++)
            for (var last = first + 2; last < occupancy.Length; last++)
            for (var middle = first + 1; middle < last; middle++)
            {
                var firstExpression = LinearExpr.Sum(occupancy[first].Variables) + occupancy[first].Fixed;
                var middleExpression = LinearExpr.Sum(occupancy[middle].Variables) + occupancy[middle].Fixed;
                var lastExpression = LinearExpr.Sum(occupancy[last].Variables) + occupancy[last].Fixed;
                model.Add(firstExpression + lastExpression - middleExpression <= 1);
            }
        }
    }

    internal static int MinimumRegularTeacherSessions(int requiredSessions, int priority)
    {
        if (requiredSessions < 0) throw new ArgumentOutOfRangeException(nameof(requiredSessions));
        if (priority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(priority));
        return ((priority - 1) * requiredSessions + 3) / 4;
    }
}
