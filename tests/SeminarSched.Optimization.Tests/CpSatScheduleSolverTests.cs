using SeminarSched.Optimization.Core;

namespace SeminarSched.Optimization.Tests;

public sealed class CpSatScheduleSolverTests
{
    [Fact]
    public async Task SolveAsync_AssignsAllLessonsWithoutStudentOrTeacherCollision()
    {
        var problem=new ScheduleProblem(
            [new LessonDemand(1,10,1,0),new LessonDemand(2,20,1,0)],
            [new PlacementCandidate(1,10,100,1,1),new PlacementCandidate(1,10,100,1,2),new PlacementCandidate(2,20,100,1,1),new PlacementCandidate(2,20,100,1,2)]);
        var solution=await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(5));
        Assert.Equal(2,solution.Placements.Count);Assert.Equal(0,solution.UnassignedLessons);ScheduleSolutionValidator.Validate(problem,solution);
    }
    [Fact]
    public async Task SolveAsync_NoCandidates_ReportsUnassigned()
    {
        var problem=new ScheduleProblem([new LessonDemand(1,10,3,1)],[]);
        var solution=await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(1));
        Assert.Equal(2,solution.UnassignedLessons);Assert.Empty(solution.Placements);
    }
    [Fact]
    public async Task SolveAsync_FreeRequestIdsFreezesEverythingElseToTheHint()
    {
        // Two independent demands, each with two equally-cheap slots on different days. Left
        // unconstrained, the solver is free to pick either day for either demand. With a hint plus
        // an empty FreeRequestIds set, every candidate must match the hint's decision exactly -
        // a solver still picks *a* solution, but it must be the hinted one, not a fresh optimum.
        var problem = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0)],
            [
                new PlacementCandidate(1, 10, 100, 1, 1),
                new PlacementCandidate(1, 10, 100, 2, 1),
                new PlacementCandidate(2, 20, 200, 1, 1),
                new PlacementCandidate(2, 20, 200, 2, 1),
            ]);
        var hint = new ScheduleSolution(
            [new SchedulePlacement(1, 10, 100, 2, 1), new SchedulePlacement(2, 20, 200, 2, 1)],
            0, 0, TimeSpan.Zero);

        var solver = new CpSatScheduleSolver();
        var frozen = await solver.SolveAsync(problem, new CpSatSolveOptions(TimeSpan.FromSeconds(5), Hint: hint, FreeRequestIds: new HashSet<long>()));

        Assert.Equal(2, frozen.Placements.Count);
        Assert.All(frozen.Placements, placement => Assert.Equal(2L, placement.OpenDateId));
        ScheduleSolutionValidator.Validate(problem, frozen);
    }

    [Fact]
    public async Task SolveAsync_FreeRequestIdsLeavesListedRequestsFreeToChange()
    {
        // Same setup as above, but demand 2 is listed as "free": the solver may move it away from
        // the hinted day while demand 1 (not listed) must still match the hint exactly.
        var problem = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0)],
            [
                new PlacementCandidate(1, 10, 100, 1, 1),
                new PlacementCandidate(1, 10, 100, 2, 1),
                new PlacementCandidate(2, 20, 200, 1, 1, PreferencePenalty: 5),
                new PlacementCandidate(2, 20, 200, 2, 1, PreferencePenalty: 0),
            ]);
        var hint = new ScheduleSolution(
            [new SchedulePlacement(1, 10, 100, 2, 1), new SchedulePlacement(2, 20, 200, 2, 1)],
            0, 0, TimeSpan.Zero);

        var solver = new CpSatScheduleSolver();
        var partiallyFrozen = await solver.SolveAsync(problem, new CpSatSolveOptions(TimeSpan.FromSeconds(5), Hint: hint, FreeRequestIds: new HashSet<long> { 2 }));

        Assert.Equal(2L, Assert.Single(partiallyFrozen.Placements, p => p.RequestId == 1).OpenDateId);
        ScheduleSolutionValidator.Validate(problem, partiallyFrozen);
    }

    [Fact]
    public void Validator_RejectsCandidateOutsideInput()
    {
        var problem=new ScheduleProblem([new LessonDemand(1,10,1,0)],[]);var solution=new ScheduleSolution([new SchedulePlacement(1,10,1,1,1)],0,0,TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(()=>ScheduleSolutionValidator.Validate(problem,solution));
    }

    [Fact]
    public async Task SolveAsync_TeacherCanTeachTwoStudentsUnlessOneToOneIsRequired()
    {
        var pairable = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0)],
            [new PlacementCandidate(1, 10, 100, 1, 1), new PlacementCandidate(2, 20, 100, 1, 1)]);
        var paired = await new CpSatScheduleSolver().SolveAsync(pairable, TimeSpan.FromSeconds(2));
        Assert.Equal(2, paired.Placements.Count);

        var oneToOne = new ScheduleProblem(
            [new LessonDemand(1, 10, 1, 0, OneToOneRequired: true), new LessonDemand(2, 20, 1, 0)],
            [new PlacementCandidate(1, 10, 100, 1, 1, OneToOneRequired: true), new PlacementCandidate(2, 20, 100, 1, 1)]);
        var separated = await new CpSatScheduleSolver().SolveAsync(oneToOne, TimeSpan.FromSeconds(2));
        Assert.Single(separated.Placements);
        Assert.Equal(1, separated.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_PreservesRegularTeacherMinimumAndPrefersBetterTeacher()
    {
        var demand = new LessonDemand(1, 10, 4, 0, RegularTeacherId: 100, RegularTeacherPriority: 3);
        var candidates = Enumerable.Range(1, 4)
            .SelectMany(day => new[]
            {
                new PlacementCandidate(1, 10, 100, day, 1, day, 1, PreferencePenalty: 0),
                new PlacementCandidate(1, 10, 200, day, 1, day, 1, PreferencePenalty: 5),
            })
            .ToArray();

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([demand], candidates), TimeSpan.FromSeconds(2));

        Assert.Equal(4, solution.Placements.Count);
        Assert.True(solution.Placements.Count(item => item.TeacherId == 100) >= 2);
        Assert.All(solution.Placements, item => Assert.Equal(100, item.TeacherId));
    }

    // ユーザー指定の目安（優先度が1下がるごとに通常担当講師の最低保証割合が30ポイント下がる:
    // 5→100%・4→70%・3→40%・2→10%）に沿って、MinimumRegularTeacherSessionsの実際の閾値を
    // SolveAsync経由で（内部関数は非公開のため）間接的に検証する。
    [Theory]
    [InlineData(4, 10, 7)] // 70% of 10, ceiling
    [InlineData(2, 10, 1)] // 10% of 10, ceiling
    public async Task SolveAsync_PreservesRegularTeacherMinimumAtEachPriorityLevel(int priority, int requiredSessions, int expectedMinimum)
    {
        var demand = new LessonDemand(1, 10, requiredSessions, 0, RegularTeacherId: 100, RegularTeacherPriority: priority);
        var candidates = Enumerable.Range(1, requiredSessions)
            .SelectMany(day => new[]
            {
                new PlacementCandidate(1, 10, 100, day, 1, day, 1, PreferencePenalty: 0),
                new PlacementCandidate(1, 10, 200, day, 1, day, 1, PreferencePenalty: 5),
            })
            .ToArray();

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([demand], candidates), TimeSpan.FromSeconds(5));

        Assert.Equal(requiredSessions, solution.Placements.Count);
        Assert.True(solution.Placements.Count(item => item.TeacherId == 100) >= expectedMinimum);
    }

    [Fact]
    public async Task SolveAsync_RespectsMaximumConsecutiveSlots()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1, 10, 3, 0, MaxConsecutiveSlots: 2)],
            [
                new PlacementCandidate(1, 10, 100, 1, 1, SlotOrder: 1),
                new PlacementCandidate(1, 10, 100, 1, 2, SlotOrder: 2),
                new PlacementCandidate(1, 10, 100, 1, 3, SlotOrder: 3),
            ]);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(2));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Equal(1, solution.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_FixedPairableLesson_LeavesOneTeacherSeat()
    {
        var problem = new ScheduleProblem(
            [new LessonDemand(1,10,1,1),new LessonDemand(2,20,1,0),new LessonDemand(3,30,1,0)],
            [new PlacementCandidate(2,20,100,1,1),new PlacementCandidate(3,30,100,1,1)],
            [new ScheduleSlot(1,1,0,1)],
            [new FixedPlacement(1,10,100,1,1,0,1)]);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem,TimeSpan.FromSeconds(2));

        Assert.Single(solution.Placements);
        Assert.Equal(1,solution.UnassignedLessons);
    }

    [Fact]
    public async Task SolveAsync_NoGap_UsesCompleteOpenSlotSequence()
    {
        var candidates = new[]
        {
            new PlacementCandidate(1,10,100,1,1,SlotOrder:1),
            new PlacementCandidate(1,10,100,1,3,SlotOrder:3),
        };
        var slots = new[] { new ScheduleSlot(1,1,0,1),new ScheduleSlot(1,2,0,2),new ScheduleSlot(1,3,0,3) };
        var noGap = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([new LessonDemand(1,10,2,0,AllowGap:false)],candidates,slots),TimeSpan.FromSeconds(2));
        var gapAllowed = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([new LessonDemand(1,10,2,0,AllowGap:true)],candidates,slots),TimeSpan.FromSeconds(2));

        Assert.Single(noGap.Placements);
        Assert.Equal(2,gapAllowed.Placements.Count);
    }

    [Fact]
    public async Task SolveAsync_PrefersSpreadingAStudentsSessionsAcrossDistinctDaysWhenOtherwiseTied()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 10, 1, 0) };
        var candidates = new[] { 1L, 2L }
            .SelectMany(requestId => new[] { 1L, 2L }.SelectMany(day => new[]
            {
                new PlacementCandidate(requestId, 10, 100, day, 1, (int)day, 1),
                new PlacementCandidate(requestId, 10, 100, day, 2, (int)day, 2),
            }))
            .ToArray();
        var slots = new[] { 1L, 2L }.SelectMany(day => new[] { new ScheduleSlot(day, 1, (int)day, 1), new ScheduleSlot(day, 2, (int)day, 2) }).ToArray();

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, slots), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Equal(2, solution.Placements.Select(p => p.OpenDateId).Distinct().Count());
    }

    [Fact]
    public void Validator_AcceptsUnsatisfiedRegularTeacherMinimum()
    {
        // The regular-teacher minimum is a penalized soft target inside the solver (see
        // SolveAsync_RegularTeacherMinimumBecomesSoftWhenTwoDemandsCollide below), not a hard
        // invariant every structurally-valid solution must meet, so the validator must not reject
        // a solution just because it fell short of one.
        var problem = new ScheduleProblem(
            [new LessonDemand(1,10,1,0,RegularTeacherId:100,RegularTeacherPriority:5)],
            [new PlacementCandidate(1,10,100,1,1),new PlacementCandidate(1,10,200,1,1)]);
        var solution = new ScheduleSolution([new SchedulePlacement(1,10,200,1,1)],0,0,TimeSpan.Zero);

        ScheduleSolutionValidator.Validate(problem,solution);
    }

    // ユーザー要望「授業と授業の間隔が（休みの日を除いて）常に同じ程度であると良い。授業日が25回
    // あったら大体3日空けくらいに」を検証する。9日間・1受講希望3回で、他の条件が全く同じ場合
    // （どの日を選んでも点数が変わらないPreferencePenalty/AvailabilityPreference）、9÷3=3日おき
    // に近い散らばりを持つ配置を、1〜3日目に固まった配置より優先するはず。
    [Fact]
    public async Task SolveAsync_PrefersEvenlySpacedLessonDaysOverClusteringWhenOtherwiseTied()
    {
        var demand = new LessonDemand(1, 10, 3, 0);
        var candidates = Enumerable.Range(1, 9)
            .Select(day => new PlacementCandidate(1, 10, 100, day, 1, day, 1))
            .ToArray();
        var problem = new ScheduleProblem([demand], candidates);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(5));

        Assert.Equal(3, solution.Placements.Count);
        var days = solution.Placements.Select(item => item.OpenDateId).OrderBy(item => item).ToArray();
        var span = days[^1] - days[0];
        Assert.True(span >= 6, $"expected lessons spread across most of the 9-day range (ideal gap 3 days), got days {string.Join(",", days)}");
    }

    // ユーザー要望「数数数数英英英英ではなく数英数英…に近い方が良い」を検証する。同じ生徒の2科目
    // （各4回、16日間、科目ごとに別講師・別コマなので同日に両方入れることも可能）で、他の条件が
    // 同じ場合、日付順に並べたときに同じ科目が隣り合う回数が、完全な「前半後半で固まる」配置
    // （6回連続）よりずっと少ないはず。
    [Fact]
    public async Task SolveAsync_InterleavesTwoSubjectsInsteadOfClusteringSameSubjectSessions()
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

        var solution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(8));

        Assert.Equal(8, solution.Placements.Count);
        var orderedRequestIds = solution.Placements.OrderBy(item => item.OpenDateId).Select(item => item.RequestId).ToArray();
        var adjacentSameSubjectCount = Enumerable.Range(1, orderedRequestIds.Length - 1)
            .Count(i => orderedRequestIds[i] == orderedRequestIds[i - 1]);
        Assert.True(adjacentSameSubjectCount <= 2, $"expected mostly-alternating subjects, got sequence {string.Join(",", orderedRequestIds)}");
    }

    [Fact]
    public async Task SolveAsync_RegularTeacherMinimumBecomesSoftWhenTwoDemandsCollide()
    {
        // Reproduces the real-world infeasibility found against a real 57-student import: two
        // subjects for the same student each have exactly one available candidate slot, both at
        // the same (student, date, slot), and each subject's regular-teacher minimum is fully
        // achievable from its own candidate pool in isolation. Forcing both as hard lower bounds
        // (the old behavior) made the whole model Infeasible, since the student can only occupy
        // that slot once. The solver must still return a solution instead of throwing.
        var demandA = new LessonDemand(1, 10, 2, 0, RegularTeacherId: 100, RegularTeacherPriority: 5);
        var demandB = new LessonDemand(2, 10, 2, 0, RegularTeacherId: 200, RegularTeacherPriority: 5);
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1),
            new PlacementCandidate(2, 10, 200, 1, 1),
        };
        var problem = new ScheduleProblem([demandA, demandB], candidates);

        var solution = await new CpSatScheduleSolver().SolveAsync(problem, TimeSpan.FromSeconds(2));

        Assert.True(solution.Placements.Count <= 1);
        ScheduleSolutionValidator.Validate(problem, solution);
    }
}
