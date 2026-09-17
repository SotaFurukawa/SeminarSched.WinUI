using Microsoft.Data.Sqlite;
using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.MasterData;

public sealed class SqliteMasterDataRepository : IMasterDataRepository
{
    public async Task<IReadOnlyList<Student>> GetStudentsAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, ExternalId, Name, Grade, DefaultMaxConsecutiveSlots, AllowGap, Note, Active FROM Student {(includeInactive ? "" : "WHERE Active = 1")} ORDER BY ExternalId;";
        var result = new List<Student>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Student(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetString(6), reader.GetBoolean(7)));
        return result;
    }

    public async Task<Student> SaveStudentAsync(string projectPath, Student student, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(student);
        await using var connection = await OpenAsync(projectPath, cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var id = await SaveAsync(connection, "Student", student.Id,
            "ExternalId, Name, Grade, DefaultMaxConsecutiveSlots, AllowGap, Note, Active",
            "@externalId, @name, @grade, @maximum, @allowGap, @note, @active",
            "ExternalId=@externalId, Name=@name, Grade=@grade, DefaultMaxConsecutiveSlots=@maximum, AllowGap=@allowGap, Note=@note, Active=@active",
            command => { command.Parameters.AddWithValue("@externalId", student.ExternalId); command.Parameters.AddWithValue("@name", student.Name); command.Parameters.AddWithValue("@grade", student.Grade); command.Parameters.AddWithValue("@maximum", student.DefaultMaxConsecutiveSlots); command.Parameters.AddWithValue("@allowGap", student.AllowGap); command.Parameters.AddWithValue("@note", student.Note); command.Parameters.AddWithValue("@active", student.Active); }, cancellationToken);
        return student with { Id = id };
    }

    public async Task<IReadOnlyList<Teacher>> GetTeachersAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, ExternalId, Name, AllowGap, Note, Active FROM Teacher {(includeInactive ? "" : "WHERE Active = 1")} ORDER BY ExternalId;";
        var result = new List<Teacher>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new Teacher(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4), reader.GetBoolean(5)));
        return result;
    }

    public async Task<Teacher> SaveTeacherAsync(string projectPath, Teacher teacher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(teacher); await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        var id = await SaveAsync(connection, "Teacher", teacher.Id, "ExternalId, Name, AllowGap, Note, Active", "@externalId, @name, @allowGap, @note, @active",
            "ExternalId=@externalId, Name=@name, AllowGap=@allowGap, Note=@note, Active=@active",
            command => { command.Parameters.AddWithValue("@externalId", teacher.ExternalId); command.Parameters.AddWithValue("@name", teacher.Name); command.Parameters.AddWithValue("@allowGap", teacher.AllowGap); command.Parameters.AddWithValue("@note", teacher.Note); command.Parameters.AddWithValue("@active", teacher.Active); }, cancellationToken);
        return teacher with { Id = id };
    }

    public async Task<IReadOnlyList<Subject>> GetSubjectsAsync(string projectPath, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT Id, Code, DisplayName, ShortName, SchoolLevel, SortOrder, Active FROM Subject {(includeInactive ? "" : "WHERE Active = 1")} ORDER BY SortOrder, Code;";
        var result = new List<Subject>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new Subject(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetBoolean(6)));
        return result;
    }

    public async Task<Subject> SaveSubjectAsync(string projectPath, Subject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject); await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        var id = await SaveAsync(connection, "Subject", subject.Id, "Code, DisplayName, ShortName, SchoolLevel, SortOrder, Active", "@code, @name, @short, @level, @sort, @active",
            "Code=@code, DisplayName=@name, ShortName=@short, SchoolLevel=@level, SortOrder=@sort, Active=@active",
            command => { command.Parameters.AddWithValue("@code", subject.Code); command.Parameters.AddWithValue("@name", subject.DisplayName); command.Parameters.AddWithValue("@short", subject.ShortName); command.Parameters.AddWithValue("@level", subject.SchoolLevel); command.Parameters.AddWithValue("@sort", subject.SortOrder); command.Parameters.AddWithValue("@active", subject.Active); }, cancellationToken);
        return subject with { Id = id };
    }

    public async Task SaveQualificationAsync(string projectPath, TeacherQualification qualification, CancellationToken cancellationToken = default)
    {
        qualification = qualification.Normalize(); await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO TeacherQualification (TeacherId, SubjectId, CanTeach, Note) VALUES (@teacher, @subject, @can, @note) ON CONFLICT(TeacherId, SubjectId) DO UPDATE SET CanTeach=excluded.CanTeach, Note=excluded.Note;";
        command.Parameters.AddWithValue("@teacher", qualification.TeacherId); command.Parameters.AddWithValue("@subject", qualification.SubjectId); command.Parameters.AddWithValue("@can", qualification.CanTeach); command.Parameters.AddWithValue("@note", qualification.Note); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherQualification>> GetQualificationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken);await EnsureSchemaAsync(connection,cancellationToken);await using var command=connection.CreateCommand();command.CommandText="SELECT TeacherId,SubjectId,CanTeach,Note FROM TeacherQualification ORDER BY TeacherId,SubjectId;";var result=new List<TeacherQualification>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))result.Add(new TeacherQualification(reader.GetInt64(0),reader.GetInt64(1),reader.GetBoolean(2),reader.GetString(3)));return result;
    }

    public async Task SaveRegularLessonAsync(string projectPath, RegularLessonProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile); await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO RegularLessonProfile (ProjectId, StudentId, SubjectId, RegularTeacherId, RegularTeacherPriority, OneToOneRequired, Note) VALUES (1, @student, @subject, @teacher, @priority, @one, @note) ON CONFLICT(ProjectId, StudentId, SubjectId) DO UPDATE SET RegularTeacherId=excluded.RegularTeacherId, RegularTeacherPriority=excluded.RegularTeacherPriority, OneToOneRequired=excluded.OneToOneRequired, Note=excluded.Note;";
        command.Parameters.AddWithValue("@student", profile.StudentId); command.Parameters.AddWithValue("@subject", profile.SubjectId); command.Parameters.AddWithValue("@teacher", (object?)profile.RegularTeacherId ?? DBNull.Value); command.Parameters.AddWithValue("@priority", profile.RegularTeacherPriority); command.Parameters.AddWithValue("@one", profile.OneToOneRequired); command.Parameters.AddWithValue("@note", profile.Note); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegularLessonProfile>> GetRegularLessonsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection=await OpenAsync(projectPath,cancellationToken);await EnsureSchemaAsync(connection,cancellationToken);await using var command=connection.CreateCommand();command.CommandText="SELECT Id,StudentId,SubjectId,RegularTeacherId,RegularTeacherPriority,OneToOneRequired,Note FROM RegularLessonProfile WHERE ProjectId=1 ORDER BY StudentId,SubjectId;";var result=new List<RegularLessonProfile>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))result.Add(new RegularLessonProfile(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.IsDBNull(3)?null:reader.GetInt64(3),reader.GetInt32(4),reader.GetBoolean(5),reader.GetString(6)));return result;
    }

    public async Task SaveLessonRequestAsync(string projectPath, LessonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority,PreferredTeacher1Id,PreferredTeacher2Id,PreferredTeacher3Id,OneToOneRequired,MaxConsecutiveSlotsOverride,AllowGapOverride,Note)
            VALUES(1,@student,@subject,@sessions,@regular,@priority,@preferred1,@preferred2,@preferred3,@one,@maximum,@gap,@note)
            ON CONFLICT(ProjectId,StudentId,SubjectId) DO UPDATE SET RequiredSessions=excluded.RequiredSessions,RegularTeacherId=excluded.RegularTeacherId,RegularTeacherPriority=excluded.RegularTeacherPriority,PreferredTeacher1Id=excluded.PreferredTeacher1Id,PreferredTeacher2Id=excluded.PreferredTeacher2Id,PreferredTeacher3Id=excluded.PreferredTeacher3Id,OneToOneRequired=excluded.OneToOneRequired,MaxConsecutiveSlotsOverride=excluded.MaxConsecutiveSlotsOverride,AllowGapOverride=excluded.AllowGapOverride,Note=excluded.Note;
            """;
        command.Parameters.AddWithValue("@student", request.StudentId); command.Parameters.AddWithValue("@subject", request.SubjectId); command.Parameters.AddWithValue("@sessions", request.RequiredSessions);
        command.Parameters.AddWithValue("@regular", (object?)request.RegularTeacherId ?? DBNull.Value); command.Parameters.AddWithValue("@priority", request.RegularTeacherPriority);
        command.Parameters.AddWithValue("@preferred1", (object?)request.PreferredTeacher1Id ?? DBNull.Value); command.Parameters.AddWithValue("@preferred2", (object?)request.PreferredTeacher2Id ?? DBNull.Value); command.Parameters.AddWithValue("@preferred3", (object?)request.PreferredTeacher3Id ?? DBNull.Value);
        command.Parameters.AddWithValue("@one", request.OneToOneRequired); command.Parameters.AddWithValue("@maximum", (object?)request.MaxConsecutiveSlotsOverride ?? DBNull.Value); command.Parameters.AddWithValue("@gap", (object?)request.AllowGapOverride ?? DBNull.Value); command.Parameters.AddWithValue("@note", request.Note);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LessonRequest>> GetLessonRequestsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority,PreferredTeacher1Id,PreferredTeacher2Id,PreferredTeacher3Id,OneToOneRequired,MaxConsecutiveSlotsOverride,AllowGapOverride,Note FROM LessonRequest WHERE ProjectId=1 ORDER BY StudentId,SubjectId;";
        var result = new List<LessonRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new LessonRequest(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetBoolean(11), reader.GetString(12)));
        return result;
    }

    public async Task DeleteLessonRequestAsync(string projectPath, long studentId, long subjectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM LessonRequest WHERE ProjectId=1 AND StudentId=@student AND SubjectId=@subject;";
        command.Parameters.AddWithValue("@student", studentId); command.Parameters.AddWithValue("@subject", subjectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> SaveAsync(SqliteConnection connection, string table, long id, string columns, string values, string updates, Action<SqliteCommand> bind, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = id == 0 ? $"INSERT INTO {table} ({columns}) VALUES ({values}); SELECT last_insert_rowid();" : $"UPDATE {table} SET {updates} WHERE Id=@id; SELECT changes();";
        bind(command); if (id != 0) command.Parameters.AddWithValue("@id", id);
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(token));
        if (id != 0 && value != 1) throw new InvalidOperationException("更新対象が見つかりません。");
        return id == 0 ? value : id;
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync(token); return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        await SqliteProjectSchema.EnsureCurrentAsync(connection, token).ConfigureAwait(false);
    }
}
