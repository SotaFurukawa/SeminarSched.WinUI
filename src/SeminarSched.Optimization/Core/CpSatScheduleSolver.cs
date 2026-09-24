using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;
using SeminarSched.Domain.Scheduling;

namespace SeminarSched.Optimization.Core;

/// <summary>
/// Tuning knobs shared by every CP-SAT-based strategy. <see cref="Hint"/> is a previously found
/// solution used to warm-start the search (via CpModel.AddHint); when <see cref="FreeRequestIds"/>
/// is also supplied, every request NOT in that set is additionally hard-fixed to its hint decision
/// (Large Neighborhood Search: re-optimize only the "free" requests' neighborhood while trusting the
/// rest of a known-good solution unchanged - always feasible, since the hint itself already satisfies
/// every hard constraint).
/// </summary>
/// <remarks>
/// <see cref="NumSearchWorkers"/> defaults to 0 ("let OR-Tools decide"), not 1. Measured directly
/// against a real 57-student/83-request import (41,575 candidate variables): num_search_workers:1
/// (true single-threaded search) never even reached a feasible solution in 40 seconds, while 0 solved
/// it in 32.4s - CP-SAT's own automatic parallelism matters enormously at this problem size, so no
/// caller should have to remember to override this just to get the previously-working behavior back.
/// A caller-supplied 0 is not passed to CP-SAT literally, though: <see cref="CpSatScheduleSolver"/>
/// resolves it to <see cref="ResolvedAutoSearchWorkers"/> (a capped worker count), since letting
/// OR-Tools pick unbounded parallelism pins every logical core for the run's entire duration - tolerable
/// for a solve that finishes in seconds, but a real problem for the "grinding" strategies that now
/// deliberately keep re-solving for the whole nominal budget (reported by a user as unacceptably heavy
/// CPU load on their machine during Highest-quality runs). Until checkpoint93, every concrete strategy
/// in <c>CpSatStrategies.cs</c> explicitly passed a positive worker count anyway (matching
/// <c>Environment.ProcessorCount</c>), which silently defeated this whole mechanism - the cap existed
/// but nothing ever actually asked for "auto". Fixed alongside a report that Highest-quality still
/// would not finish in 2 hours even with the OS-level throttling from checkpoint92, since the search
/// itself had never actually been running at a reduced worker count.
/// </remarks>
public sealed record CpSatSolveOptions(
    TimeSpan MaximumDuration,
    int RandomSeed = 1,
    int NumSearchWorkers = 0,
    string? SearchBranching = null,
    ScheduleSolution? Hint = null,
    IReadOnlySet<long>? FreeRequestIds = null);

public sealed class CpSatScheduleSolver
{
    /// <summary>Set false to let a caller's <c>NumSearchWorkers: 0</c> pass through to CP-SAT literally
    /// (true unbounded auto-parallelism) instead of being resolved to <see cref="ResolvedAutoSearchWorkers"/>.
    /// This is the "CPU使用率を制限しない" escape hatch checkbox on the ⑤画面: a single process-wide
    /// switch is safe here because only one optimization run is ever active at a time
    /// (<c>OptimizationRunState.StartAsync</c> throws if one is already running), so there is no
    /// concurrent-run race to worry about. Defaults to true (limited).</summary>
    public static bool WorkerLimitEnabled { get; set; } = true;

    /// <summary>Worker count used in place of a caller's <c>NumSearchWorkers: 0</c> ("auto") while
    /// <see cref="WorkerLimitEnabled"/> is true. Half the logical processors (floor 2, matching the
    /// smallest count actually measured as safe - see <see cref="CpSatSolveOptions"/>) keeps CP-SAT's
    /// parallel search meaningfully faster than a single thread while leaving room for everything else
    /// on the machine during a long grinding run. Briefly lowered to a third of the processors alongside
    /// a hard OS-level CPU rate cap (ProcessResourceLimiter, a separate project this one does not
    /// reference), but that cap turned out to throttle the search even while the machine was otherwise
    /// idle, and a user reported Highest-quality runs no longer finishing even after 2 hours as a direct
    /// result. The OS-level cap was replaced with a process-priority reduction (which only yields CPU
    /// time under real contention), so this worker count was restored to half the processors - it alone
    /// is enough to keep CP-SAT from pinning every core, without also starving the search on an idle
    /// machine. When disabled, returns 0 so the caller's "auto" passes straight through to CP-SAT.</summary>
    public static int ResolvedAutoSearchWorkers => WorkerLimitEnabled ? Math.Max(2, Environment.ProcessorCount / 2) : 0;

    public Task<ScheduleSolution> SolveAsync(
        ScheduleProblem problem,
        TimeSpan maximumDuration,
        int randomSeed = 1,
        CancellationToken cancellationToken = default)
    {
        if (maximumDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        return SolveAsync(problem, new CpSatSolveOptions(maximumDuration, randomSeed), cancellationToken);
    }

    public Task<ScheduleSolution> SolveAsync(
        ScheduleProblem problem,
        CpSatSolveOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.MaximumDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        return Task.Run(() => Solve(problem, options, cancellationToken), cancellationToken);
    }

    private static ScheduleSolution Solve(ScheduleProblem problem, CpSatSolveOptions options, CancellationToken cancellationToken)
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

        // ユーザー要望「担当する生徒の人数（既定1対2）を1対3・1対4等へ変更できるようにしたい」への
        // 対応。checkpoint93までは容量・ウェイトとも常に「2」固定だった箇所を、
        // problem.Policy.MaxStudentsPerTeacher（既定2）へ一般化した。1対1必須の受講希望は容量を
        // 丸ごと占有する点は変わらない（ウェイトを固定の2からmaxCapacityへ変更しただけ）。
        var maxCapacity = problem.Policy.MaxStudentsPerTeacher;
        foreach (var group in variables.GroupBy(item => (item.candidate.TeacherId, item.candidate.OpenDateId, item.candidate.TimeSlotId)))
        {
            var weighted = group.Select(item => LinearExpr.Term(item.variable, item.candidate.OneToOneRequired ? maxCapacity : 1));
            var fixedLoad = problem.ExistingPlacements
                .Where(item => item.TeacherId == group.Key.TeacherId && item.OpenDateId == group.Key.OpenDateId && item.TimeSlotId == group.Key.TimeSlotId)
                .Sum(item => item.OneToOneRequired ? maxCapacity : 1);
            model.Add(LinearExpr.Sum(weighted) <= maxCapacity - fixedLoad);
        }

        // ユーザー要望⑥「同時に使える座席数を『N人までに設定する』、0で考慮しない」への対応。
        // 教室単位ではなく学校（プロジェクト）全体で、同じ日付・時間帯に授業を受けている生徒の
        // 合計人数（1対1必須かどうかに関わらず、生徒1名につき1席）を上限で縛るハード制約。
        if (problem.Policy.MaxConcurrentSeats > 0)
        {
            foreach (var group in variables.GroupBy(item => (item.candidate.OpenDateId, item.candidate.TimeSlotId)))
            {
                var fixedSeats = problem.ExistingPlacements
                    .Count(item => item.OpenDateId == group.Key.OpenDateId && item.TimeSlotId == group.Key.TimeSlotId);
                model.Add(LinearExpr.Sum(group.Select(item => item.variable)) <= problem.Policy.MaxConcurrentSeats - fixedSeats);
            }
        }

        var regularTeacherShortfallTerms = AddRegularTeacherMinimums(model, problem, variables).ToArray();
        AddStudentConsecutiveAndGapConstraints(model, problem, variables);
        ApplyHintAndNeighborhoodFreeze(model, variables, options);

        // Assignment count dominates every soft penalty, so a prettier timetable can never
        // replace an otherwise assignable lesson with an unassigned lesson. Day-spread ranks
        // above teacher-preference matching per the Python reference's lexicographic order
        // (v1.9.5 objectives.py), so it is weighted above the *100 preference term but stays
        // far below the 1,000,000-per-placement floor. The regular-teacher shortfall penalty
        // (see AddRegularTeacherMinimums) ranks between day-spread and the placement floor.
        // EvenSpacing/SubjectSpacing (see BuildEvenSpacingTerms/BuildSubjectSpacingTerms) refine
        // day-spread further - even interval, not just distinct-day count - and rank just below it.
        var objectiveTerms = variables.Select(item =>
            LinearExpr.Term(item.variable, 1_000_000L - (item.candidate.PreferencePenalty * 100L) + item.candidate.AvailabilityPreference))
            .Concat(BuildDayDispersionTerms(model, variables, problem.Policy.StudentAttendanceDaysPreference))
            .Concat(BuildEvenSpacingTerms(model, problem, variables))
            .Concat(BuildPairingSizeTerms(model, variables, problem.Policy.PairingSizePreference, problem.Policy.MaxStudentsPerTeacher))
            .Concat(BuildSubjectSpacingTerms(model, problem, variables))
            .Concat(BuildTeacherGapAvoidanceTerms(model, problem, variables))
            .Concat(BuildTeacherCountPerDayTerms(model, variables, problem.Policy.TeacherCountPerDayPreference))
            .Concat(BuildTeacherLoadBalanceTerms(model, problem, variables, problem.Policy.TeacherLoadBalancePreference))
            .Concat(BuildTimeOfDayTerms(variables, problem.Policy.TimeOfDayPreference))
            .Concat(regularTeacherShortfallTerms);
        model.Maximize(LinearExpr.Sum(objectiveTerms));

        var searchWorkers = options.NumSearchWorkers > 0 ? options.NumSearchWorkers : ResolvedAutoSearchWorkers;
        var parameters = $"max_time_in_seconds:{options.MaximumDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} random_seed:{options.RandomSeed} num_search_workers:{searchWorkers}";
        if (options.SearchBranching is not null) parameters += $" search_branching:{options.SearchBranching}";
        var solver = new CpSolver { StringParameters = parameters };
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

    private static void ApplyHintAndNeighborhoodFreeze(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        CpSatSolveOptions options)
    {
        if (options.Hint is null) return;
        var hintedKeys = options.Hint.Placements
            .Select(p => (p.RequestId, p.TeacherId, p.OpenDateId, p.TimeSlotId))
            .ToHashSet();
        foreach (var (candidate, variable) in variables)
        {
            var isHinted = hintedKeys.Contains((candidate.RequestId, candidate.TeacherId, candidate.OpenDateId, candidate.TimeSlotId));
            if (options.FreeRequestIds is not null && !options.FreeRequestIds.Contains(candidate.RequestId))
                model.Add(variable == (isHinted ? 1 : 0));
            else
                model.AddHint(variable, isHinted);
        }
    }

    private const long DayDispersionWeight = 10_000L;

    /// <summary>
    /// ③ユーザー要望「生徒の授業日をできるだけ減らす（同じ日にまとめる）／分散する／考慮しない」への
    /// 対応。checkpoint93までは常時ON（Spread相当、Python v1.9.5 objectives.py tier 3の
    /// "同一日への過度な集中を抑制"）だった機能をトグル化した。Spread: 使用日数が多いほど加点（元の
    /// 挙動そのまま）。Concentrate: 符号を反転し、使用日数が多いほど減点（できるだけ同じ日へ集約）。
    /// None（既定）: このstudent×dayごとの項自体を一切生成しない。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildDayDispersionTerms(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        StudentAttendanceDaysPreference preference)
    {
        if (preference == StudentAttendanceDaysPreference.None) yield break;
        var sign = preference == StudentAttendanceDaysPreference.Concentrate ? -1L : 1L;
        var index = 0;
        foreach (var group in variables.GroupBy(item => (item.candidate.StudentId, item.candidate.OpenDateId)))
        {
            var dayVariables = group.Select(item => item.variable).ToArray();
            if (dayVariables.Length <= 1)
            {
                yield return LinearExpr.Term(dayVariables[0], sign * DayDispersionWeight);
                continue;
            }
            var dayUsed = model.NewBoolVar($"day_used_{index++}");
            model.AddMaxEquality(dayUsed, dayVariables);
            yield return LinearExpr.Term(dayUsed, sign * DayDispersionWeight);
        }
    }

    private const long EvenSpacingWeight = 4_000L;
    private const long SubjectSpacingWeight = 2_000L;

    /// <summary>
    /// ユーザー要望「授業と授業の間隔が（休みの日を除いて）常に同じ程度であると良い。授業日が25回
    /// あったら大体3日空けくらいに」への対応。開講日数を生徒の合計授業回数で割った「理想的な間隔
    /// （日数）」をwindow幅とし、そのwindow幅でスライドさせた各区間内の「利用日数」が1からどれだけ
    /// 乖離しているか（2以上＝密集、0＝間隔が開きすぎ）を目的関数で減点する。真の分散最小化ではなく、
    /// AddStudentConsecutiveAndGapConstraintsと同じ「window幅でスライドさせた合計」という手法を
    /// ハード制約ではなくソフトな目的関数へ応用した近似（CP-SATは選ばれる日付の集合を事前に知らない
    /// ため、正確な「隣接する利用日同士の間隔」を線形モデルで直接表現できない）。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildEvenSpacingTerms(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        var dayOrder = BuildOpenDayOrder(problem);
        if (dayOrder.Count < 2) yield break;

        var totalSessionsByStudent = problem.Demands
            .GroupBy(demand => demand.StudentId)
            .ToDictionary(group => group.Key, group => group.Sum(demand => demand.RequiredSessions));
        var dayUsedByStudentDay = BuildDayUsedVariables(model, variables, item => item.candidate.StudentId, "even_spacing_day");
        var fixedDaysByStudent = problem.ExistingPlacements
            .GroupBy(item => item.StudentId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.OpenDateId).ToHashSet());

        foreach (var term in BuildSpacingTerms(model, dayOrder, totalSessionsByStudent, dayUsedByStudentDay, fixedDaysByStudent, EvenSpacingWeight, "even_spacing"))
            yield return term;
    }

    /// <summary>
    /// ユーザー要望「同じ科目が固まらないようにしてほしい（数数数数英英英英ではなく数英数英…に近い
    /// 方が良い）」への対応。受講希望（＝生徒1名につき科目1つと1:1対応）単位で、その科目自身の
    /// セッションだけを対象に上と同じ間隔均等化を適用する。各科目が個別に間隔を空けて配置されれば、
    /// 結果として同じ科目が連続しにくくなる（完全な交互配置を保証するものではないが、同一科目の
    /// 密集は強く抑制される）。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildSubjectSpacingTerms(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        var dayOrder = BuildOpenDayOrder(problem);
        if (dayOrder.Count < 2) yield break;

        var totalSessionsByRequest = problem.Demands
            .Where(demand => demand.RequiredSessions >= 2)
            .ToDictionary(demand => demand.RequestId, demand => demand.RequiredSessions);
        if (totalSessionsByRequest.Count == 0) yield break;

        var dayUsedByRequestDay = BuildDayUsedVariables(model, variables, item => item.candidate.RequestId, "subject_spacing_day");
        var fixedDaysByRequest = problem.ExistingPlacements
            .GroupBy(item => item.RequestId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.OpenDateId).ToHashSet());

        foreach (var term in BuildSpacingTerms(model, dayOrder, totalSessionsByRequest, dayUsedByRequestDay, fixedDaysByRequest, SubjectSpacingWeight, "subject_spacing"))
            yield return term;
    }

    private const long PairingSizeWeight = 3_000L;

    /// <summary>
    /// ④ユーザー要望「1コマあたりの生徒の対応人数をできるだけ多くする／少なくする／考慮しない」への
    /// 対応。checkpoint93までは常時ON・容量2固定（「1対1が多いように見える。絶対ダメではないが
    /// 1対2の方がいい」というユーザー要望への対応、checkpoint87）だった機能を、容量
    /// problem.Policy.MaxStudentsPerTeacherに応じて一般化し、トグル化した。
    ///
    /// 各（講師・日付・コマ）の組について、実際に埋まっている人数がしきい値k（2〜maxCapacity）以上
    /// なら加点/減点するbool変数を、しきい値ごとに1つずつ用意する（`k*atLeastK <= 実際に埋まっている
    /// 人数`という片方向の緩和 - atLeastKを0にすることは常に許されるが、1にできるのは人数がk以上の
    /// 場合だけ。目的関数の最大化/最小化の性質上、ソルバーは条件を満たす限りatLeastK=1を選ぶ）。
    /// しきい値を積み上げることで、実質的に「1名を超えて何名埋まっているか」に比例する加点/減点になる
    /// （maxCapacity=2なら旧実装のpairedと完全に同じ、しきい値はk=2の1個だけ）。Maximize:
    /// 正の重み（多く埋めるほど加点）。Minimize: 負の重み（1対1に近いほど有利、詰め込むほど減点）。
    /// None（既定）: このグループの項自体を一切生成しない。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildPairingSizeTerms(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        PairingSizePreference preference,
        int maxStudentsPerTeacher)
    {
        if (preference == PairingSizePreference.None || maxStudentsPerTeacher < 2) yield break;
        var sign = preference == PairingSizePreference.Minimize ? -1L : 1L;
        var index = 0;
        foreach (var group in variables.GroupBy(item => (item.candidate.TeacherId, item.candidate.OpenDateId, item.candidate.TimeSlotId)))
        {
            var pairable = group.Where(item => !item.candidate.OneToOneRequired).Select(item => item.variable).ToArray();
            if (pairable.Length < 2) continue;
            var filled = LinearExpr.Sum(pairable);
            var cap = Math.Min(maxStudentsPerTeacher, pairable.Length);
            for (var threshold = 2; threshold <= cap; threshold++)
            {
                var atLeast = model.NewBoolVar($"pairing_at_least_{threshold}_{index++}");
                model.Add(LinearExpr.Term(atLeast, threshold) <= filled);
                yield return LinearExpr.Term(atLeast, sign * PairingSizeWeight);
            }
        }
    }

    private const long TeacherGapWeight = 2_500L;

    /// <summary>
    /// ユーザー要望「講師の空きコマも基本作らないでください」への対応。生徒側の
    /// AddStudentConsecutiveAndGapConstraints（AllowGap=falseなら常にハード制約）と同じ「前後のコマは
    /// 埋まっているのに真ん中だけ空いている」判定を講師側にも適用するが、講師の出勤不可
    /// （TeacherUnavailability）等によって穴が構造的に避けられない場合にInfeasibleへ追い込むと
    /// 元も子もないため、ハードではなくソフトなペナルティとする（「基本」という言葉通り、絶対の
    /// 禁止ではなく強い推奨として扱う）。1講師が同時に生徒2名まで担当できる（容量2）ため、
    /// 前後のコマの占有人数が最大2になり得る点を考慮し、スラック変数の上限は0〜3とする。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildTeacherGapAvoidanceTerms(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables)
    {
        var teacherDays = variables.Select(item => (item.candidate.TeacherId, item.candidate.OpenDateId))
            .Concat(problem.ExistingPlacements.Select(item => (item.TeacherId, item.OpenDateId)))
            .Distinct();
        var index = 0;
        foreach (var (teacherId, openDateId) in teacherDays)
        {
            var teacherDay = variables.Where(item => item.candidate.TeacherId == teacherId && item.candidate.OpenDateId == openDateId).ToArray();
            var fixedForDay = problem.ExistingPlacements.Where(item => item.TeacherId == teacherId && item.OpenDateId == openDateId).ToArray();
            var slotOrders = problem.AvailableSlots.Where(slot => slot.OpenDateId == openDateId).Select(slot => slot.SlotOrder).Distinct().Order().ToArray();
            var occupancy = slotOrders.Select(slotOrder => new
            {
                Variables = teacherDay.Where(item => item.candidate.SlotOrder == slotOrder).Select(item => item.variable).ToArray(),
                Fixed = fixedForDay.Count(item => item.SlotOrder == slotOrder),
            }).ToArray();

            for (var first = 0; first < occupancy.Length; first++)
            for (var last = first + 2; last < occupancy.Length; last++)
            for (var middle = first + 1; middle < last; middle++)
            {
                var firstExpr = LinearExpr.Sum(occupancy[first].Variables) + occupancy[first].Fixed;
                var middleExpr = LinearExpr.Sum(occupancy[middle].Variables) + occupancy[middle].Fixed;
                var lastExpr = LinearExpr.Sum(occupancy[last].Variables) + occupancy[last].Fixed;
                var slack = model.NewIntVar(0, 3, $"teacher_gap_slack_{index++}");
                model.Add(firstExpr + lastExpr - middleExpr <= 1 + slack);
                yield return LinearExpr.Term(slack, -TeacherGapWeight);
            }
        }
    }

    private const long TeacherCountPerDayWeight = 1_500L;

    /// <summary>
    /// ①ユーザー要望「一日当たりの講師人数をできるだけ少なくする／多くする／考慮しない」への対応。
    /// checkpoint93までは常時ON・Minimize固定（「1日当たりのコマ数も多い方がいい。Aタイムのためだけに
    /// 出勤させるのは申し訳ない」というユーザー要望への対応、checkpoint87の
    /// TeacherDayConcentrationTerms）だった機能をトグル化した。(講師,日付)の組ごとに「その講師がその日
    /// 出勤するか」を表すboolを1つ用意し、全組について合計すると「日ごとの延べ講師人数」の合計に一致
    /// する（＝この合計を最小化/最大化することは、各日の講師人数を平均的に少なく/多くすることと同義）。
    /// Minimize: 減点（出勤する講師×日の組み合わせが少ないほど有利＝同じ講師へ集約）。Maximize: 加点
    /// （多くの講師に分散するほど有利）。None（既定）: このteacher×dayごとの項自体を一切生成しない。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildTeacherCountPerDayTerms(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        TeacherCountPerDayPreference preference)
    {
        if (preference == TeacherCountPerDayPreference.None) yield break;
        var sign = preference == TeacherCountPerDayPreference.Minimize ? -1L : 1L;
        var index = 0;
        foreach (var group in variables.GroupBy(item => (item.candidate.TeacherId, item.candidate.OpenDateId)))
        {
            var dayVariables = group.Select(item => item.variable).ToArray();
            if (dayVariables.Length == 1)
            {
                yield return LinearExpr.Term(dayVariables[0], sign * TeacherCountPerDayWeight);
                continue;
            }
            var dayUsed = model.NewBoolVar($"teacher_day_used_{index++}");
            model.AddMaxEquality(dayUsed, dayVariables);
            yield return LinearExpr.Term(dayUsed, sign * TeacherCountPerDayWeight);
        }
    }

    private const long TeacherLoadBalanceWeight = 1_000L;

    /// <summary>
    /// ②ユーザー要望「講師ごとのコマ数の偏りを均等にする／考慮しない」への対応（新規）。真の分散最小化
    /// はCP-SATの線形モデルで直接表現できないため、「講師の総コマ数の最大値をできるだけ小さくする」
    /// という標準的なmin-max近似を使う（全講師の総コマ数がこの上限以下になるよう毎回押し下げられる
    /// ため、結果的に最も負荷が高い講師を減らす方向＝平準化する方向へ働く）。None（既定）:
    /// 項を一切生成しない。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildTeacherLoadBalanceTerms(
        CpModel model,
        ScheduleProblem problem,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        TeacherLoadBalancePreference preference)
    {
        if (preference == TeacherLoadBalancePreference.None) yield break;
        var teacherIds = variables.Select(item => item.candidate.TeacherId).Distinct().ToArray();
        if (teacherIds.Length < 2) yield break;

        var maxLoad = model.NewIntVar(0, variables.Count, "teacher_load_max");
        foreach (var teacherId in teacherIds)
        {
            var teacherVariables = variables.Where(item => item.candidate.TeacherId == teacherId).Select(item => item.variable).ToArray();
            var fixedLoad = problem.ExistingPlacements.Count(item => item.TeacherId == teacherId);
            model.Add(maxLoad >= LinearExpr.Sum(teacherVariables) + fixedLoad);
        }
        yield return LinearExpr.Term(maxLoad, -TeacherLoadBalanceWeight);
    }

    private const long TimeOfDayWeight = 200L;

    /// <summary>
    /// ⑤ユーザー要望「時間帯をできるだけ遅くする／早くする／考慮しない」への対応（新規）。
    /// PlacementCandidate.SlotOrder（1日の中での時限順）に比例した加点/減点を各候補へ直接付与する
    /// （補助変数不要）。Late: SlotOrderが大きいほど加点。Early: 小さいほど有利（＝SlotOrderが大きい
    /// ほど減点）。None（既定）: 項を一切生成しない。
    /// </summary>
    private static IEnumerable<LinearExpr> BuildTimeOfDayTerms(
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        TimeOfDayPreference preference)
    {
        if (preference == TimeOfDayPreference.None) yield break;
        var sign = preference == TimeOfDayPreference.Early ? -1L : 1L;
        foreach (var (candidate, variable) in variables)
            yield return LinearExpr.Term(variable, sign * TimeOfDayWeight * candidate.SlotOrder);
    }

    /// <summary>Open (school) dates only, in calendar order - closed days never appear here, so every
    /// gap computed from these ranks is automatically "excluding holidays" per the user's request.
    /// Internal (not private) so ScheduleEvaluationCalculator can compute the same day-index space
    /// when scoring a completed solution's spacing quality.</summary>
    internal static IReadOnlyList<long> BuildOpenDayOrder(ScheduleProblem problem) =>
        problem.AvailableSlots
            .Select(slot => (slot.OpenDateId, slot.DayOrdinal))
            .Distinct()
            .OrderBy(item => item.DayOrdinal)
            .Select(item => item.OpenDateId)
            .ToArray();

    private static Dictionary<(long EntityId, long OpenDateId), BoolVar> BuildDayUsedVariables(
        CpModel model,
        IReadOnlyList<(PlacementCandidate candidate, BoolVar variable)> variables,
        Func<(PlacementCandidate candidate, BoolVar variable), long> entitySelector,
        string namePrefix)
    {
        var result = new Dictionary<(long, long), BoolVar>();
        var index = 0;
        foreach (var group in variables.GroupBy(item => (EntityId: entitySelector(item), item.candidate.OpenDateId)))
        {
            var dayVariables = group.Select(item => item.variable).ToArray();
            if (dayVariables.Length == 1)
            {
                result[group.Key] = dayVariables[0];
                continue;
            }
            var dayUsed = model.NewBoolVar($"{namePrefix}_{index++}");
            model.AddMaxEquality(dayUsed, dayVariables);
            result[group.Key] = dayUsed;
        }
        return result;
    }

    /// <summary>Shared window-deviation penalty builder used by both BuildEvenSpacingTerms (per
    /// student, across all subjects) and BuildSubjectSpacingTerms (per lesson request, i.e. per
    /// student+subject). For every entity with >=2 total sessions, slides a window sized to that
    /// entity's own "ideal gap" (openDays / sessions) across the whole open-day range and penalizes
    /// any window containing 2+ used days (clustering) or 0 used days (an overly large gap) - windows
    /// with nothing to optimize (no free variable inside them) are skipped entirely.</summary>
    private static IEnumerable<LinearExpr> BuildSpacingTerms(
        CpModel model,
        IReadOnlyList<long> dayOrder,
        IReadOnlyDictionary<long, int> totalSessionsByEntity,
        IReadOnlyDictionary<(long EntityId, long OpenDateId), BoolVar> dayUsedByEntityDay,
        IReadOnlyDictionary<long, HashSet<long>> fixedDaysByEntity,
        long weight,
        string namePrefix)
    {
        var totalOpenDays = dayOrder.Count;
        var index = 0;
        foreach (var (entityId, totalSessions) in totalSessionsByEntity)
        {
            if (totalSessions < 2) continue;
            var window = (int)Math.Round((double)totalOpenDays / totalSessions);
            if (window < 2) continue;

            var fixedDays = fixedDaysByEntity.GetValueOrDefault(entityId);
            for (var start = 0; start + window <= totalOpenDays; start++)
            {
                var termsInWindow = new List<BoolVar>();
                var fixedCountInWindow = 0;
                for (var offset = 0; offset < window; offset++)
                {
                    var openDateId = dayOrder[start + offset];
                    if (dayUsedByEntityDay.TryGetValue((entityId, openDateId), out var v)) termsInWindow.Add(v);
                    if (fixedDays is not null && fixedDays.Contains(openDateId)) fixedCountInWindow++;
                }
                if (termsInWindow.Count == 0) continue;
                var countExpr = LinearExpr.Sum(termsInWindow) + fixedCountInWindow;
                var maxPossible = termsInWindow.Count + fixedCountInWindow;

                if (maxPossible >= 2)
                {
                    var over = model.NewIntVar(0, maxPossible - 1, $"{namePrefix}_over_{index}");
                    model.Add(countExpr - 1 <= over);
                    yield return LinearExpr.Term(over, -weight);
                }
                if (fixedCountInWindow == 0)
                {
                    var under = model.NewIntVar(0, 1, $"{namePrefix}_under_{index}");
                    model.Add(1 - countExpr <= under);
                    yield return LinearExpr.Term(under, -weight);
                }
                index++;
            }
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

    // ユーザー指定の目安: 優先度が1下がるごとに、通常担当講師が入る割合の最低保証値が30ポイントずつ
    // 下がる（5→100%・4→70%・3→40%・2→10%・1→保証なし）。優先度5については、この最低割合の
    // 目標に加えて、BuildProblemAsync（SqliteScheduleRunService）側で「通常担当講師の出勤可能コマ数が
    // 必要回数以上ある場合に限り、候補を通常担当・第1〜第3希望講師だけへ絞り込む」というハード制約も
    // 別途課している。この関数はその制約が使えない場合（出勤可能コマ数が不足）や優先度2〜4のための、
    // ソフトな最低保証（達成できないと目的関数が減点されるだけで、Infeasibleにはしない）を計算する。
    internal static int MinimumRegularTeacherSessions(int requiredSessions, int priority)
    {
        if (requiredSessions < 0) throw new ArgumentOutOfRangeException(nameof(requiredSessions));
        if (priority is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(priority));
        var targetPercentage = Math.Max(0, 100 - (5 - priority) * 30);
        return (requiredSessions * targetPercentage + 99) / 100;
    }
}
