using Microsoft.Data.Sqlite;
using SeminarSched.Application.GroupLessons;
using SeminarSched.Domain.GroupLessons;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.GroupLessons;

public sealed class SqliteGroupLessonService : IGroupLessonService
{
    public async Task<IReadOnlyList<GroupLessonClass>> GetClassesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Name,Grade,Subject,AllowOtherGrades,Active,TeacherId FROM GroupLessonClass WHERE ProjectId=1 ORDER BY Grade,Name;";
        var result = new List<GroupLessonClass>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonClass(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        return result;
    }

    public async Task<GroupLessonClass> SaveClassAsync(string projectPath, GroupLessonClass value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long id;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                try
                {
                    if (value.Id == 0)
                    {
                        command.CommandText = "INSERT INTO GroupLessonClass(ProjectId,Name,Grade,Subject,AllowOtherGrades,Active,TeacherId) VALUES(1,$name,$grade,$subject,$allow,$active,$teacher); SELECT last_insert_rowid();";
                        command.Parameters.AddWithValue("$name", value.Name); command.Parameters.AddWithValue("$grade", value.Grade); command.Parameters.AddWithValue("$subject", value.Subject); command.Parameters.AddWithValue("$allow", value.AllowOtherGrades); command.Parameters.AddWithValue("$active", value.Active); command.Parameters.AddWithValue("$teacher", (object?)value.TeacherId ?? DBNull.Value);
                        id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                    }
                    else
                    {
                        command.CommandText = "UPDATE GroupLessonClass SET Name=$name,Grade=$grade,Subject=$subject,AllowOtherGrades=$allow,Active=$active,TeacherId=$teacher WHERE Id=$id; SELECT changes();";
                        command.Parameters.AddWithValue("$name", value.Name); command.Parameters.AddWithValue("$grade", value.Grade); command.Parameters.AddWithValue("$subject", value.Subject); command.Parameters.AddWithValue("$allow", value.AllowOtherGrades); command.Parameters.AddWithValue("$active", value.Active); command.Parameters.AddWithValue("$teacher", (object?)value.TeacherId ?? DBNull.Value); command.Parameters.AddWithValue("$id", value.Id);
                        var changed = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                        if (changed != 1) throw new InvalidOperationException("更新対象のクラスが見つかりません。");
                        id = value.Id;
                    }
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
                {
                    throw new InvalidOperationException($"クラス名「{value.Name}」は既に登録されています。");
                }
            }
            await RecomputeTeacherBlocksForClassAsync(connection, transaction, id, value.TeacherId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return value with { Id = id };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteClassAsync(string projectPath, long classId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // 担当講師由来のTeacherUnavailabilityはGroupLessonClassへの外部キーを持たないため、
        // ON DELETE CASCADEでは消えない。行削除の前に「担当講師なし」相当まで復元してから削除する。
        await RecomputeTeacherBlocksForClassAsync(connection, transaction, classId, effectiveTeacherId: null, cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM GroupLessonClass WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", classId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GroupLessonCalendarDate>> GetCalendarDatesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,Date FROM OpenDate ORDER BY Date;";
        var result = new List<GroupLessonCalendarDate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonCalendarDate(reader.GetInt64(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task<IReadOnlyList<GroupLessonSessionOption>> GetAllSessionsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id,s.ClassId,c.Name,c.Subject,s.OpenDateId,s.StartTime,s.EndTime
            FROM GroupLessonSession s JOIN GroupLessonClass c ON c.Id=s.ClassId
            ORDER BY s.OpenDateId,s.StartTime;
            """;
        var result = new List<GroupLessonSessionOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonSessionOption(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), TimeOnly.ParseExact(reader.GetString(5), "HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeOnly.ParseExact(reader.GetString(6), "HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task AddSessionsAsync(string projectPath, long classId, IReadOnlyCollection<long> openDateIds, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime) throw new ArgumentException("終了時刻は開始時刻より後にしてください。");
        if (openDateIds.Count == 0) return;
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var startText = startTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var endText = endTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var openDateId in openDateIds)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO GroupLessonSession(ClassId,OpenDateId,StartTime,EndTime) VALUES($class,$date,$start,$end) ON CONFLICT(ClassId,OpenDateId,StartTime,EndTime) DO NOTHING;";
                command.Parameters.AddWithValue("$class", classId); command.Parameters.AddWithValue("$date", openDateId); command.Parameters.AddWithValue("$start", startText); command.Parameters.AddWithValue("$end", endText);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // 追加した開講日程が担当講師の既存の時間帯と重なる場合、その日時のブロックも増やす
            // 必要があるため再計算する（担当講師が割り当てられていなければ何もしない）。
            var teacherId = await ReadClassTeacherIdAsync(connection, transaction, classId, cancellationToken).ConfigureAwait(false);
            await RecomputeTeacherBlocksForClassAsync(connection, transaction, classId, teacherId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task RemoveSessionAsync(string projectPath, long sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        long classId;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT ClassId FROM GroupLessonSession WHERE Id=$id;";
            read.Parameters.AddWithValue("$id", sessionId);
            var value = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null) { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); return; }
            classId = Convert.ToInt64(value);
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM GroupLessonSession WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        // 削除した開講日程が担当講師のブロックの根拠だった可能性があるため再計算する。
        var teacherId = await ReadClassTeacherIdAsync(connection, transaction, classId, cancellationToken).ConfigureAwait(false);
        await RecomputeTeacherBlocksForClassAsync(connection, transaction, classId, teacherId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GroupLessonEnrollmentCandidate>> GetEnrollmentCandidatesAsync(string projectPath, long classId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        string grade; bool allowOtherGrades;
        await using (var classCommand = connection.CreateCommand())
        {
            classCommand.CommandText = "SELECT Grade,AllowOtherGrades FROM GroupLessonClass WHERE Id=$id;";
            classCommand.Parameters.AddWithValue("$id", classId);
            await using var reader = await classCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("指定されたクラスが見つかりません。");
            grade = reader.GetString(0); allowOtherGrades = reader.GetBoolean(1);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT st.Id,st.ExternalId,st.Name,st.Grade,
                   CASE WHEN e.StudentId IS NOT NULL THEN 1 ELSE 0 END
            FROM Student st
            LEFT JOIN GroupLessonEnrollment e ON e.StudentId=st.Id AND e.ClassId=$class
            WHERE st.Active=1 AND ($allowOther=1 OR st.Grade=$grade)
            ORDER BY st.ExternalId;
            """;
        command.Parameters.AddWithValue("$class", classId); command.Parameters.AddWithValue("$allowOther", allowOtherGrades); command.Parameters.AddWithValue("$grade", grade);
        var result = new List<GroupLessonEnrollmentCandidate>();
        await using var studentReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await studentReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonEnrollmentCandidate(studentReader.GetInt64(0), studentReader.GetString(1), studentReader.GetString(2), studentReader.GetString(3), studentReader.GetBoolean(4)));
        return result;
    }

    public async Task SetEnrollmentAsync(string projectPath, long classId, long studentId, bool enrolled, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = enrolled
            ? "INSERT INTO GroupLessonEnrollment(ClassId,StudentId) VALUES($class,$student) ON CONFLICT(ClassId,StudentId) DO NOTHING;"
            : "DELETE FROM GroupLessonEnrollment WHERE ClassId=$class AND StudentId=$student;";
        command.Parameters.AddWithValue("$class", classId); command.Parameters.AddWithValue("$student", studentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GroupLessonTeacherCandidate>> GetTeacherCandidatesAsync(string projectPath, long classId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT te.Id,te.ExternalId,te.Name,
                   CASE WHEN gt.TeacherId IS NOT NULL THEN 1 ELSE 0 END
            FROM Teacher te
            LEFT JOIN GroupLessonTeacher gt ON gt.TeacherId=te.Id AND gt.ClassId=$class
            WHERE te.Active=1
            ORDER BY te.ExternalId;
            """;
        command.Parameters.AddWithValue("$class", classId);
        var result = new List<GroupLessonTeacherCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonTeacherCandidate(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return result;
    }

    public async Task SetTeacherAssignmentAsync(string projectPath, long classId, long teacherId, bool assigned, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = assigned
            ? "INSERT INTO GroupLessonTeacher(ClassId,TeacherId) VALUES($class,$teacher) ON CONFLICT(ClassId,TeacherId) DO NOTHING;"
            : "DELETE FROM GroupLessonTeacher WHERE ClassId=$class AND TeacherId=$teacher;";
        command.Parameters.AddWithValue("$class", classId); command.Parameters.AddWithValue("$teacher", teacherId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long?> ReadClassTeacherIdAsync(SqliteConnection connection, SqliteTransaction transaction, long classId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TeacherId FROM GroupLessonClass WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", classId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    // ユーザー要望（checkpoint112）「集団授業のクラスに担当講師（任意）を割り当て...その講師はその
    // 日時に個別授業を持てないようにブロックする」への対応。クラスの担当講師と、その全開講セッション
    // （GroupLessonSession、コマに縛られない自由な開始・終了時刻）から、時間帯が重なるTimeSlotを
    // 求め、TeacherUnavailabilityへ自動反映する。TeacherUnavailabilityは④時間割編集の「出勤不可」
    // 手動指定と共有するテーブルのため、このクラス由来で追加した行だけを後から正しく取り消せるよう、
    // 専用の紐付けテーブルGroupLessonTeacherBlockで「どの行がこのクラス由来か」を記録する
    // （同じ講師・日時を複数のクラスがそれぞれ担当講師に割り当てるケースでも、片方を解除したときに
    // もう片方の分まで誤って取り消さないようにするため）。担当講師が既にその日時に個別指導の配置を
    // 持っている場合は、丸ごと中止する（SqliteScheduleEditorService.SetTeacherUnavailableManyAsync
    // と同じ方針）。
    private static async Task RecomputeTeacherBlocksForClassAsync(
        SqliteConnection connection, SqliteTransaction transaction, long classId, long? effectiveTeacherId, CancellationToken cancellationToken)
    {
        var previous = new HashSet<(long TeacherId, long OpenDateId, long TimeSlotId)>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT TeacherId,OpenDateId,TimeSlotId FROM GroupLessonTeacherBlock WHERE ClassId=$class;";
            read.Parameters.AddWithValue("$class", classId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                previous.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        var target = new HashSet<(long TeacherId, long OpenDateId, long TimeSlotId)>();
        if (effectiveTeacherId is long teacherId)
        {
            var sessions = new List<(long OpenDateId, TimeOnly Start, TimeOnly End)>();
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT OpenDateId,StartTime,EndTime FROM GroupLessonSession WHERE ClassId=$class;";
                read.Parameters.AddWithValue("$class", classId);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    sessions.Add((reader.GetInt64(0), TimeOnly.ParseExact(reader.GetString(1), "HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeOnly.ParseExact(reader.GetString(2), "HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (sessions.Count > 0)
            {
                var slots = new List<(long OpenDateId, long TimeSlotId, TimeOnly Start, TimeOnly End)>();
                await using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT ds.OpenDateId,ts.Id,ts.StartTime,ts.EndTime FROM OpenDateTimeSlot ds JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId WHERE ts.Active=1;";
                    await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        slots.Add((reader.GetInt64(0), reader.GetInt64(1), TimeOnly.ParseExact(reader.GetString(2), "HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeOnly.ParseExact(reader.GetString(3), "HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
                }

                foreach (var session in sessions)
                    foreach (var slot in slots.Where(s => s.OpenDateId == session.OpenDateId && s.Start < session.End && session.Start < s.End))
                        target.Add((teacherId, slot.OpenDateId, slot.TimeSlotId));
            }

            var newlyBlocked = target.Except(previous).ToArray();
            foreach (var (blockedTeacherId, openDateId, timeSlotId) in newlyBlocked)
            {
                await using var check = connection.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = "SELECT EXISTS(SELECT 1 FROM Assignment WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);";
                check.Parameters.AddWithValue("$teacher", blockedTeacherId); check.Parameters.AddWithValue("$date", openDateId); check.Parameters.AddWithValue("$slot", timeSlotId);
                if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
                    throw new InvalidOperationException("この講師は割り当てようとした開講日時に既に個別指導の配置があります。先に配置を移動または削除してください。");
            }
        }

        foreach (var (removedTeacherId, openDateId, timeSlotId) in previous.Except(target))
        {
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM GroupLessonTeacherBlock WHERE ClassId=$class AND OpenDateId=$date AND TimeSlotId=$slot;";
                delete.Parameters.AddWithValue("$class", classId); delete.Parameters.AddWithValue("$date", openDateId); delete.Parameters.AddWithValue("$slot", timeSlotId);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM GroupLessonTeacherBlock WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);";
            check.Parameters.AddWithValue("$teacher", removedTeacherId); check.Parameters.AddWithValue("$date", openDateId); check.Parameters.AddWithValue("$slot", timeSlotId);
            var stillReferenced = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
            if (stillReferenced) continue;
            // ④時間割編集で手動でも「出勤不可」に設定し直されている場合（Source='manual'）は、この
            // クラス由来の紐付けが無くなっても手動指定として残す。
            await using var removeUnavailable = connection.CreateCommand();
            removeUnavailable.Transaction = transaction;
            removeUnavailable.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot AND Source='group_lesson';";
            removeUnavailable.Parameters.AddWithValue("$teacher", removedTeacherId); removeUnavailable.Parameters.AddWithValue("$date", openDateId); removeUnavailable.Parameters.AddWithValue("$slot", timeSlotId);
            await removeUnavailable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (addedTeacherId, openDateId, timeSlotId) in target.Except(previous))
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO GroupLessonTeacherBlock(ClassId,TeacherId,OpenDateId,TimeSlotId) VALUES($class,$teacher,$date,$slot);";
                insert.Parameters.AddWithValue("$class", classId); insert.Parameters.AddWithValue("$teacher", addedTeacherId); insert.Parameters.AddWithValue("$date", openDateId); insert.Parameters.AddWithValue("$slot", timeSlotId);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var insertUnavailable = connection.CreateCommand();
            insertUnavailable.Transaction = transaction;
            insertUnavailable.CommandText = "INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId,Source) VALUES($teacher,$date,$slot,'group_lesson') ON CONFLICT(TeacherId,OpenDateId,TimeSlotId) DO NOTHING;";
            insertUnavailable.Parameters.AddWithValue("$teacher", addedTeacherId); insertUnavailable.Parameters.AddWithValue("$date", openDateId); insertUnavailable.Parameters.AddWithValue("$slot", timeSlotId);
            await insertUnavailable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
