using Microsoft.Data.Sqlite;
using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;

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

    public async Task SaveRegularLessonAsync(string projectPath, RegularLessonProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile); await using var connection = await OpenAsync(projectPath, cancellationToken); await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO RegularLessonProfile (ProjectId, StudentId, SubjectId, RegularTeacherId, RegularTeacherPriority, OneToOneRequired, Note) VALUES (1, @student, @subject, @teacher, @priority, @one, @note) ON CONFLICT(ProjectId, StudentId, SubjectId) DO UPDATE SET RegularTeacherId=excluded.RegularTeacherId, RegularTeacherPriority=excluded.RegularTeacherPriority, OneToOneRequired=excluded.OneToOneRequired, Note=excluded.Note;";
        command.Parameters.AddWithValue("@student", profile.StudentId); command.Parameters.AddWithValue("@subject", profile.SubjectId); command.Parameters.AddWithValue("@teacher", (object?)profile.RegularTeacherId ?? DBNull.Value); command.Parameters.AddWithValue("@priority", profile.RegularTeacherPriority); command.Parameters.AddWithValue("@one", profile.OneToOneRequired); command.Parameters.AddWithValue("@note", profile.Note); await command.ExecuteNonQueryAsync(cancellationToken);
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
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM ApplicationMetadata WHERE Id=1 AND Product='SeminarSched.WinUI' AND SchemaVersion=1;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 1) throw new InvalidDataException("現在のWinUI projectではありません。");
        // v0.1.0 development databases created before master-data support are upgraded additively.
        command.CommandText = MasterSchemaSql; await command.ExecuteNonQueryAsync(token);
    }

    private const string MasterSchemaSql = """
        CREATE TABLE IF NOT EXISTS Student (Id INTEGER PRIMARY KEY AUTOINCREMENT, ExternalId TEXT NOT NULL UNIQUE CHECK(length(trim(ExternalId))>0), Name TEXT NOT NULL CHECK(length(trim(Name))>0), Grade TEXT NOT NULL, DefaultMaxConsecutiveSlots INTEGER NOT NULL DEFAULT 2 CHECK(DefaultMaxConsecutiveSlots>0), AllowGap INTEGER NOT NULL DEFAULT 0 CHECK(AllowGap IN(0,1)), Note TEXT NOT NULL DEFAULT '', Active INTEGER NOT NULL DEFAULT 1 CHECK(Active IN(0,1)));
        CREATE TABLE IF NOT EXISTS Teacher (Id INTEGER PRIMARY KEY AUTOINCREMENT, ExternalId TEXT NOT NULL UNIQUE CHECK(length(trim(ExternalId))>0), Name TEXT NOT NULL CHECK(length(trim(Name))>0), AllowGap INTEGER NOT NULL DEFAULT 0 CHECK(AllowGap IN(0,1)), Note TEXT NOT NULL DEFAULT '', Active INTEGER NOT NULL DEFAULT 1 CHECK(Active IN(0,1)));
        CREATE TABLE IF NOT EXISTS Subject (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL UNIQUE CHECK(length(trim(Code))>0), DisplayName TEXT NOT NULL CHECK(length(trim(DisplayName))>0), ShortName TEXT NOT NULL DEFAULT '', SchoolLevel TEXT NOT NULL CHECK(length(trim(SchoolLevel))>0), SortOrder INTEGER NOT NULL CHECK(SortOrder>=1), Active INTEGER NOT NULL DEFAULT 1 CHECK(Active IN(0,1)));
        CREATE TABLE IF NOT EXISTS TeacherQualification (TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE CASCADE, SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE, CanTeach INTEGER NOT NULL DEFAULT 0 CHECK(CanTeach IN(0,1)), Note TEXT NOT NULL DEFAULT '', PRIMARY KEY(TeacherId,SubjectId));
        CREATE TABLE IF NOT EXISTS RegularLessonProfile (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE, StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE, SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE, RegularTeacherId INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL, RegularTeacherPriority INTEGER NOT NULL DEFAULT 3 CHECK(RegularTeacherPriority BETWEEN 1 AND 5), OneToOneRequired INTEGER NOT NULL DEFAULT 0 CHECK(OneToOneRequired IN(0,1)), Note TEXT NOT NULL DEFAULT '', UNIQUE(ProjectId,StudentId,SubjectId));
        """;
}
