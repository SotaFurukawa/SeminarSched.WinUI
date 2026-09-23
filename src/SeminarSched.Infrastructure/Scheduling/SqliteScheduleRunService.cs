using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
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
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        var problem = await BuildProblemAsync(connection, cancellationToken).ConfigureAwait(false);

        var optimizer = new ScheduleOptimizer<ScheduleProblem, ScheduleSolution>(CreateStrategies());
        var result = await optimizer.RunAsync(problem, profile, control, progress, cancellationToken).ConfigureAwait(false);
        if (result.Best is null)
            throw new InvalidOperationException("時間割を作成できませんでした: すべての戦略で解が得られませんでした。");

        var solution = result.Best.Solution;
        ScheduleSolutionValidator.Validate(problem, solution);
        await SaveValidatedAsync(connection, problem, solution, profile.MaximumDuration, cancellationToken).ConfigureAwait(false);
        return new ScheduleRunSummary(solution.Placements.Count, solution.UnassignedLessons, result.Elapsed, result.Best.Strategy.ToString(), result.WasExtended);
    }

    private static IEnumerable<IScheduleStrategy<ScheduleProblem, ScheduleSolution>> CreateStrategies() =>
    [
        new StandardCpSatStrategy(), new SeededCpSatAStrategy(), new SeededCpSatBStrategy(), new SeededCpSatCStrategy(),
        new AlternateDecisionStrategy(), new MultiStageStrategy(), new HintImprovementStrategy(),
        new NeighborhoodRepairStrategy(), new NeighborhoodRepairBStrategy(), new NeighborhoodRepairCStrategy(),
        new NeighborhoodRepairDStrategy(), new NeighborhoodRepairEStrategy(), new GrindingNeighborhoodRepairStrategy(),
        new FinalPolishingStrategy(), new FinalPolishingBStrategy(), new GrindingFinalPolishingStrategy(),
    ];

    private static async Task<ScheduleProblem> BuildProblemAsync(SqliteConnection connection, CancellationToken cancellationToken)
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
        var restrictedCandidates = RestrictPriorityFiveCandidatesToPreferredTeachers(candidates, demands, metadata);
        return new ScheduleProblem(demands, restrictedCandidates, slots, fixedPlacements);
    }

    // ユーザー指示: 担当講師優先度5は通常担当講師に限る（他の講師が候補として残らないようにする）。
    // ただし通常担当講師の出勤可能コマ数（＝この受講希望に対する候補コマ数）が必要回数に満たない
    // 場合は、全回数を通常担当講師だけで満たすこと自体が不可能なため、この絞り込みを適用しない
    // （元の全候補のまま残す）。第2希望・第3希望が設定されている場合はそれらも候補として残す
    // （通常担当＝第1希望が優先されるべきという前提は、PreferencePenaltyの得点差で維持される）。
    private static List<PlacementCandidate> RestrictPriorityFiveCandidatesToPreferredTeachers(
        List<PlacementCandidate> candidates,
        List<LessonDemand> demands,
        IReadOnlyDictionary<long, RequestMetadata> metadata)
    {
        var demandsById = demands.ToDictionary(demand => demand.RequestId);
        var restricted = new List<PlacementCandidate>(candidates.Count);
        foreach (var group in candidates.GroupBy(candidate => candidate.RequestId))
        {
            var demand = demandsById[group.Key];
            if (demand.RegularTeacherPriority == 5 && demand.RegularTeacherId is long regularTeacherId)
            {
                var remainingNeeded = Math.Max(0, demand.RequiredSessions - demand.AlreadyFixedSessions);
                var regularTeacherCandidateCount = group.Count(candidate => candidate.TeacherId == regularTeacherId);
                if (regularTeacherCandidateCount >= remainingNeeded)
                {
                    var meta = metadata[group.Key];
                    var allowedTeacherIds = new HashSet<long> { regularTeacherId };
                    if (meta.PreferredTeacher1Id is long preferred1) allowedTeacherIds.Add(preferred1);
                    if (meta.PreferredTeacher2Id is long preferred2) allowedTeacherIds.Add(preferred2);
                    if (meta.PreferredTeacher3Id is long preferred3) allowedTeacherIds.Add(preferred3);
                    restricted.AddRange(group.Where(candidate => allowedTeacherIds.Contains(candidate.TeacherId)));
                    continue;
                }
            }
            restricted.AddRange(group);
        }
        return restricted;
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
