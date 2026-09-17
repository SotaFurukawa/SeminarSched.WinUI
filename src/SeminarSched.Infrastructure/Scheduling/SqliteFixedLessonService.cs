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
        command.CommandText = "SELECT r.Id,r.StudentId,s.ExternalId||' '||s.Name||' / '||sub.DisplayName FROM LessonRequest r JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId ORDER BY s.ExternalId,sub.SortOrder;";
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
        command.CommandText = "SELECT Id,ExternalId||' '||Name FROM Teacher WHERE Active=1 ORDER BY ExternalId;";
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

    public async Task<IReadOnlyList<FixedLesson>> GetFixedLessonsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT a.Id,a.LessonRequestId,a.TeacherId,a.OpenDateId,a.TimeSlotId,d.Date||' '||ts.DisplayName||' / '||s.Name||' / '||te.Name FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student s ON s.Id=r.StudentId JOIN Teacher te ON te.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId WHERE a.IsLocked=1 ORDER BY d.Date,ts.SortOrder;";
        var result = new List<FixedLesson>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new FixedLesson(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5)));
        return result;
    }

    public async Task AddAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default)
        => await AddCoreAsync(projectPath, requestId, teacherId, openDateId, timeSlotId, true, false, "preconfirmed", cancellationToken).ConfigureAwait(false);

    internal async Task AddManualAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, CancellationToken cancellationToken = default)
        => await AddCoreAsync(projectPath, requestId, teacherId, openDateId, timeSlotId, isLocked, true, "manual", cancellationToken).ConfigureAwait(false);

    internal async Task MoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var current = await ReadAssignmentAsync(connection, transaction, assignmentId, cancellationToken).ConfigureAwait(false);
        if (current.IsLocked) throw new InvalidOperationException("ロック済みの配置は移動できません。先にロックを解除してください。");
        if (current.OpenDateId == openDateId && current.TimeSlotId == timeSlotId && current.TeacherId == teacherId)
            throw new InvalidOperationException("移動先が現在の配置と同じです。");
        await EnsureSlotOpenAsync(connection, transaction, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        await EnsureNoStudentCollisionAsync(connection, transaction, current.StudentId, openDateId, timeSlotId, assignmentId, cancellationToken).ConfigureAwait(false);
        await EnsureTeacherCanTeachAsync(connection, transaction, teacherId, current.SubjectId, cancellationToken).ConfigureAwait(false);
        await EnsureAvailabilityAsync(connection, transaction, current.StudentId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        await EnsureTeacherCapacityAsync(connection, transaction, teacherId, openDateId, timeSlotId, current.OneToOneRequired ? 2 : 1, assignmentId, cancellationToken).ConfigureAwait(false);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Assignment SET TeacherId=$teacher,OpenDateId=$date,TimeSlotId=$slot,IsManual=1 WHERE Id=$id;";
        update.Parameters.AddWithValue("$teacher", teacherId);
        update.Parameters.AddWithValue("$date", openDateId);
        update.Parameters.AddWithValue("$slot", timeSlotId);
        update.Parameters.AddWithValue("$id", assignmentId);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,'manual_assignment_moved','assignment',$entity,$after,'時間割手動移動','manual',$operation);";
        audit.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$entity", assignmentId.ToString(CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new { teacherId, openDateId, timeSlotId }));
        audit.Parameters.AddWithValue("$operation", Guid.NewGuid().ToString("N"));
        await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AssignmentState> ReadAssignmentAsync(SqliteConnection connection, SqliteTransaction transaction, long assignmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.TeacherId,a.OpenDateId,a.TimeSlotId,a.IsLocked,r.StudentId,r.SubjectId,
                   CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 1 ELSE 0 END
            FROM Assignment a
            JOIN LessonRequest r ON r.Id=a.LessonRequestId
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE a.Id=$id;
            """;
        command.Parameters.AddWithValue("$id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("配置が見つかりません。");
        return new AssignmentState(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetBoolean(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetBoolean(6));
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

    private static async Task AddCoreAsync(string projectPath, long requestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, bool isManual, string source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var request = await ReadRequestAsync(connection, transaction, requestId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        await EnsureNoStudentCollisionAsync(connection, transaction, request.StudentId, openDateId, timeSlotId, null, cancellationToken).ConfigureAwait(false);
        await EnsureTeacherCanTeachAsync(connection, transaction, teacherId, request.SubjectId, cancellationToken).ConfigureAwait(false);
        await EnsureAvailabilityAsync(connection, transaction, request.StudentId, teacherId, openDateId, timeSlotId, cancellationToken).ConfigureAwait(false);
        await EnsureTeacherCapacityAsync(connection, transaction, teacherId, openDateId, timeSlotId, request.OneToOneRequired ? 2 : 1, null, cancellationToken).ConfigureAwait(false);

        await using var add = connection.CreateCommand();
        add.Transaction = transaction;
        add.CommandText = """
            INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual)
            VALUES($request,$teacher,$date,$slot,$locked,$source,$session,$manual);
            """;
        add.Parameters.AddWithValue("$request", requestId);
        add.Parameters.AddWithValue("$teacher", teacherId);
        add.Parameters.AddWithValue("$date", openDateId);
        add.Parameters.AddWithValue("$slot", timeSlotId);
        add.Parameters.AddWithValue("$locked", isLocked);
        add.Parameters.AddWithValue("$source", source);
        add.Parameters.AddWithValue("$session", request.AssignedSessions + 1);
        add.Parameters.AddWithValue("$manual", isManual);
        await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var audit=connection.CreateCommand();audit.Transaction=transaction;audit.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,$action,'assignment',$entity,$after,$reason,'manual',$operation);";audit.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));audit.Parameters.AddWithValue("$action",isManual?"manual_assignment_added":"preconfirmed_assignment_added");audit.Parameters.AddWithValue("$entity",requestId.ToString(CultureInfo.InvariantCulture));audit.Parameters.AddWithValue("$after",JsonSerializer.Serialize(new{teacherId,openDateId,timeSlotId,isLocked,isManual}));audit.Parameters.AddWithValue("$reason",isManual?"時間割手動配置":"事前確定授業");audit.Parameters.AddWithValue("$operation",Guid.NewGuid().ToString("N"));await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Assignment WHERE Id=$id AND IsLocked=1;";
        command.Parameters.AddWithValue("$id", assignmentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task EnsureTeacherCanTeachAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long subjectId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId WHERE q.TeacherId=$teacher AND q.SubjectId=$subject AND q.CanTeach=1 AND t.Active=1);";
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$subject", subjectId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
            throw new InvalidOperationException("この講師は科目を担当可能に設定されていません。");
    }

    private static async Task EnsureAvailabilityAsync(SqliteConnection connection, SqliteTransaction transaction, long studentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
              (EXISTS(SELECT 1 FROM StudentAvailability WHERE StudentId=$student) AND NOT EXISTS(SELECT 1 FROM StudentAvailability WHERE StudentId=$student AND OpenDateId=$date AND TimeSlotId=$slot AND AvailabilityLevel>0))
              OR (EXISTS(SELECT 1 FROM TeacherAvailability WHERE TeacherId=$teacher) AND NOT EXISTS(SELECT 1 FROM TeacherAvailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot AND AvailabilityLevel>0))
              OR EXISTS(SELECT 1 FROM TeacherUnavailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);
            """;
        command.Parameters.AddWithValue("$student", studentId);
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new InvalidOperationException("生徒または講師が参加できない日時です。");
    }

    private static async Task EnsureTeacherCapacityAsync(SqliteConnection connection, SqliteTransaction transaction, long teacherId, long openDateId, long timeSlotId, int requestedLoad, long? excludeAssignmentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(CASE WHEN r.OneToOneRequired=1 OR COALESCE(p.OneToOneRequired,0)=1 THEN 2 ELSE 1 END),0)
            FROM Assignment a
            JOIN LessonRequest r ON r.Id=a.LessonRequestId
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE a.TeacherId=$teacher AND a.OpenDateId=$date AND a.TimeSlotId=$slot AND a.Id<>$exclude;
            """;
        command.Parameters.AddWithValue("$teacher", teacherId);
        command.Parameters.AddWithValue("$date", openDateId);
        command.Parameters.AddWithValue("$slot", timeSlotId);
        command.Parameters.AddWithValue("$exclude", excludeAssignmentId ?? 0L);
        var existingLoad = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (existingLoad + requestedLoad > 2)
            throw new InvalidOperationException("同じ日時の講師担当上限（2人）を超えます。");
    }

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
    private sealed record AssignmentState(long TeacherId, long OpenDateId, long TimeSlotId, bool IsLocked, long StudentId, long SubjectId, bool OneToOneRequired);
}
