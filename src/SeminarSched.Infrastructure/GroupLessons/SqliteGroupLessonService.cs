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
        command.CommandText = "SELECT Id,Name,Grade,AllowOtherGrades,Active FROM GroupLessonClass WHERE ProjectId=1 ORDER BY Grade,Name;";
        var result = new List<GroupLessonClass>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonClass(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        return result;
    }

    public async Task<GroupLessonClass> SaveClassAsync(string projectPath, GroupLessonClass value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        try
        {
            if (value.Id == 0)
            {
                command.CommandText = "INSERT INTO GroupLessonClass(ProjectId,Name,Grade,AllowOtherGrades,Active) VALUES(1,$name,$grade,$allow,$active); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$name", value.Name); command.Parameters.AddWithValue("$grade", value.Grade); command.Parameters.AddWithValue("$allow", value.AllowOtherGrades); command.Parameters.AddWithValue("$active", value.Active);
                var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                return value with { Id = id };
            }
            command.CommandText = "UPDATE GroupLessonClass SET Name=$name,Grade=$grade,AllowOtherGrades=$allow,Active=$active WHERE Id=$id; SELECT changes();";
            command.Parameters.AddWithValue("$name", value.Name); command.Parameters.AddWithValue("$grade", value.Grade); command.Parameters.AddWithValue("$allow", value.AllowOtherGrades); command.Parameters.AddWithValue("$active", value.Active); command.Parameters.AddWithValue("$id", value.Id);
            var changed = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (changed != 1) throw new InvalidOperationException("更新対象のクラスが見つかりません。");
            return value;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"クラス名「{value.Name}」は既に登録されています。");
        }
    }

    public async Task DeleteClassAsync(string projectPath, long classId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GroupLessonClass WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", classId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            SELECT s.Id,s.ClassId,c.Name,s.OpenDateId,s.StartTime,s.EndTime
            FROM GroupLessonSession s JOIN GroupLessonClass c ON c.Id=s.ClassId
            ORDER BY s.OpenDateId,s.StartTime;
            """;
        var result = new List<GroupLessonSessionOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new GroupLessonSessionOption(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), TimeOnly.ParseExact(reader.GetString(4), "HH:mm", System.Globalization.CultureInfo.InvariantCulture), TimeOnly.ParseExact(reader.GetString(5), "HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task AddSessionsAsync(string projectPath, long classId, IReadOnlyCollection<long> openDateIds, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime) throw new ArgumentException("終了時刻は開始時刻より後にしてください。");
        if (openDateIds.Count == 0) return;
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveSessionAsync(string projectPath, long sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GroupLessonSession WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
