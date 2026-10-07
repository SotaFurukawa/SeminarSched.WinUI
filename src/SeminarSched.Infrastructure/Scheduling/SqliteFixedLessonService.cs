using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Scheduling;

public sealed class SqliteFixedLessonService : IFixedLessonService
{
    public async Task<IReadOnlyList<LessonRequestOption>> GetRequestsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.Id,r.StudentId,s.Name||' / '||sub.DisplayName FROM LessonRequest r JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId ORDER BY s.ExternalId,sub.SortOrder;";
        var result = new List<LessonRequestOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new LessonRequestOption(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
        return result;
    }

    public async Task<IReadOnlyList<TeacherOption>> GetTeachersAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name FROM Teacher WHERE Active=1 ORDER BY ExternalId;";
        var result = new List<TeacherOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new TeacherOption(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<IReadOnlyList<ScheduleSlotOption>> GetSlotsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT d.Id,s.Id,d.Date||' '||s.DisplayName||' ('||s.StartTime||'-'||s.EndTime||')' FROM OpenDate d JOIN OpenDateTimeSlot ds ON ds.OpenDateId=d.Id JOIN TimeSlot s ON s.Id=ds.TimeSlotId WHERE d.IsOpen=1 AND s.Active=1 ORDER BY d.Date,s.SortOrder;";
        var result = new List<ScheduleSlotOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ScheduleSlotOption(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
        return result;
    }

    internal async Task AddManualAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, bool confirmSoftWarnings = false, string? reason = null, CancellationToken cancellationToken = default)
        => await AddCoreAsync(projectPath, requestId, teacherId, openDateId, timeSlotId, isLocked, confirmSoftWarnings, reason, cancellationToken).ConfigureAwait(false);

    // Python版のpreview_edit相当をAddCoreAsyncにも適用したもの。ユーザー要望：手動配置で条件を
    // 満たさない場合に即ブロックせず、MoveAsyncと同じgreen/yellow/red判定＋確認ダイアログにしたい。
    // 受講希望・コマ自体が無効／必要回数を配置済み／生徒の二重配置だけは、物理的に成立しないためRED
    // のまま。指導可能科目・出勤/出席可否・講師の同時担当上限は、確認の上で手動配置を許可するYELLOWへ。
    internal async Task<EditPreview> PreviewAddAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        // プレビューは何も確定しない（トランザクションはコミットせず破棄=ロールバックする）。
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return await BuildAddPreviewAsync(connection, transaction, requestId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EditPreview> BuildAddPreviewAsync(SqliteConnection connection, SqliteTransaction transaction, long requestId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        RequestState request;
        try
        {
            request = await ReadRequestAsync(connection, transaction, requestId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
            await EnsureNoStudentCollisionAsync(connection, transaction, request.StudentId, openDateId, timeSlotId, null, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return new EditPreview(EditDecision.Red, ex.Message, Array.Empty<SoftMetricDelta>());
        }

        var warnings = new List<string>();
        var deltas = new List<SoftMetricDelta>();

        if (!await IsTeacherQualifiedAsync(connection, transaction, teacherId, request.SubjectId, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add("選択した講師はこの科目を担当可能に設定されていません。");
            deltas.Add(new SoftMetricDelta("qualification_override", "指導可能科目としての登録", HigherIsBetter: false, 0, 1));
        }
        var studentAvailable = await IsStudentAvailableAsync(connection, transaction, request.StudentId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        var teacherAvailable = await IsTeacherAvailableAsync(connection, transaction, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        if (!studentAvailable || !teacherAvailable)
        {
            warnings.Add(!studentAvailable && !teacherAvailable
                ? "生徒と講師の両方がこの日時に参加できない設定になっています。"
                : !studentAvailable
                    ? "生徒がアンケートで出席不可にしています。"
                    : "講師がこの日時に出勤できない設定になっています。");
            deltas.Add(new SoftMetricDelta("availability_override", "出勤・出席可否の設定", HigherIsBetter: false, 0, 1));
        }
        var addMaxCapacity = await ReadMaxStudentsPerTeacherAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (!await IsWithinTeacherCapacityAsync(connection, transaction, teacherId, openDateId, timeSlotId, request.OneToOneRequired ? addMaxCapacity : 1, addMaxCapacity, null, cancellationToken).ConfigureAwait(false))
        {
            warnings.Add($"この講師は同じ日時の担当人数上限（{addMaxCapacity}人）を超えます。");
            deltas.Add(new SoftMetricDelta("capacity_override", "講師の同時担当人数", HigherIsBetter: false, 0, 1));
        }

        if (warnings.Count == 0) return new EditPreview(EditDecision.Green, "配置可能です。", deltas);
        return new EditPreview(EditDecision.Yellow, string.Join(" ", warnings), deltas);
    }

    internal async Task<EditPreview> PreviewMoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        // プレビューは何も確定しない（トランザクションはコミットせず破棄=ロールバックする）。
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return await BuildMovePreviewAsync(connection, transaction, assignmentId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
    }

    internal async Task MoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, bool confirmSoftWarnings = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var preview = await BuildMovePreviewAsync(connection, transaction, assignmentId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        if (preview.Decision == EditDecision.Red) throw new InvalidOperationException(preview.Message);
        if (preview.Decision == EditDecision.Yellow && !confirmSoftWarnings) throw new SoftWarningConfirmationRequiredException(preview);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Assignment SET TeacherId=$teacher,OpenDateId=$date,TimeSlotId=$slot,IsManual=1 WHERE Id=$id;";
        update.Parameters.AddWithValue("$teacher", teacherId);
        update.Parameters.AddWithValue("$date", openDateId);
        update.Parameters.AddWithValue("$slot", timeSlotId);
        update.Parameters.AddWithValue("$id", assignmentId);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var auditReason = preview.Decision == EditDecision.Yellow ? (string.IsNullOrWhiteSpace(reason) ? "ソフト条件を確認して変更" : reason) : "時間割手動移動";
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,'manual_assignment_moved','assignment',$entity,$after,$reason,'manual',$operation);";
        audit.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$entity", assignmentId.ToString(CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new { teacherId, openDateId, timeSlotId }));
        audit.Parameters.AddWithValue("$reason", auditReason);
        audit.Parameters.AddWithValue("$operation", Guid.NewGuid().ToString("N"));
        await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Python版のpreview_edit相当。ハード制約（EnsureXxxAsync群）を先に検証しRED/message化し、
    // 満たす場合のみソフト指標（優先講師一致度・希望日時一致度・使用コマ数・1対2ペア数）の
    // before/afterを比較してYELLOW（悪化あり）/GREEN（悪化なし）を判定する。
    private static async Task<EditPreview> BuildMovePreviewAsync(SqliteConnection connection, SqliteTransaction transaction, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        AssignmentState current;
        bool qualified;
        try
        {
            current = await ReadAssignmentAsync(connection, transaction, assignmentId, cancellationToken).ConfigureAwait(false);
            if (current.IsLocked) throw new InvalidOperationException("ロック済みの配置は移動できません。先にロックを解除してください。");
            if (current.OpenDateId == openDateId && current.TimeSlotId == timeSlotId && current.TeacherId == teacherId)
                throw new InvalidOperationException("移動先が現在の配置と同じです。");
            await EnsureSlotOpenAsync(connection, transaction, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
            await EnsureNoStudentCollisionAsync(connection, transaction, current.StudentId, openDateId, timeSlotId, assignmentId, cancellationToken).ConfigureAwait(false);
            // Python版のqualification override特例：講師の指導可能科目チェックだけはここでハード拒否せず、
            // 他の全ハード制約を満たす場合に限りYELLOW（確認の上で許可）へ回す。
            qualified = await IsTeacherQualifiedAsync(connection, transaction, teacherId, current.SubjectId, cancellationToken).ConfigureAwait(false);
            await EnsureAvailabilityAsync(connection, transaction, current.StudentId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
            var moveMaxCapacity = await ReadMaxStudentsPerTeacherAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await EnsureTeacherCapacityAsync(connection, transaction, teacherId, openDateId, timeSlotId, current.OneToOneRequired ? moveMaxCapacity : 1, moveMaxCapacity, assignmentId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return new EditPreview(EditDecision.Red, ex.Message, Array.Empty<SoftMetricDelta>());
        }

        var deltas = (await ComputeSoftDeltasAsync(connection, transaction, assignmentId, current, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false)).ToList();
        if (!qualified)
        {
            deltas.Add(new SoftMetricDelta("qualification_override", "指導可能科目としての登録", HigherIsBetter: false, 0, 1));
            return new EditPreview(EditDecision.Yellow, "選択した講師の指導可能科目に含まれていません。確認後は手動配置できますが、自動最適化では候補にしません。", deltas);
        }
        var worsenedCount = deltas.Count(d => d.Worsened);
        return worsenedCount > 0
            ? new EditPreview(EditDecision.Yellow, $"配置は可能ですが、ソフト条件が{worsenedCount}項目悪化します。", deltas)
            : new EditPreview(EditDecision.Green, "配置可能です。", deltas);
    }

    private static async Task<bool> IsTeacherQualifiedAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long subjectId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId WHERE q.TeacherId=$teacher AND q.SubjectId=$subject AND q.CanTeach=1 AND t.Active=1);";
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$subject", subjectId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<IReadOnlyList<SoftMetricDelta>> ComputeSoftDeltasAsync(SqliteConnection connection, SqliteTransaction transaction, long assignmentId, AssignmentState current, long newTeacherId, long newOpenDateId, long newTimeSlotId, CancellationToken cancellationToken)
    {
        var profile = await ReadPreferenceProfileAsync(connection, transaction, current.LessonRequestId, cancellationToken).ConfigureAwait(false);
        var preferenceBefore = PreferencePenalty(profile, current.TeacherId);
        var preferenceAfter = PreferencePenalty(profile, newTeacherId);

        var availabilityBefore = await AvailabilityPreferenceAsync(connection, transaction, current.StudentId, current.TeacherId, current.OpenDateId, current.TimeSlotId, cancellationToken).ConfigureAwait(false);
        var availabilityAfter = await AvailabilityPreferenceAsync(connection, transaction, current.StudentId, newTeacherId, newOpenDateId, newTimeSlotId, cancellationToken).ConfigureAwait(false);

        var allSlots = await LoadAssignmentSlotsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var beforeTuples = allSlots.Select(a => (a.TeacherId, a.OpenDateId, a.TimeSlotId)).ToList();
        var afterTuples = allSlots.Select(a => a.Id == assignmentId ? (newTeacherId, newOpenDateId, newTimeSlotId) : (a.TeacherId, a.OpenDateId, a.TimeSlotId)).ToList();
        var activeBefore = beforeTuples.Distinct().Count();
        var activeAfter = afterTuples.Distinct().Count();
        // ユーザー要望「担当する生徒の人数（既定1対2）を1対3・1対4等へ変更できるようにしたい」への
        // 対応。以前は「ちょうど2名」だけをペア扱いしていたが、上限が3・4等に変更されている場合も
        // 「複数名まとめて配置されているコマ数」として意味を持たせるため「2名以上」に一般化した。
        var pairedBefore = beforeTuples.GroupBy(t => t).Count(g => g.Count() > 1);
        var pairedAfter = afterTuples.GroupBy(t => t).Count(g => g.Count() > 1);

        return
        [
            new SoftMetricDelta("preferred_teacher", "優先講師との一致度", HigherIsBetter: false, preferenceBefore, preferenceAfter),
            new SoftMetricDelta("preferred_time", "希望日時との一致度", HigherIsBetter: true, availabilityBefore, availabilityAfter),
            new SoftMetricDelta("active_teacher_slots", "使用コマ数（講師×日時）", HigherIsBetter: false, activeBefore, activeAfter),
            new SoftMetricDelta("paired_slots", "複数人ペア配置数", HigherIsBetter: true, pairedBefore, pairedAfter),
        ];
    }

    // SqliteScheduleRunService.PreferencePenaltyと同じ計算式（通常担当講師の優先度＋第1〜3希望講師を
    // 統合した1つのペナルティ値。CP-SATの最適化目的関数と同じ重み付けを流用する）。
    private static int PreferencePenalty(PreferenceProfile profile, long teacherId)
    {
        var scores = new Dictionary<long, int>();
        if (profile.RegularTeacherId is long regular) scores[regular] = (profile.RegularTeacherPriority - 1) * 2 + 2;
        KeepMaximum(scores, profile.PreferredTeacher1Id, 6);
        KeepMaximum(scores, profile.PreferredTeacher2Id, 4);
        KeepMaximum(scores, profile.PreferredTeacher3Id, 2);
        return scores.Count == 0 ? 0 : scores.Values.Max() - scores.GetValueOrDefault(teacherId);
    }

    private static void KeepMaximum(Dictionary<long, int> scores, long? teacherId, int score)
    {
        if (teacherId is not long id) return;
        scores[id] = Math.Max(scores.GetValueOrDefault(id), score);
    }

    private static async Task<PreferenceProfile> ReadPreferenceProfileAsync(SqliteConnection connection, SqliteTransaction transaction, long requestId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(r.RegularTeacherId,p.RegularTeacherId),
                   COALESCE(NULLIF(r.RegularTeacherPriority,1),p.RegularTeacherPriority,1),
                   r.PreferredTeacher1Id,r.PreferredTeacher2Id,r.PreferredTeacher3Id
            FROM LessonRequest r
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE r.Id=$id;
            """;
        command.Parameters.AddWithValue("$id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new PreferenceProfile(null, 1, null, null, null);
        return new PreferenceProfile(
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4));
    }

    // SqliteScheduleRunServiceの候補生成と同じ既定値（該当日時に評価行が無ければ中立の1点）を使う。
    private static async Task<int> AvailabilityPreferenceAsync(SqliteConnection connection, SqliteTransaction transaction, long studentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
              COALESCE((SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId=$student AND OpenDateId=$date AND TimeSlotId=$slot),1),
              COALESCE((SELECT AvailabilityLevel FROM TeacherAvailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot),1);
            """;
        command.Parameters.AddWithValue("$student", studentId);
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Math.Min(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<List<(long Id, long TeacherId, long OpenDateId, long TimeSlotId)>> LoadAssignmentSlotsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var list = new List<(long, long, long, long)>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id,TeacherId,OpenDateId,TimeSlotId FROM Assignment;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
        return list;
    }

    private sealed record PreferenceProfile(long? RegularTeacherId, int RegularTeacherPriority, long? PreferredTeacher1Id, long? PreferredTeacher2Id, long? PreferredTeacher3Id);

    private static async Task<AssignmentState> ReadAssignmentAsync(SqliteConnection connection, SqliteTransaction transaction, long assignmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.TeacherId,a.OpenDateId,a.TimeSlotId,a.IsLocked,r.StudentId,r.SubjectId,
                   CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END,r.Id
            FROM Assignment a
            JOIN LessonRequest r ON r.Id=a.LessonRequestId
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE a.Id=$id;
            """;
        command.Parameters.AddWithValue("$id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("配置が見つかりません。");
        return new AssignmentState(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetBoolean(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetBoolean(6), reader.GetInt64(7));
    }

    private static async Task EnsureSlotOpenAsync(SqliteConnection connection, SqliteTransaction transaction, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM OpenDateTimeSlot ds JOIN OpenDate d ON d.Id=ds.OpenDateId JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ds.OpenDateId=$date AND ds.TimeSlotId=$slot AND d.IsOpen=1 AND ts.Active=1);";
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
            throw new InvalidOperationException("移動先の開講コマが無効です。");
    }

    // Python版と同様、事前確定は専用の種別を持たず「手動配置（IsManual=1）＋ロック（IsLocked=1)」として
    // 保存する。isLockedの値だけで、通常の手動配置か事前確定かを監査ログ上区別する。
    private static async Task AddCoreAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, bool confirmSoftWarnings, string? reason, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var preview = await BuildAddPreviewAsync(connection, transaction, requestId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        if (preview.Decision == EditDecision.Red) throw new InvalidOperationException(preview.Message);
        if (preview.Decision == EditDecision.Yellow && !confirmSoftWarnings) throw new SoftWarningConfirmationRequiredException(preview);

        var request = await ReadRequestAsync(connection, transaction, requestId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);

        await using var add = connection.CreateCommand();
        add.Transaction = transaction;
        add.CommandText = """
            INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual)
            VALUES($request,$teacher,$date,$slot,$locked,'manual',$session,1);
            """;
        add.Parameters.AddWithValue("$request", requestId);
        add.Parameters.AddWithValue("$teacher", teacherId);
        add.Parameters.AddWithValue("$date", openDateId);
        add.Parameters.AddWithValue("$slot", timeSlotId);
        add.Parameters.AddWithValue("$locked", isLocked);
        add.Parameters.AddWithValue("$session", request.AssignedSessions + 1);
        await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var auditReason = preview.Decision == EditDecision.Yellow ? (string.IsNullOrWhiteSpace(reason) ? "ソフト条件を確認して配置" : reason) : (isLocked ? "事前確定授業" : "時間割手動配置");
        await using var audit=connection.CreateCommand();audit.Transaction=transaction;audit.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,$action,'assignment',$entity,$after,$reason,'manual',$operation);";audit.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));audit.Parameters.AddWithValue("$action",isLocked?"preconfirmed_assignment_added":"manual_assignment_added");audit.Parameters.AddWithValue("$entity",requestId.ToString(CultureInfo.InvariantCulture));audit.Parameters.AddWithValue("$after",JsonSerializer.Serialize(new{teacherId,openDateId,timeSlotId,isLocked}));audit.Parameters.AddWithValue("$reason",auditReason);audit.Parameters.AddWithValue("$operation",Guid.NewGuid().ToString("N"));await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RequestState> ReadRequestAsync(SqliteConnection connection, SqliteTransaction transaction, long requestId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.StudentId,r.SubjectId,r.RequiredSessions,
                   (SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id),
                   CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END
            FROM LessonRequest r
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE r.Id=$request
              AND EXISTS(SELECT 1 FROM OpenDateTimeSlot ds JOIN OpenDate d ON d.Id=ds.OpenDateId WHERE ds.OpenDateId=$date AND ds.TimeSlotId=$slot AND d.IsOpen=1);
            """;
        command.Parameters.AddWithValue("$request", requestId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("受講希望または開校コマが無効です。");
        var state = new RequestState(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetBoolean(4));
        if (state.AssignedSessions >= state.RequiredSessions)
            throw new InvalidOperationException("この受講希望は必要回数がすべて配置済みです。");
        return state;
    }

    private static async Task EnsureNoStudentCollisionAsync(SqliteConnection connection, SqliteTransaction transaction, long studentId, long openDateId, long timeSlotId, long? excludeAssignmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId WHERE r.StudentId=$student AND a.OpenDateId=$date AND a.TimeSlotId=$slot AND a.Id<>$exclude);";
        command.Parameters.AddWithValue("$student", studentId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        command.Parameters.AddWithValue("$exclude", excludeAssignmentId ?? 0L);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new InvalidOperationException("同じ日時に生徒の授業が既にあります。");
    }

    // ユーザー要望（checkpoint151）「未配置をドラッグして生徒が出席不可のコマへ置こうとしたとき、
    // 『生徒がアンケートで出席不可にしています。』のような具体的な警告を出す」への対応。従来は
    // 生徒側・講師側の出欠を1つのSQLでOR結合した単一のbool（IsAvailableAsync）しか返さず、
    // 呼び出し側ではどちらが原因かを区別できなかった（メッセージは常に「生徒または講師が...」で
    // 一括りだった）。生徒側・講師側を別メソッドへ分割し、呼び出し側でどちらが原因かに応じた
    // メッセージを組み立てられるようにした。
    private static async Task<bool> IsStudentAvailableAsync(SqliteConnection connection, SqliteTransaction transaction, long studentId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // 生徒側は意図的な既定動作（未回答なら空き扱い）。
        command.CommandText = """
            SELECT NOT (EXISTS(SELECT 1 FROM StudentAvailability WHERE StudentId=$student) AND NOT EXISTS(SELECT 1 FROM StudentAvailability WHERE StudentId=$student AND OpenDateId=$date AND TimeSlotId=$slot AND AvailabilityLevel>0));
            """;
        command.Parameters.AddWithValue("$student", studentId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<bool> IsTeacherAvailableAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // ユーザー報告バグ修正: 講師側は「行が1件でもあれば、この時間帯の行が無い＝出勤不可」という
        // 条件だった（EXISTS(any row) AND NOT EXISTS(this slot available)）ため、アンケートに一度も
        // 回答していない講師（TeacherAvailability行が0件）は前段のEXISTSがfalseとなり、常に出勤可能
        // 扱いになっていた。判定の粒度を「講師単位」ではなく「プロジェクト単位」に変更した
        // （プロジェクト全体でまだ出勤可否データを1件も取込んでいなければ、従来通り出勤可能扱いの
        // ままにする。詳細はSqliteScheduleRunService.BuildProblemAsyncの同種の修正コメント参照。
        // このアプリは1ファイル1プロジェクト固定のためProjectId=1で決め打ちする、この付近の他の
        // クエリと同じ慣習）。
        command.CommandText = """
            SELECT NOT (
              (EXISTS(SELECT 1 FROM TeacherAvailability WHERE ProjectId=1) AND NOT EXISTS(SELECT 1 FROM TeacherAvailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot AND AvailabilityLevel>0))
              OR EXISTS(SELECT 1 FROM TeacherUnavailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot));
            """;
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task EnsureAvailabilityAsync(SqliteConnection connection, SqliteTransaction transaction, long studentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        var studentAvailable = await IsStudentAvailableAsync(connection, transaction, studentId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        var teacherAvailable = await IsTeacherAvailableAsync(connection, transaction, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        if (!studentAvailable || !teacherAvailable)
            throw new InvalidOperationException("生徒または講師が参加できない日時です。");
    }

    // ユーザー要望「担当する生徒の人数（既定1対2）を1対3・1対4等へ変更できるようにしたい」への対応。
    // checkpoint93まで固定だった"2"をSchedulingPolicy.MaxStudentsPerTeacherへ一般化した。行が無い
    // （＝一度も保存されていない）プロジェクトはSchedulingPolicy.Default（2）扱いとする。
    private static async Task<int> ReadMaxStudentsPerTeacherAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MaxStudentsPerTeacher FROM SchedulingPolicy WHERE ProjectId=1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? SeminarSched.Domain.Scheduling.SchedulingPolicy.Default.MaxStudentsPerTeacher : Convert.ToInt32(value);
    }

    private static async Task<bool> IsWithinTeacherCapacityAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long openDateId, long timeSlotId, int requestedLoad, int maxCapacity, long? excludeAssignmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN $maxCapacity ELSE 1 END),0)
            FROM Assignment a
            JOIN LessonRequest r ON r.Id=a.LessonRequestId
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE a.TeacherId=$teacher AND a.OpenDateId=$date AND a.TimeSlotId=$slot AND a.Id<>$exclude;
            """;
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        command.Parameters.AddWithValue("$exclude", excludeAssignmentId ?? 0L);
        command.Parameters.AddWithValue("$maxCapacity", maxCapacity);
        var existingLoad = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return existingLoad + requestedLoad <= maxCapacity;
    }

    private static async Task EnsureTeacherCapacityAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long openDateId, long timeSlotId, int requestedLoad, int maxCapacity, long? excludeAssignmentId, CancellationToken cancellationToken)
    {
        if (!await IsWithinTeacherCapacityAsync(connection, transaction, teacherId, openDateId, timeSlotId, requestedLoad, maxCapacity, excludeAssignmentId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"同じ日時の講師担当上限（{maxCapacity}人）を超えます。");
    }

    // ユーザー要望（checkpoint152）「自動作成の後から配置するものは元から動かないようにするという
    // 設定で、残ったものがどこにも配置できなかった場合に、ここをこう変えれば配置できますよ、という
    // のがあれば警告を出して、『入れ替えて配置する』『配置せずそのままにする』ボタンを用意してほしい。
    // この入れ替えはできるだけ最小になるように」への対応。未配置の受講希望1件ごとに、既存の固定配置
    // （ロック・手動配置は対象外＝ユーザーが明示的に確定させたものは動かさない）を1件だけ別の空きコマへ
    // 移動すれば配置できる、という「1手」の入れ替え案だけを探す。それ以上の連鎖的な入れ替えは探索しない
    // （これが「最小」の実装範囲）。個別クエリをネストで何度も発行すると組み合わせ爆発になるため、
    // 必要なデータを一括読み込みしてインメモリで判定する（SqliteScheduleRunService.BuildProblemAsyncと
    // 同じ設計方針）。
    internal async Task<IReadOnlyList<SwapSuggestion>> FindSwapSuggestionsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);

        var maxCapacity = await ReadMaxCapacityNoTransactionAsync(connection, cancellationToken).ConfigureAwait(false);

        var slots = new List<(long OpenDateId, long TimeSlotId, string Label)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT d.Id,s.Id,d.Date,s.DisplayName
                FROM OpenDate d JOIN OpenDateTimeSlot ds ON ds.OpenDateId=d.Id JOIN TimeSlot s ON s.Id=ds.TimeSlotId
                WHERE d.IsOpen=1 AND s.Active=1 ORDER BY d.Date,s.SortOrder;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var date = DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
                slots.Add((reader.GetInt64(0), reader.GetInt64(1), $"{date:yyyy-MM-dd}({JapaneseWeekday(date)}) {reader.GetString(3)}"));
            }
        }

        var teachersBySubject = new Dictionary<long, List<(long TeacherId, string Label)>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT q.SubjectId,t.Id,t.Name FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId
                WHERE q.CanTeach=1 AND t.Active=1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var subjectId = reader.GetInt64(0);
                if (!teachersBySubject.TryGetValue(subjectId, out var list)) { list = []; teachersBySubject[subjectId] = list; }
                list.Add((reader.GetInt64(1), reader.GetString(2)));
            }
        }

        var studentsWithAnyAvailability = new HashSet<long>();
        var studentAvailableSlots = new HashSet<(long StudentId, long OpenDateId, long TimeSlotId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT StudentId,OpenDateId,TimeSlotId,AvailabilityLevel FROM StudentAvailability;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var studentId = reader.GetInt64(0);
                studentsWithAnyAvailability.Add(studentId);
                if (reader.GetInt32(3) > 0) studentAvailableSlots.Add((studentId, reader.GetInt64(1), reader.GetInt64(2)));
            }
        }

        var anyTeacherAvailabilityData = false;
        var teacherAvailableSlots = new HashSet<(long TeacherId, long OpenDateId, long TimeSlotId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TeacherId,OpenDateId,TimeSlotId,AvailabilityLevel FROM TeacherAvailability WHERE ProjectId=1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                anyTeacherAvailabilityData = true;
                if (reader.GetInt32(3) > 0) teacherAvailableSlots.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
            }
        }
        var teacherUnavailable = new HashSet<(long TeacherId, long OpenDateId, long TimeSlotId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TeacherId,OpenDateId,TimeSlotId FROM TeacherUnavailability;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                teacherUnavailable.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        var assignments = new List<SwapAssignmentInfo>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.Id,r.StudentId,r.SubjectId,a.TeacherId,a.OpenDateId,a.TimeSlotId,
                       CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END,
                       a.IsLocked,a.IsManual,st.Name,su.DisplayName
                FROM Assignment a
                JOIN LessonRequest r ON r.Id=a.LessonRequestId
                JOIN Student st ON st.Id=r.StudentId
                JOIN Subject su ON su.Id=r.SubjectId
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                assignments.Add(new SwapAssignmentInfo(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
                    reader.GetInt32(6) != 0, reader.GetBoolean(7), reader.GetBoolean(8), reader.GetString(9), reader.GetString(10)));
        }
        var assignmentsBySlot = assignments.GroupBy(a => (a.TeacherId, a.OpenDateId, a.TimeSlotId)).ToDictionary(g => g.Key, g => g.ToList());
        var studentBookedSlots = assignments.Select(a => (a.StudentId, a.OpenDateId, a.TimeSlotId)).ToHashSet();

        var unassigned = new List<(long RequestId, long StudentId, long SubjectId, bool OneToOneRequired, string Label)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT r.Id,r.StudentId,r.SubjectId,
                       CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END,
                       st.Name||'（'||st.Grade||'） / '||su.DisplayName
                FROM LessonRequest r
                JOIN Student st ON st.Id=r.StudentId
                JOIN Subject su ON su.Id=r.SubjectId
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                WHERE r.RequiredSessions > (SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id);
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                unassigned.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3) != 0, reader.GetString(4)));
        }

        bool StudentAvailable(long studentId, long dateId, long slotId) =>
            !studentsWithAnyAvailability.Contains(studentId) || studentAvailableSlots.Contains((studentId, dateId, slotId));
        bool TeacherAvailable(long teacherId, long dateId, long slotId) =>
            (!anyTeacherAvailabilityData || teacherAvailableSlots.Contains((teacherId, dateId, slotId)))
            && !teacherUnavailable.Contains((teacherId, dateId, slotId));
        int LoadAt(long teacherId, long dateId, long slotId, long? excludeAssignmentId = null) =>
            assignmentsBySlot.TryGetValue((teacherId, dateId, slotId), out var list)
                ? list.Where(a => a.AssignmentId != excludeAssignmentId).Sum(a => a.OneToOneRequired ? maxCapacity : 1)
                : 0;

        var results = new List<SwapSuggestion>();
        foreach (var demand in unassigned)
        {
            if (!teachersBySubject.TryGetValue(demand.SubjectId, out var qualifiedTeachers)) continue;
            var requestedLoad = demand.OneToOneRequired ? maxCapacity : 1;
            SwapSuggestion? found = null;

            foreach (var (teacherId, teacherLabel) in qualifiedTeachers)
            {
                if (found is not null) break;
                foreach (var (dateId, slotId, slotLabel) in slots)
                {
                    if (!StudentAvailable(demand.StudentId, dateId, slotId)) continue;
                    if (!TeacherAvailable(teacherId, dateId, slotId)) continue;
                    if (studentBookedSlots.Contains((demand.StudentId, dateId, slotId))) continue;

                    var currentLoad = LoadAt(teacherId, dateId, slotId);
                    if (currentLoad + requestedLoad <= maxCapacity) continue; // 空きがあるなら入れ替え不要（ソルバーが本来置けたはず）
                    if (!assignmentsBySlot.TryGetValue((teacherId, dateId, slotId), out var occupying)) continue;

                    foreach (var moving in occupying)
                    {
                        if (moving.IsLocked || moving.IsManual) continue; // ユーザーが確定させた配置は動かさない
                        var remainingLoad = currentLoad - (moving.OneToOneRequired ? maxCapacity : 1);
                        if (remainingLoad + requestedLoad > maxCapacity) continue; // これ1件を動かしても足りない

                        if (!teachersBySubject.TryGetValue(moving.SubjectId, out var movingQualified)) continue;
                        var movingLoad = moving.OneToOneRequired ? maxCapacity : 1;
                        foreach (var (altTeacherId, altTeacherLabel) in movingQualified)
                        {
                            if (found is not null) break;
                            foreach (var (altDateId, altSlotId, altSlotLabel) in slots)
                            {
                                if (altTeacherId == teacherId && altDateId == dateId && altSlotId == slotId) continue; // 元の場所は除く
                                if (!StudentAvailable(moving.StudentId, altDateId, altSlotId)) continue;
                                if (!TeacherAvailable(altTeacherId, altDateId, altSlotId)) continue;
                                if (studentBookedSlots.Contains((moving.StudentId, altDateId, altSlotId))) continue;
                                if (LoadAt(altTeacherId, altDateId, altSlotId, moving.AssignmentId) + movingLoad > maxCapacity) continue;

                                found = new SwapSuggestion(
                                    demand.RequestId, demand.Label,
                                    moving.AssignmentId, $"{moving.StudentLabel} / {moving.SubjectLabel}",
                                    teacherId, dateId, slotId, $"{teacherLabel} {slotLabel}",
                                    altTeacherId, altDateId, altSlotId, $"{altTeacherLabel} {altSlotLabel}");
                                break;
                            }
                            if (found is not null) break;
                        }
                        if (found is not null) break;
                    }
                    if (found is not null) break;
                }
            }

            if (found is not null) results.Add(found);
        }
        return results;
    }

    // FindSwapSuggestionsAsyncが見つけた1手の入れ替え案を実際に適用する。Finding時点から状態が
    // 変わっていた場合（移動対象が別の場所へ動いていた・ロックされた等）は何も変更せず例外を投げる。
    internal async Task ApplySwapSuggestionAsync(string projectPath, SwapSuggestion suggestion, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var moving = await ReadAssignmentAsync(connection, transaction, suggestion.MovingAssignmentId, cancellationToken).ConfigureAwait(false);
        if (moving.IsLocked) throw new InvalidOperationException("移動対象の配置がロックされています。最新の状態を確認してください。");
        if (moving.TeacherId != suggestion.FreedTeacherId || moving.OpenDateId != suggestion.FreedOpenDateId || moving.TimeSlotId != suggestion.FreedTimeSlotId)
            throw new InvalidOperationException("移動対象の配置が変更されています。最新の状態を確認してください。");
        await EnsureSlotOpenAsync(connection, transaction, suggestion.NewOpenDateId, suggestion.NewTimeSlotId, cancellationToken).ConfigureAwait(false);

        var request = await ReadRequestAsync(connection, transaction, suggestion.UnassignedRequestId, suggestion.FreedOpenDateId, suggestion.FreedTimeSlotId, cancellationToken).ConfigureAwait(false);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Assignment SET TeacherId=$teacher,OpenDateId=$date,TimeSlotId=$slot,IsManual=1 WHERE Id=$id;";
            update.Parameters.AddWithValue("$teacher", suggestion.NewTeacherId);
            update.Parameters.AddWithValue("$date", suggestion.NewOpenDateId);
            update.Parameters.AddWithValue("$slot", suggestion.NewTimeSlotId);
            update.Parameters.AddWithValue("$id", suggestion.MovingAssignmentId);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var add = connection.CreateCommand())
        {
            add.Transaction = transaction;
            add.CommandText = """
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual)
                VALUES($request,$teacher,$date,$slot,0,'manual',$session,1);
                """;
            add.Parameters.AddWithValue("$request", suggestion.UnassignedRequestId);
            add.Parameters.AddWithValue("$teacher", suggestion.FreedTeacherId);
            add.Parameters.AddWithValue("$date", suggestion.FreedOpenDateId);
            add.Parameters.AddWithValue("$slot", suggestion.FreedTimeSlotId);
            add.Parameters.AddWithValue("$session", request.AssignedSessions + 1);
            await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId)
                VALUES(1,$utc,'swap_suggestion_applied','assignment',$entity,$after,$reason,'manual',$operation);
                """;
            audit.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$entity", suggestion.MovingAssignmentId.ToString(CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new
            {
                suggestion.MovingAssignmentId, suggestion.NewTeacherId, suggestion.NewOpenDateId, suggestion.NewTimeSlotId,
                suggestion.UnassignedRequestId, suggestion.FreedTeacherId, suggestion.FreedOpenDateId, suggestion.FreedTimeSlotId,
            }));
            audit.Parameters.AddWithValue("$reason", "未配置解消のための入れ替え");
            audit.Parameters.AddWithValue("$operation", Guid.NewGuid().ToString("N"));
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadMaxCapacityNoTransactionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MaxStudentsPerTeacher FROM SchedulingPolicy WHERE ProjectId=1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? SeminarSched.Domain.Scheduling.SchedulingPolicy.Default.MaxStudentsPerTeacher : Convert.ToInt32(value);
    }

    private static string JapaneseWeekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Sunday => "日", DayOfWeek.Monday => "月", DayOfWeek.Tuesday => "火", DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木", DayOfWeek.Friday => "金", _ => "土",
    };

    private sealed record SwapAssignmentInfo(long AssignmentId, long StudentId, long SubjectId, long TeacherId, long OpenDateId, long TimeSlotId, bool OneToOneRequired, bool IsLocked, bool IsManual, string StudentLabel, string SubjectLabel);

    private static async Task<SqliteConnection> OpenAsync(string projectPath, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(projectPath),
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private sealed record RequestState(long StudentId, long SubjectId, int RequiredSessions, int AssignedSessions, bool OneToOneRequired);
    private sealed record AssignmentState(long TeacherId, long OpenDateId, long TimeSlotId, bool IsLocked, long StudentId, long SubjectId, bool OneToOneRequired, long LessonRequestId);
}
