using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Domain.Scheduling;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Optimization.Core;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteScheduleRunService : IScheduleRunService
{
    // 呼び出し元がTimeSpanだけを渡す旧来の1戦略呼び出し（既存テストが依存）。単一ステージ・
    // StandardCpSat 1本のプロファイルへ変換し、下のマルチ戦略経路をそのまま再利用する。
    public async Task<ScheduleRunSummary> RunAsync(string projectPath, TimeSpan maximumDuration, CancellationToken cancellationToken = default)
    {
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "single", "single", "single",
            maximumDuration, maximumDuration,
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1, [OptimizationStrategyKind.StandardCpSat])]);
        using var control = new OptimizationRunControl();
        return await RunAsync(projectPath, profile, control, progress: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScheduleRunSummary> RunAsync(
        string projectPath,
        OptimizationProfile profile,
        OptimizationRunControl control,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        SchedulingPolicy? policyOverride = null)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        var policy = policyOverride ?? await ReadSchedulingPolicyAsync(connection, cancellationToken).ConfigureAwait(false);
        var (problem, regularTeacherRestrictedRequestIds) = await BuildProblemAsync(connection, policy, cancellationToken).ConfigureAwait(false);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(CreateStrategies());
        var result = await optimizer.RunAsync(problem, profile, control, progress, cancellationToken, policy.ContinueBeyondNominalTimeIfIncomplete).ConfigureAwait(false);
        if (result.Best is null)
            throw new InvalidOperationException("時間割を作成できませんでした: すべての戦略で解が得られませんでした。");

        var solution = result.Best.Solution;
        ScheduleSolutionValidator.Validate(problem, solution);
        await SaveValidatedAsync(connection, problem, solution, profile.MaximumDuration, cancellationToken).ConfigureAwait(false);
        var (priorityFiveShortfall, noQualifiedTeacher) = DiagnoseUnassignedDemands(problem, solution, regularTeacherRestrictedRequestIds);
        return new ScheduleRunSummary(
            solution.Placements.Count, solution.UnassignedLessons, result.Elapsed, result.Best.Strategy.ToString(), result.WasExtended,
            priorityFiveShortfall, noQualifiedTeacher);
    }

    // ユーザー報告「配置できない原因がある場合はその警告を出す。例えば、優先度5になっていることで
    // ハード条件が加えられ、それにより実装できない場合はその旨を伝える」への対応。未配置のまま残った
    // 受講希望ごとに、原因をベストエフォートで分類する:
    // - 候補コマが1件も無い（講師の資格・出勤可否等の時点で構造的に配置不可能。時間をかけても解決しない）
    // - 担当講師優先度5により候補が通常担当講師（＋希望講師）へ絞り込まれ、かつ絞り込み後も余った
    //   （＝その講師の空きコマ不足が理由である可能性が高い）
    // どちらにも当てはまらない残りは、他の生徒・講師との競合または探索時間不足など、単一の原因に
    // 帰属させられないケースとして区別しない（UnassignedLessonsとの差分で分かる）。
    private static (int PriorityFiveShortfall, int NoQualifiedTeacher) DiagnoseUnassignedDemands(
        ScheduleProblem problem, ScheduleSolution solution, IReadOnlySet<long> regularTeacherRestrictedRequestIds)
    {
        var placedByRequest = solution.Placements.GroupBy(p => p.RequestId).ToDictionary(g => g.Key, g => g.Count());
        var candidateCountByRequest = problem.Candidates.GroupBy(c => c.RequestId).ToDictionary(g => g.Key, g => g.Count());
        var priorityFiveShortfall = 0;
        var noQualifiedTeacher = 0;
        foreach (var demand in problem.Demands)
        {
            var shortfall = Math.Max(0, demand.RequiredSessions - demand.AlreadyFixedSessions - placedByRequest.GetValueOrDefault(demand.RequestId));
            if (shortfall <= 0) continue;
            if (candidateCountByRequest.GetValueOrDefault(demand.RequestId) == 0) { noQualifiedTeacher++; continue; }
            if (regularTeacherRestrictedRequestIds.Contains(demand.RequestId)) priorityFiveShortfall++;
        }
        return (priorityFiveShortfall, noQualifiedTeacher);
    }

    private static IEnumerable<IScheduleStrategy<ScheduleProblem, ScheduleSolution>> CreateStrategies() =>
    [
        new StandardCpSatStrategy(), new SeededCpSatAStrategy(), new SeededCpSatBStrategy(), new SeededCpSatCStrategy(),
        new AlternateDecisionStrategy(), new MultiStageStrategy(), new HintImprovementStrategy(),
        new NeighborhoodRepairStrategy(), new NeighborhoodRepairBStrategy(), new NeighborhoodRepairCStrategy(),
        new NeighborhoodRepairDStrategy(), new NeighborhoodRepairEStrategy(), new GrindingNeighborhoodRepairStrategy(),
        new FinalPolishingStrategy(), new FinalPolishingBStrategy(), new GrindingFinalPolishingStrategy(),
    ];

    // SqliteSchedulingPolicyRepositoryと同じSELECTだが、既に開いているconnectionをそのまま使い回す
    // （RunAsyncはこの後BuildProblemAsyncで同じprojectへ何度もクエリを投げるため、ここだけ別途
    // 新しいconnectionを開き直すのは無駄。SetupPage側の読み書きはSqliteSchedulingPolicyRepository
    // 経由で行う）。
    private static async Task<SchedulingPolicy> ReadSchedulingPolicyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MaxStudentsPerTeacher,TeacherCountPerDayPreference,TeacherLoadBalancePreference,
                   StudentAttendanceDaysPreference,TeacherAttendanceDaysPreference,PairingSizePreference,
                   TimeOfDayPreference,TeacherStudentConsecutivePreference,MaxConcurrentSeats,ContinueBeyondNominalTimeIfIncomplete
            FROM SchedulingPolicy WHERE ProjectId=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return SchedulingPolicy.Default;

        return new SchedulingPolicy(
            reader.GetInt32(0),
            (TeacherCountPerDayPreference)reader.GetInt32(1),
            (TeacherLoadBalancePreference)reader.GetInt32(2),
            (StudentAttendanceDaysPreference)reader.GetInt32(3),
            (TeacherAttendanceDaysPreference)reader.GetInt32(4),
            (PairingSizePreference)reader.GetInt32(5),
            (TimeOfDayPreference)reader.GetInt32(6),
            (TeacherStudentConsecutivePreference)reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetBoolean(9));
    }

    private static async Task<(ScheduleProblem Problem, HashSet<long> RegularTeacherRestrictedRequestIds)> BuildProblemAsync(SqliteConnection connection, SchedulingPolicy policy, CancellationToken cancellationToken)
    {
        var demands = new List<LessonDemand>();
        var metadata = new Dictionary<long, RequestMetadata>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT r.Id,r.StudentId,r.RequiredSessions,
                  (SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id AND (a.IsLocked=1 OR a.IsManual=1)),
                  COALESCE(r.RegularTeacherId,p.RegularTeacherId),
                  COALESCE(NULLIF(r.RegularTeacherPriority,1),p.RegularTeacherPriority,1),
                  (SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id AND (a.IsLocked=1 OR a.IsManual=1) AND a.TeacherId=COALESCE(r.RegularTeacherId,p.RegularTeacherId)),
                  COALESCE(r.MaxConsecutiveSlotsOverride,s.DefaultMaxConsecutiveSlots),
                  COALESCE(r.AllowGapOverride,s.AllowGap),
                  CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END,
                  r.PreferredTeacher1Id,r.PreferredTeacher2Id,r.PreferredTeacher3Id,r.SubjectId
                FROM LessonRequest r
                JOIN Student s ON s.Id=r.StudentId AND s.Active=1
                JOIN Subject sub ON sub.Id=r.SubjectId AND sub.Active=1
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                WHERE r.ProjectId=1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var requestId = reader.GetInt64(0);
                long? regularTeacher = reader.IsDBNull(4) ? null : reader.GetInt64(4);
                var demand = new LessonDemand(
                    requestId,
                    reader.GetInt64(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    regularTeacher,
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetBoolean(8),
                    reader.GetBoolean(9));
                demands.Add(demand);
                metadata[requestId] = new RequestMetadata(
                    regularTeacher,
                    demand.RegularTeacherPriority,
                    reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    reader.IsDBNull(12) ? null : reader.GetInt64(12),
                    demand.OneToOneRequired);
            }
        }

        // ユーザー報告バグ修正: 生徒側は「アンケート回答行が1件も無い＝まだ取込みしていない」場合だけ
        // NOT EXISTS(...)で全コマ候補扱いへfallbackする（CourseSurveyImportServiceは回答があった生徒
        // にしかLessonRequest自体を作らないため、この分岐は実質到達しない安全弁）。一方、講師は
        // TeacherQualification経由で常にJOINへ乗るため、講師単位でNOT EXISTSをfallbackさせると、
        // アンケートに一度も回答していない講師（TeacherAvailability行が1件も無い）が「常に出勤可能」
        // として全コマの候補に混入してしまう。かといってfallback自体を完全に無くすと、今度は
        // プロジェクト全体でまだ一度も出勤可否を取込んでいない（TeacherAvailability行がプロジェクトに
        // 1件も無い）通常の初期状態で、全講師が候補から消えてしまう（試験用fixtureや、アンケート機能を
        // 使わない小規模運用が壊れる）。そこで判定の粒度を「講師単位」ではなく「プロジェクト単位」に
        // 変更した: プロジェクト内に出勤可否データが1件もまだ無ければ（＝アンケート未取込み状態）
        // 全コマ候補のまま、1件でもあれば（＝アンケートを取込み済み）、回答していない講師個別を
        // 対象外とする。
        var candidates = new List<PlacementCandidate>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT r.Id,r.StudentId,tq.TeacherId,ds.OpenDateId,ds.TimeSlotId,
                       CAST(julianday(d.Date)-julianday(cp.StartDate) AS INTEGER),ts.SortOrder,
                       COALESCE(sa.AvailabilityLevel,1),COALESCE(ta.AvailabilityLevel,1),
                       CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END
                FROM LessonRequest r
                JOIN CourseProject cp ON cp.Id=r.ProjectId
                JOIN TeacherQualification tq ON tq.SubjectId=r.SubjectId AND tq.CanTeach=1
                JOIN Teacher t ON t.Id=tq.TeacherId AND t.Active=1
                CROSS JOIN OpenDateTimeSlot ds
                JOIN OpenDate d ON d.Id=ds.OpenDateId AND d.IsOpen=1
                JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId AND ts.Active=1
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                LEFT JOIN StudentAvailability sa ON sa.ProjectId=r.ProjectId AND sa.StudentId=r.StudentId AND sa.OpenDateId=ds.OpenDateId AND sa.TimeSlotId=ds.TimeSlotId
                LEFT JOIN TeacherAvailability ta ON ta.ProjectId=r.ProjectId AND ta.TeacherId=tq.TeacherId AND ta.OpenDateId=ds.OpenDateId AND ta.TimeSlotId=ds.TimeSlotId
                WHERE (NOT EXISTS(SELECT 1 FROM StudentAvailability WHERE ProjectId=r.ProjectId AND StudentId=r.StudentId) OR COALESCE(sa.AvailabilityLevel,0)>0)
                  AND (NOT EXISTS(SELECT 1 FROM TeacherAvailability WHERE ProjectId=r.ProjectId) OR COALESCE(ta.AvailabilityLevel,0)>0)
                  AND NOT EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=tq.TeacherId AND u.OpenDateId=ds.OpenDateId AND u.TimeSlotId=ds.TimeSlotId)
                  AND NOT EXISTS(SELECT 1 FROM Assignment a JOIN LessonRequest ar ON ar.Id=a.LessonRequestId WHERE (a.IsLocked=1 OR a.IsManual=1) AND a.OpenDateId=ds.OpenDateId AND a.TimeSlotId=ds.TimeSlotId AND ar.StudentId=r.StudentId);
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var requestId = reader.GetInt64(0);
                var teacherId = reader.GetInt64(2);
                var request = metadata[requestId];
                candidates.Add(new PlacementCandidate(
                    requestId,
                    reader.GetInt64(1),
                    teacherId,
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    PreferencePenalty(request, teacherId),
                    Math.Min(reader.GetInt32(7), reader.GetInt32(8)),
                    reader.GetBoolean(9)));
            }
        }

        var slots = new List<ScheduleSlot>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ds.OpenDateId,ds.TimeSlotId,CAST(julianday(d.Date)-julianday(cp.StartDate) AS INTEGER),ts.SortOrder
                FROM OpenDateTimeSlot ds
                JOIN OpenDate d ON d.Id=ds.OpenDateId AND d.IsOpen=1
                JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId AND ts.Active=1
                JOIN CourseProject cp ON cp.Id=d.ProjectId
                ORDER BY d.Date,ts.SortOrder;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                slots.Add(new ScheduleSlot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        var fixedPlacements = new List<FixedPlacement>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.LessonRequestId,r.StudentId,a.TeacherId,a.OpenDateId,a.TimeSlotId,
                       CAST(julianday(d.Date)-julianday(cp.StartDate) AS INTEGER),ts.SortOrder,
                       CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END
                FROM Assignment a
                JOIN LessonRequest r ON r.Id=a.LessonRequestId
                JOIN CourseProject cp ON cp.Id=r.ProjectId
                JOIN OpenDate d ON d.Id=a.OpenDateId
                JOIN TimeSlot ts ON ts.Id=a.TimeSlotId
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                WHERE a.IsLocked=1 OR a.IsManual=1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                fixedPlacements.Add(new FixedPlacement(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetBoolean(7)));
        }
        var (restrictedCandidates, regularTeacherRestrictedRequestIds) = RestrictPriorityFiveCandidatesToPreferredTeachers(candidates, demands, metadata);
        return (new ScheduleProblem(demands, restrictedCandidates, slots, fixedPlacements, policy), regularTeacherRestrictedRequestIds);
    }

    // ユーザー指示（checkpoint102）「優先度5について、これは必須です。通常授業講師と第一から第三
    // 希望講師以外からは絶対に選ばないようにしてください」への対応。担当講師優先度5は、通常担当講師
    // ＋第1〜3希望講師の合計最大4名に候補を無条件で限定する（この4名の空きコマだけでは必要回数を
    // 満たせない場合でも、他の講師を候補に戻すことは絶対にしない。満たせない分はそのまま未配置として
    // 残す）。以前は「通常担当講師の空きコマ数が必要回数に満たない場合はこの絞り込み自体を適用しない
    // （元の全候補へ戻す）」という救済処理があったが、ユーザーが明示的にこれを禁止したため削除した。
    // 戻り値のRestrictedRequestIdsは、この絞り込みが適用された受講希望のID集合（優先度5＋通常担当
    // 講師が設定されている受講希望は常にここへ含まれる。DiagnoseUnassignedDemandsが、未配置のまま
    // 残った理由を「優先度5の講師の空き不足」と説明してよいかどうかの判定に使う）。
    private static (List<PlacementCandidate> Candidates, HashSet<long> RestrictedRequestIds) RestrictPriorityFiveCandidatesToPreferredTeachers(
        List<PlacementCandidate> candidates,
        List<LessonDemand> demands,
        IReadOnlyDictionary<long, RequestMetadata> metadata)
    {
        var demandsById = demands.ToDictionary(demand => demand.RequestId);
        var restricted = new List<PlacementCandidate>(candidates.Count);
        var restrictedRequestIds = new HashSet<long>();
        foreach (var group in candidates.GroupBy(candidate => candidate.RequestId))
        {
            var demand = demandsById[group.Key];
            if (demand.RegularTeacherPriority == 5 && demand.RegularTeacherId is long regularTeacherId)
            {
                var meta = metadata[group.Key];
                var allowedTeacherIds = new HashSet<long> { regularTeacherId };
                if (meta.PreferredTeacher1Id is long preferred1) allowedTeacherIds.Add(preferred1);
                if (meta.PreferredTeacher2Id is long preferred2) allowedTeacherIds.Add(preferred2);
                if (meta.PreferredTeacher3Id is long preferred3) allowedTeacherIds.Add(preferred3);
                restricted.AddRange(group.Where(candidate => allowedTeacherIds.Contains(candidate.TeacherId)));
                restrictedRequestIds.Add(group.Key);
                continue;
            }
            restricted.AddRange(group);
        }
        return (restricted, restrictedRequestIds);
    }

    private static int PreferencePenalty(RequestMetadata request, long teacherId)
    {
        var scores = new Dictionary<long, int>();
        if (request.RegularTeacherId is long regular) scores[regular] = (request.RegularTeacherPriority - 1) * 2 + 2;
        KeepMaximum(scores, request.PreferredTeacher1Id, 6);
        KeepMaximum(scores, request.PreferredTeacher2Id, 4);
        KeepMaximum(scores, request.PreferredTeacher3Id, 2);
        return scores.Count == 0 ? 0 : scores.Values.Max() - scores.GetValueOrDefault(teacherId);
    }

    private static void KeepMaximum(Dictionary<long, int> scores, long? teacherId, int score)
    {
        if (teacherId is not long id) return;
        scores[id] = Math.Max(scores.GetValueOrDefault(id), score);
    }

    private static async Task SaveValidatedAsync(SqliteConnection connection, ScheduleProblem problem, ScheduleSolution solution, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var started = DateTimeOffset.UtcNow - solution.Elapsed;
        await using var run = connection.CreateCommand();
        run.Transaction = (SqliteTransaction)transaction;
        run.CommandText = """
            INSERT INTO OptimizationRun(ProjectId,StartedUtc,FinishedUtc,Status,SolverStatus,QualityLevel,TimeLimitSeconds,ObjectiveSummaryJson,UnassignedCount,WarningCount,InputSnapshotJson,ResultSnapshotJson,RandomSeed,ElapsedSeconds)
            VALUES(1,$started,$finished,'completed','FEASIBLE',3,$limit,$objective,$unassigned,0,$input,$result,1,$elapsed);
            SELECT last_insert_rowid();
            """;
        run.Parameters.AddWithValue("$started", started.ToString("O", CultureInfo.InvariantCulture));
        run.Parameters.AddWithValue("$finished", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        run.Parameters.AddWithValue("$limit", Math.Max(1, (int)Math.Ceiling(timeLimit.TotalSeconds)));
        run.Parameters.AddWithValue("$objective", JsonSerializer.Serialize(new { solution.ObjectiveValue, solution.UnassignedLessons }));
        run.Parameters.AddWithValue("$unassigned", solution.UnassignedLessons);
        run.Parameters.AddWithValue("$input", JsonSerializer.Serialize(new { DemandCount = problem.Demands.Count, CandidateCount = problem.Candidates.Count }));
        run.Parameters.AddWithValue("$result", JsonSerializer.Serialize(new { PlacementCount = solution.Placements.Count }));
        run.Parameters.AddWithValue("$elapsed", solution.Elapsed.TotalSeconds);
        var runId = Convert.ToInt64(await run.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM Assignment WHERE IsLocked=0 AND IsManual=0;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var requestGroup in solution.Placements.GroupBy(placement => placement.RequestId))
        {
            var sessionIndex = problem.Demands.Single(demand => demand.RequestId == requestGroup.Key).AlreadyFixedSessions + 1;
            foreach (var placement in requestGroup.OrderBy(item => item.OpenDateId).ThenBy(item => item.TimeSlotId))
            {
                await using var add = connection.CreateCommand();
                add.Transaction = (SqliteTransaction)transaction;
                add.CommandText = "INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,OptimizationRunId,IsManual) VALUES($request,$teacher,$date,$slot,0,'cp-sat',$session,$run,0);";
                add.Parameters.AddWithValue("$request", placement.RequestId); add.Parameters.AddWithValue("$teacher", placement.TeacherId);
                add.Parameters.AddWithValue("$date", placement.OpenDateId); add.Parameters.AddWithValue("$slot", placement.TimeSlotId);
                add.Parameters.AddWithValue("$session", sessionIndex++); add.Parameters.AddWithValue("$run", runId);
                await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        await ValidateDatabaseAsync(connection, (SqliteTransaction)transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateDatabaseAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var validate = connection.CreateCommand();
        validate.Transaction = transaction;
        validate.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM (SELECT r.StudentId,a.OpenDateId,a.TimeSlotId FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId GROUP BY r.StudentId,a.OpenDateId,a.TimeSlotId HAVING COUNT(*)>1))
            + (SELECT COUNT(*) FROM (SELECT a.TeacherId,a.OpenDateId,a.TimeSlotId,SUM(CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 2 ELSE 1 END) load FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId GROUP BY a.TeacherId,a.OpenDateId,a.TimeSlotId HAVING load>2))
            + (SELECT COUNT(*) FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId
               WHERE NOT EXISTS(SELECT 1 FROM OpenDateTimeSlot ds JOIN OpenDate d ON d.Id=ds.OpenDateId WHERE ds.OpenDateId=a.OpenDateId AND ds.TimeSlotId=a.TimeSlotId AND d.IsOpen=1)
                  OR NOT EXISTS(SELECT 1 FROM TeacherQualification tq WHERE tq.TeacherId=a.TeacherId AND tq.SubjectId=r.SubjectId AND tq.CanTeach=1)
                  OR EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=a.TeacherId AND u.OpenDateId=a.OpenDateId AND u.TimeSlotId=a.TimeSlotId)
                  OR EXISTS(SELECT 1 FROM StudentAvailability sa WHERE sa.StudentId=r.StudentId AND sa.OpenDateId=a.OpenDateId AND sa.TimeSlotId=a.TimeSlotId AND sa.AvailabilityLevel=0)
                  OR EXISTS(SELECT 1 FROM TeacherAvailability ta WHERE ta.TeacherId=a.TeacherId AND ta.OpenDateId=a.OpenDateId AND ta.TimeSlotId=a.TimeSlotId AND ta.AvailabilityLevel=0))
            + (SELECT COUNT(*) FROM (SELECT r.Id FROM LessonRequest r LEFT JOIN Assignment a ON a.LessonRequestId=r.Id GROUP BY r.Id,r.RequiredSessions HAVING COUNT(a.Id)>r.RequiredSessions));
            """;
        var invalid = Convert.ToInt64(await validate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (invalid > 0) throw new InvalidDataException("保存前validatorが制約違反を検出しました。");
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private sealed record RequestMetadata(long? RegularTeacherId, int RegularTeacherPriority, long? PreferredTeacher1Id, long? PreferredTeacher2Id, long? PreferredTeacher3Id, bool OneToOneRequired);
}
