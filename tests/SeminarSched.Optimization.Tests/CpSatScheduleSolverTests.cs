using SeminarSched.Domain.Scheduling;
using SeminarSched.Optimization.Core;

namespace SeminarSched.Optimization.Tests;

public sealed class CpSatScheduleSolverTests
{
    // ユーザー報告: 「グリギング」戦略が名目時間いっぱい走り続けるようになったこと（checkpoint87）で、
    // num_search_workers:0（"任意"）がOR-Tools側で論理コア全てを使い切ってしまい、長時間CPU負荷が
    // 高止まりする問題が顕在化した。少なくとも2並列は維持しつつ（1並列だと実データで40秒経っても
    // feasible解すら出ないことをコメントの通り実測済み）、機械全体を専有しないよう上限を設ける。
    [Fact]
    public void ResolvedAutoSearchWorkers_IsCappedButAtLeastTwo()
    {
        Assert.True(CpSatScheduleSolver.ResolvedAutoSearchWorkers >= 2);
        Assert.True(CpSatScheduleSolver.ResolvedAutoSearchWorkers <= Environment.ProcessorCount);
    }

    // ユーザー要望「CPU使用率を制限しないチェックボックス」を検証する。WorkerLimitEnabledを
    // falseにすると、呼び出し元のNumSearchWorkers:0（既定）がOR-Tools側へそのまま渡る（真の
    // 無制限auto）ようにResolvedAutoSearchWorkersが0を返す。他のテストへ影響しないよう、
    // 元の値へ必ず戻す。
    [Fact]
    public void ResolvedAutoSearchWorkers_ReturnsZeroWhenWorkerLimitDisabled()
    {
        var original = CpSatScheduleSolver.WorkerLimitEnabled;
        try
        {
            CpSatScheduleSolver.WorkerLimitEnabled = false;
            Assert.Equal(0, CpSatScheduleSolver.ResolvedAutoSearchWorkers);
        }
        finally
        {
            CpSatScheduleSolver.WorkerLimitEnabled = original;
        }
    }

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
        // checkpoint94: 日程分散はユーザー要望によりトグル化され既定「考慮しない」になったため、
        // この挙動を検証するにはPolicyで明示的にSpreadを指定する必要がある。
        var policy = new SchedulingPolicy(studentAttendanceDaysPreference: StudentAttendanceDaysPreference.Spread);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, slots, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Equal(2, solution.Placements.Select(p => p.OpenDateId).Distinct().Count());
    }

    // ユーザー要望（checkpoint104）「講師の出勤日について、考慮しない・できるだけ減らす・分散する、
    // を追加してほしい」を検証する。既存の`SolveAsync_ConcentratesATeachersSessionsIntoFewerDays
    // WhenOtherwiseTied`（①TeacherCountPerDayPreference、学校全体で1日あたりに登場する講師の
    // "人数"を絞る仕組み）とは異なる、この新設トグル（④TeacherAttendanceDaysPreference、講師
    // 1人あたりの出勤日数を絞る仕組み）専用の検証。①をNone（既定）のままにし、同じ講師が2件の
    // 受講希望（別々の生徒）のどちらも2日のいずれの候補コマにも配置可能（他の条件は全く同じ）な
    // とき、④にConcentrateを指定すると講師の出勤日数が少ない方（両方とも同じ日）へまとめられる
    // はず。
    [Fact]
    public async Task SolveAsync_PrefersFewerDistinctAttendanceDaysForATeacherWhenOtherwiseTied()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0) };
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1, 1, 1),
            new PlacementCandidate(1, 10, 100, 2, 1, 2, 1),
            new PlacementCandidate(2, 20, 100, 1, 2, 1, 2),
            new PlacementCandidate(2, 20, 100, 2, 2, 2, 2),
        };
        var policy = new SchedulingPolicy(teacherAttendanceDaysPreference: TeacherAttendanceDaysPreference.Concentrate);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Single(solution.Placements.Select(p => p.OpenDateId).Distinct());
    }

    // ユーザー要望（checkpoint107）「同一講師が同一生徒を連続コマで担当するのを避ける/優遇するに
    // ついて、避ける意味はあまりないと思うので、『考慮しない』と『できるだけ連続にする』にして、
    // 新たな探索方針としてください」を検証する。生徒10の受講希望1件目は講師100・1限に固定。
    // 2件目は同じ2限だが、担当講師が講師100（1件目と同じ、隣り合うコマで同一講師×同一生徒になる）
    // か講師200（別講師）かのどちらでも他の条件は全く同じ（tie）。PreferConsecutiveを指定すると、
    // 同一講師（100）の方が選ばれるはず。
    [Fact]
    public async Task SolveAsync_PrefersSameTeacherForAdjacentSlotOfTheSameStudentWhenOtherwiseTied()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 10, 1, 0) };
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1, 1, 1),
            new PlacementCandidate(2, 10, 100, 1, 2, 1, 2),
            new PlacementCandidate(2, 10, 200, 1, 2, 1, 2),
        };
        var policy = new SchedulingPolicy(teacherStudentConsecutivePreference: TeacherStudentConsecutivePreference.PreferConsecutive);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        var demand2Placement = solution.Placements.Single(p => p.RequestId == 2);
        Assert.Equal(100L, demand2Placement.TeacherId);
    }

    // ユーザー要望「1対1が多いように見える。絶対ダメではないが1対2の方がいい」を検証する。2名の
    // 生徒が同じ講師・同じ日の2コマのどちらにも配置可能（1対1必須ではない）とき、他の条件が同じなら
    // 片方のコマへ2名ともまとめて配置（1対2）し、もう片方のコマは空けたままにするはず。
    [Fact]
    public async Task SolveAsync_PrefersPairingTwoStudentsInTheSameSlotOverSplittingAcrossSlotsWhenOtherwiseTied()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0) };
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1),
            new PlacementCandidate(1, 10, 100, 1, 2),
            new PlacementCandidate(2, 20, 100, 1, 1),
            new PlacementCandidate(2, 20, 100, 1, 2),
        };
        // checkpoint94: ペア優遇はユーザー要望によりトグル化され既定「考慮しない」になったため、
        // この挙動を検証するにはPolicyで明示的にMaximizeを指定する必要がある。
        var policy = new SchedulingPolicy(pairingSizePreference: PairingSizePreference.Maximize);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Single(solution.Placements.Select(p => p.TimeSlotId).Distinct());
    }

    // ユーザー要望「講師の空きコマも基本作らないでください」を検証する。1対1必須の2受講希望が、
    // 同じ講師の3コマ（連続する順序）のうちどれか2つを使える（他の条件は同じ）とき、真ん中を
    // 空けたまま両端（1・3コマ目）を使う「空きコマ」配置ではなく、隣接する2コマを使うはず。
    [Fact]
    public async Task SolveAsync_PrefersAdjacentTeacherSlotsOverLeavingAGapBetweenThemWhenOtherwiseTied()
    {
        var demands = new[]
        {
            new LessonDemand(1, 10, 1, 0, OneToOneRequired: true),
            new LessonDemand(2, 20, 1, 0, OneToOneRequired: true),
        };
        var candidates = Enumerable.Range(1, 3)
            .SelectMany(slot => new[]
            {
                new PlacementCandidate(1, 10, 100, 1, slot, SlotOrder: slot, OneToOneRequired: true),
                new PlacementCandidate(2, 20, 100, 1, slot, SlotOrder: slot, OneToOneRequired: true),
            })
            .ToArray();
        var slots = Enumerable.Range(1, 3).Select(slot => new ScheduleSlot(1, slot, 0, slot)).ToArray();

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, slots), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        var usedSlots = solution.Placements.Select(p => p.TimeSlotId).OrderBy(x => x).ToArray();
        Assert.Equal(1, usedSlots[1] - usedSlots[0]);
    }

    // ユーザー要望「1日当たりのコマ数も多い方がいい。Aタイムのためだけに出勤させるのは申し訳ない」を
    // 検証する。1対1必須の2受講希望が、同じ講師の2日間×各2コマのどれでも使える（他の条件は同じ）
    // とき、2日に1コマずつ分散させるのではなく、1日へ集約して2コマとも使うはず。
    [Fact]
    public async Task SolveAsync_ConcentratesATeachersSessionsIntoFewerDaysWhenOtherwiseTied()
    {
        var demands = new[]
        {
            new LessonDemand(1, 10, 1, 0, OneToOneRequired: true),
            new LessonDemand(2, 20, 1, 0, OneToOneRequired: true),
        };
        var candidates = new[] { 1L, 2L }
            .SelectMany(day => new[] { 1L, 2L }.SelectMany(slot => new[]
            {
                new PlacementCandidate(1, 10, 100, day, slot, OneToOneRequired: true),
                new PlacementCandidate(2, 20, 100, day, slot, OneToOneRequired: true),
            }))
            .ToArray();
        var slots = new[] { 1L, 2L }.SelectMany(day => new[] { 1L, 2L }.Select(slot => new ScheduleSlot(day, slot, (int)day, (int)slot))).ToArray();
        // checkpoint94: 講師の出勤日集約はユーザー要望によりトグル化され既定「考慮しない」になったため、
        // この挙動を検証するにはPolicyで明示的にMinimizeを指定する必要がある。
        var policy = new SchedulingPolicy(teacherCountPerDayPreference: TeacherCountPerDayPreference.Minimize);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, slots, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(2, solution.Placements.Count);
        Assert.Single(solution.Placements.Select(p => p.OpenDateId).Distinct());
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

    // ユーザー要望「担当する生徒の人数（既定1対2）を1対3・1対4等へ変更できるようにしたい」を検証する。
    // MaxStudentsPerTeacher=3の下で、同じ講師・同じコマに3名とも配置可能（1対1必須ではない）なら、
    // 3名全員が配置されるはず（既定の2までしか許さないハード制約のままなら1名は必ず未配置になる）。
    [Fact]
    public async Task SolveAsync_AllowsUpToConfiguredMaxStudentsPerTeacher()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0), new LessonDemand(3, 30, 1, 0) };
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1),
            new PlacementCandidate(2, 20, 100, 1, 1),
            new PlacementCandidate(3, 30, 100, 1, 1),
        };
        var policy = new SchedulingPolicy(maxStudentsPerTeacher: 3);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Equal(3, solution.Placements.Count);
        Assert.Equal(0, solution.UnassignedLessons);
        ScheduleSolutionValidator.Validate(new ScheduleProblem(demands, candidates, Policy: policy), solution);
    }

    // ユーザー要望⑥「同時に使える座席数を『〇人までに設定する』」を検証する。学校全体で同時刻に
    // MaxConcurrentSeats=1しか許さない場合、2名の生徒が別々の講師で同じ日時にしか対応できなくても、
    // 同時に配置できるのはどちらか1名だけのはず。
    [Fact]
    public async Task SolveAsync_RespectsMaxConcurrentSeatsAcrossDifferentTeachers()
    {
        var demands = new[] { new LessonDemand(1, 10, 1, 0), new LessonDemand(2, 20, 1, 0) };
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1, OneToOneRequired: true),
            new PlacementCandidate(2, 20, 200, 1, 1, OneToOneRequired: true),
        };
        var policy = new SchedulingPolicy(maxConcurrentSeats: 1);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem(demands, candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Single(solution.Placements);
        Assert.Equal(1, solution.UnassignedLessons);
    }

    // ユーザー要望⑤「時間帯をできるだけ遅くする」を検証する。1受講希望が同日の2コマ（早い/遅い）の
    // どちらでも配置可能で他の条件が同じとき、Lateを指定すれば遅いコマを、Earlyを指定すれば早いコマを
    // 選ぶはず。
    [Theory]
    [InlineData(TimeOfDayPreference.Late, 2)]
    [InlineData(TimeOfDayPreference.Early, 1)]
    public async Task SolveAsync_PrefersConfiguredTimeOfDayWhenOtherwiseTied(TimeOfDayPreference preference, int expectedSlotOrder)
    {
        var demand = new LessonDemand(1, 10, 1, 0);
        var candidates = new[]
        {
            new PlacementCandidate(1, 10, 100, 1, 1, SlotOrder: 1),
            new PlacementCandidate(1, 10, 100, 1, 2, SlotOrder: 2),
        };
        var policy = new SchedulingPolicy(timeOfDayPreference: preference);

        var solution = await new CpSatScheduleSolver().SolveAsync(new ScheduleProblem([demand], candidates, Policy: policy), TimeSpan.FromSeconds(5));

        Assert.Single(solution.Placements);
        Assert.Equal(expectedSlotOrder, solution.Placements[0].TimeSlotId);
    }
}
