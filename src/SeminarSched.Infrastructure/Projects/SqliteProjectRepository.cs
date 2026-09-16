using System.Globalization;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Projects;
using SeminarSched.Domain.Projects;

namespace SeminarSched.Infrastructure.Projects;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private const int CurrentSchemaVersion = 1;
    private const string ProductMarker = "SeminarSched.WinUI";

    public async Task<ProjectSummary> CreateAsync(
        string path,
        CourseProjectDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(definition);

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The project path has no parent directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(target))
        {
            throw new IOException("The target project already exists.");
        }

        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await CreateDatabaseAsync(temporary, definition, cancellationToken).ConfigureAwait(false);
            var integrity = await CheckIntegrityAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (!integrity.IsValid)
            {
                throw new InvalidDataException(integrity.Message);
            }

            File.Move(temporary, target, overwrite: false);
            return await OpenAsync(target, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task<ProjectSummary> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var connection = CreateConnection(fullPath, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cp.Title, cp.AcademicYear, cp.Season, cp.StartDate, cp.EndDate, cp.WorkflowCompletedStep
            FROM CourseProject AS cp
            INNER JOIN ApplicationMetadata AS am ON am.Id = 1
            WHERE cp.Id = 1 AND am.Product = $product AND am.SchemaVersion = $schemaVersion;
            """;
        command.Parameters.AddWithValue("$product", ProductMarker);
        command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "このファイルは現在のWinUI版プロジェクトではありません。Python版projectのcopy-importは未実装です。");
        }

        return new ProjectSummary(
            fullPath,
            reader.GetString(0),
            reader.GetInt32(1),
            (CourseSeason)reader.GetInt32(2),
            DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetInt32(5));
    }

    public async Task<ProjectIntegrityResult> CheckIntegrityAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection(Path.GetFullPath(path), SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
                ? ProjectIntegrityResult.Valid
                : new ProjectIntegrityResult(false, $"SQLite integrity check failed: {result}");
        }
        catch (SqliteException exception)
        {
            return new ProjectIntegrityResult(false, $"SQLite file could not be read: {exception.SqliteErrorCode}");
        }
    }

    public async Task CreateBackupAsync(
        string sourcePath,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var target = Path.GetFullPath(backupPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The source project does not exist.", source);
        }

        if (File.Exists(target))
        {
            throw new IOException("The backup target already exists.");
        }

        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The backup path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await BackupDatabaseAsync(source, temporary, cancellationToken).ConfigureAwait(false);
            var integrity = await CheckIntegrityAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (!integrity.IsValid)
            {
                throw new InvalidDataException(integrity.Message);
            }

            await OpenAsync(temporary, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task RestoreBackupAsync(
        string backupPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(backupPath);
        var target = Path.GetFullPath(targetPath);
        await OpenAsync(source, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The target path has no parent directory.");
        var baseName = Path.GetFileNameWithoutExtension(target);
        var safetyBackup = Path.Combine(
            directory,
            $"{baseName}_before_restore_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}{Path.GetExtension(target)}");
        await CreateBackupAsync(target, safetyBackup, cancellationToken).ConfigureAwait(false);
        var replacement = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.restore.tmp");
        var rollback = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.rollback");
        try
        {
            await BackupDatabaseAsync(source, replacement, cancellationToken).ConfigureAwait(false);
            var integrity = await CheckIntegrityAsync(replacement, cancellationToken).ConfigureAwait(false);
            if (!integrity.IsValid)
            {
                throw new InvalidDataException(integrity.Message);
            }

            File.Replace(replacement, target, rollback, ignoreMetadataErrors: true);
            var restored = await CheckIntegrityAsync(target, cancellationToken).ConfigureAwait(false);
            if (!restored.IsValid)
            {
                File.Replace(rollback, target, null, ignoreMetadataErrors: true);
                throw new InvalidDataException("Restored project failed validation; the original was restored.");
            }
        }
        finally
        {
            if (File.Exists(replacement)) File.Delete(replacement);
            if (File.Exists(rollback)) File.Delete(rollback);
        }

        PruneRestoreBackups(directory, baseName, Path.GetExtension(target), keep: 3);
    }

    private static void PruneRestoreBackups(string directory, string baseName, string extension, int keep)
    {
        var pattern = $"{baseName}_before_restore_*{extension}";
        foreach (var obsolete in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(keep))
        {
            File.Delete(obsolete);
        }
    }

    private static async Task BackupDatabaseAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using var source = CreateConnection(sourcePath, SqliteOpenMode.ReadOnly);
        await using var target = CreateConnection(targetPath, SqliteOpenMode.ReadWriteCreate);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await target.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(target);
    }

    private static async Task CreateDatabaseAsync(
        string path,
        CourseProjectDefinition definition,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(path, SqliteOpenMode.ReadWriteCreate);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var schema = connection.CreateCommand())
        {
            schema.Transaction = (SqliteTransaction)transaction;
            schema.CommandText = """
                CREATE TABLE ApplicationMetadata (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Product TEXT NOT NULL,
                    SchemaVersion INTEGER NOT NULL,
                    CreatedUtc TEXT NOT NULL
                );
                CREATE TABLE CourseProject (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Title TEXT NOT NULL,
                    AcademicYear INTEGER NOT NULL,
                    Season INTEGER NOT NULL,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT NOT NULL,
                    WorkflowCompletedStep INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE OpenDate (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
                    Date TEXT NOT NULL UNIQUE,
                    IsOpen INTEGER NOT NULL DEFAULT 1 CHECK (IsOpen IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE TimeSlot (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE CHECK (length(trim(Code)) > 0),
                    DisplayName TEXT NOT NULL CHECK (length(trim(DisplayName)) > 0),
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 1),
                    Active INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0, 1))
                );
                CREATE TABLE OpenDateTimeSlot (
                    OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,
                    TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE CASCADE,
                    PRIMARY KEY (OpenDateId, TimeSlotId)
                );
                CREATE TABLE Student (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExternalId TEXT NOT NULL UNIQUE CHECK (length(trim(ExternalId)) > 0),
                    Name TEXT NOT NULL CHECK (length(trim(Name)) > 0),
                    Grade TEXT NOT NULL,
                    DefaultMaxConsecutiveSlots INTEGER NOT NULL DEFAULT 2 CHECK (DefaultMaxConsecutiveSlots > 0),
                    AllowGap INTEGER NOT NULL DEFAULT 0 CHECK (AllowGap IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT '',
                    Active INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0, 1))
                );
                CREATE TABLE Teacher (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExternalId TEXT NOT NULL UNIQUE CHECK (length(trim(ExternalId)) > 0),
                    Name TEXT NOT NULL CHECK (length(trim(Name)) > 0),
                    AllowGap INTEGER NOT NULL DEFAULT 0 CHECK (AllowGap IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT '',
                    Active INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0, 1))
                );
                CREATE TABLE Subject (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE CHECK (length(trim(Code)) > 0),
                    DisplayName TEXT NOT NULL CHECK (length(trim(DisplayName)) > 0),
                    ShortName TEXT NOT NULL DEFAULT '',
                    SchoolLevel TEXT NOT NULL CHECK (length(trim(SchoolLevel)) > 0),
                    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 1),
                    Active INTEGER NOT NULL DEFAULT 1 CHECK (Active IN (0, 1))
                );
                CREATE TABLE TeacherQualification (
                    TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE CASCADE,
                    SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE,
                    CanTeach INTEGER NOT NULL DEFAULT 0 CHECK (CanTeach IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY (TeacherId, SubjectId)
                );
                CREATE TABLE RegularLessonProfile (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
                    StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE,
                    SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE CASCADE,
                    RegularTeacherId INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
                    RegularTeacherPriority INTEGER NOT NULL DEFAULT 3 CHECK (RegularTeacherPriority BETWEEN 1 AND 5),
                    OneToOneRequired INTEGER NOT NULL DEFAULT 0 CHECK (OneToOneRequired IN (0, 1)),
                    Note TEXT NOT NULL DEFAULT '',
                    UNIQUE (ProjectId, StudentId, SubjectId)
                );
                PRAGMA user_version = 1;
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = (SqliteTransaction)transaction;
            metadata.CommandText = """
                INSERT INTO ApplicationMetadata (Id, Product, SchemaVersion, CreatedUtc)
                VALUES (1, $product, $schemaVersion, $createdUtc);
                INSERT INTO CourseProject (Id, Title, AcademicYear, Season, StartDate, EndDate)
                VALUES (1, $title, $year, $season, $startDate, $endDate);
                """;
            metadata.Parameters.AddWithValue("$product", ProductMarker);
            metadata.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);
            metadata.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$title", definition.Title);
            metadata.Parameters.AddWithValue("$year", definition.AcademicYear);
            metadata.Parameters.AddWithValue("$season", (int)definition.Season);
            metadata.Parameters.AddWithValue("$startDate", definition.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            metadata.Parameters.AddWithValue("$endDate", definition.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await metadata.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var date = definition.StartDate; date <= definition.EndDate; date = date.AddDays(1))
        {
            await using var openDate = connection.CreateCommand();
            openDate.Transaction = (SqliteTransaction)transaction;
            openDate.CommandText = "INSERT INTO OpenDate (ProjectId, Date) VALUES (1, $date);";
            openDate.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await openDate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SqliteConnection CreateConnection(string path, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        };
        return new SqliteConnection(builder.ToString());
    }
}
