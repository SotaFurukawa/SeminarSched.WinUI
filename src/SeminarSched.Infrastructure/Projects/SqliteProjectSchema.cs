using Microsoft.Data.Sqlite;

namespace SeminarSched.Infrastructure.Projects;

internal static class SqliteProjectSchema
{
    internal const string ProductMarker = "SeminarSched.WinUI";
    internal const int CurrentVersion = 2;

    internal static async Task EnsureCurrentAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        var version = await ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"対応していないproject schema versionです: {version}");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // GroupLessonSessionは同日中に「TimeSlot参照」から「自由入力のStartTime/EndTime」へ設計変更した
        // （集団授業のクラス管理機能自体が同一開発サイクル内の未リリース機能で実データが無いため、
        // 通常のAddColumnIfMissingAsyncによる追加ではなく一度DROPして作り直す一回限りの対応）。
        await DropTableIfHasColumnAsync(connection, (SqliteTransaction)transaction, "GroupLessonSession", "TimeSlotId", cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = CompleteSchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnsAsync(connection, (SqliteTransaction)transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DropTableIfHasColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string obsoleteColumn,
        CancellationToken cancellationToken)
    {
        await using var info = connection.CreateCommand();
        info.Transaction = transaction;
        info.CommandText = $"PRAGMA table_info({table});";
        var hasObsoleteColumn = false;
        await using (var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), obsoleteColumn, StringComparison.OrdinalIgnoreCase))
                {
                    hasObsoleteColumn = true;
                    break;
                }
            }
        }
        if (!hasObsoleteColumn) return;

        await using var drop = connection.CreateCommand();
        drop.Transaction = transaction;
        drop.CommandText = $"DROP TABLE {table};";
        await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> ReadVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SchemaVersion FROM ApplicationMetadata WHERE Id=1 AND Product=$product;";
        command.Parameters.AddWithValue("$product", ProductMarker);
        object? value;
        try
        {
            value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // Not every .jukuschedule-shaped file is one of ours: the Python reference app's own
            // files (and any unrelated/corrupt SQLite file) predate this table entirely.
            throw new InvalidDataException("このファイルはSeminarSched.WinUIのprojectファイルではありません。Python版projectのcopy-importは未実装です。");
        }
        if (value is null or DBNull)
        {
            throw new InvalidDataException("現在のWinUI projectではありません。");
        }

        return Convert.ToInt32(value);
    }

    internal static async Task MigrateV1ToV2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        var version = await ReadVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version == CurrentVersion)
        {
            await EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (version != 1)
        {
            throw new InvalidDataException($"対応していないproject schema versionです: {version}");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = CompleteSchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureColumnsAsync(connection, (SqliteTransaction)transaction, cancellationToken).ConfigureAwait(false);
        command.CommandText = "UPDATE ApplicationMetadata SET SchemaVersion=$version WHERE Id=1; PRAGMA user_version=2;";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$version", CurrentVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "RegularTeacherId", "INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "RegularTeacherPriority", "INTEGER NOT NULL DEFAULT 1 CHECK(RegularTeacherPriority BETWEEN 1 AND 5)", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "PreferredTeacher1Id", "INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "PreferredTeacher2Id", "INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "PreferredTeacher3Id", "INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "OneToOneRequired", "INTEGER NOT NULL DEFAULT 0 CHECK(OneToOneRequired IN(0,1))", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "MaxConsecutiveSlotsOverride", "INTEGER CHECK(MaxConsecutiveSlotsOverride IS NULL OR MaxConsecutiveSlotsOverride>0)", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "AllowGapOverride", "INTEGER CHECK(AllowGapOverride IS NULL OR AllowGapOverride IN(0,1))", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "LessonRequest", "Note", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await AddColumnIfMissingAsync(connection, transaction, "Assignment", "SessionIndex", "INTEGER NOT NULL DEFAULT 1 CHECK(SessionIndex>0)", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "Assignment", "OptimizationRunId", "INTEGER REFERENCES OptimizationRun(Id) ON DELETE SET NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "Assignment", "IsManual", "INTEGER NOT NULL DEFAULT 0 CHECK(IsManual IN(0,1))", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "Assignment", "Note", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        // ホーム画面「新しい講習プロジェクト」の「集団授業の日程を考慮する」チェックボックス（既定オフ）。
        // オンの場合のみ③アンケート取込みに3.1/3.2（集団授業クラスの登録・受講登録）を表示する。
        await AddColumnIfMissingAsync(connection, transaction, "CourseProject", "ConsiderGroupLessons", "INTEGER NOT NULL DEFAULT 0 CHECK(ConsiderGroupLessons IN(0,1))", cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var info = connection.CreateCommand();
        info.Transaction = transaction;
        info.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        await using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string CompleteSchemaSql = """
        CREATE TABLE IF NOT EXISTS LessonRequest (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE RESTRICT,
            SubjectId INTEGER NOT NULL REFERENCES Subject(Id) ON DELETE RESTRICT,
            RequiredSessions INTEGER NOT NULL CHECK(RequiredSessions>0),
            RegularTeacherId INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
            RegularTeacherPriority INTEGER NOT NULL DEFAULT 1 CHECK(RegularTeacherPriority BETWEEN 1 AND 5),
            PreferredTeacher1Id INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
            PreferredTeacher2Id INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
            PreferredTeacher3Id INTEGER REFERENCES Teacher(Id) ON DELETE SET NULL,
            OneToOneRequired INTEGER NOT NULL DEFAULT 0 CHECK(OneToOneRequired IN(0,1)),
            MaxConsecutiveSlotsOverride INTEGER CHECK(MaxConsecutiveSlotsOverride IS NULL OR MaxConsecutiveSlotsOverride>0),
            AllowGapOverride INTEGER CHECK(AllowGapOverride IS NULL OR AllowGapOverride IN(0,1)),
            Note TEXT NOT NULL DEFAULT '',
            UNIQUE(ProjectId,StudentId,SubjectId)
        );
        CREATE TABLE IF NOT EXISTS TeacherUnavailability (
            TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE CASCADE,
            OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,
            TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE CASCADE,
            PRIMARY KEY(TeacherId,OpenDateId,TimeSlotId)
        );
        CREATE TABLE IF NOT EXISTS StudentAvailability (
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE,
            OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,
            TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE CASCADE,
            AvailabilityLevel INTEGER NOT NULL CHECK(AvailabilityLevel BETWEEN 0 AND 2),
            PRIMARY KEY(ProjectId,StudentId,OpenDateId,TimeSlotId)
        );
        CREATE TABLE IF NOT EXISTS TeacherAvailability (
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE CASCADE,
            OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,
            TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE CASCADE,
            AvailabilityLevel INTEGER NOT NULL CHECK(AvailabilityLevel BETWEEN 0 AND 2),
            PRIMARY KEY(ProjectId,TeacherId,OpenDateId,TimeSlotId)
        );
        CREATE TABLE IF NOT EXISTS ImportBatch (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            ImportType TEXT NOT NULL,
            SourceFileName TEXT NOT NULL,
            ImportedUtc TEXT NOT NULL,
            RowCount INTEGER NOT NULL CHECK(RowCount>=0),
            SuccessCount INTEGER NOT NULL CHECK(SuccessCount>=0),
            WarningCount INTEGER NOT NULL CHECK(WarningCount>=0),
            ErrorCount INTEGER NOT NULL CHECK(ErrorCount>=0),
            MappingJson TEXT NOT NULL DEFAULT '{}'
        );
        CREATE TABLE IF NOT EXISTS ImportSourceSnapshot (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            ImportType TEXT NOT NULL,
            SourceFileName TEXT NOT NULL,
            Content BLOB NOT NULL,
            Sha256 TEXT NOT NULL,
            SizeBytes INTEGER NOT NULL CHECK(SizeBytes>=0),
            ImportedUtc TEXT NOT NULL,
            UNIQUE(ProjectId,ImportType)
        );
        CREATE TABLE IF NOT EXISTS ValidationIssue (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            Severity TEXT NOT NULL CHECK(Severity IN('error','warning','info')),
            IssueType TEXT NOT NULL,
            EntityType TEXT NOT NULL,
            EntityId TEXT,
            Message TEXT NOT NULL,
            DetailsJson TEXT NOT NULL DEFAULT '{}',
            Resolved INTEGER NOT NULL DEFAULT 0 CHECK(Resolved IN(0,1)),
            CreatedUtc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS AuditLog (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            TimestampUtc TEXT NOT NULL,
            Action TEXT NOT NULL,
            EntityType TEXT NOT NULL,
            EntityId TEXT NOT NULL,
            BeforeJson TEXT,
            AfterJson TEXT,
            Reason TEXT,
            Source TEXT NOT NULL CHECK(Source IN('system','manual','automatic','undo','redo','import')),
            OperationId TEXT
        );
        CREATE TABLE IF NOT EXISTS OptimizationRun (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            StartedUtc TEXT NOT NULL,
            FinishedUtc TEXT,
            Status TEXT NOT NULL CHECK(Status IN('running','completed','cancelled','failed')),
            SolverStatus TEXT NOT NULL DEFAULT 'UNKNOWN',
            QualityLevel INTEGER NOT NULL DEFAULT 3 CHECK(QualityLevel BETWEEN 1 AND 5),
            TimeLimitSeconds INTEGER NOT NULL CHECK(TimeLimitSeconds>0),
            ObjectiveSummaryJson TEXT NOT NULL DEFAULT '{}',
            UnassignedCount INTEGER NOT NULL DEFAULT 0 CHECK(UnassignedCount>=0),
            WarningCount INTEGER NOT NULL DEFAULT 0 CHECK(WarningCount>=0),
            InputSnapshotJson TEXT NOT NULL DEFAULT '{}',
            ResultSnapshotJson TEXT NOT NULL DEFAULT '{}',
            RandomSeed INTEGER NOT NULL DEFAULT 0,
            ElapsedSeconds REAL
        );
        CREATE TABLE IF NOT EXISTS Assignment (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            LessonRequestId INTEGER NOT NULL REFERENCES LessonRequest(Id) ON DELETE CASCADE,
            TeacherId INTEGER NOT NULL REFERENCES Teacher(Id) ON DELETE RESTRICT,
            OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE RESTRICT,
            TimeSlotId INTEGER NOT NULL REFERENCES TimeSlot(Id) ON DELETE RESTRICT,
            IsLocked INTEGER NOT NULL DEFAULT 0 CHECK(IsLocked IN(0,1)),
            Source TEXT NOT NULL,
            SessionIndex INTEGER NOT NULL DEFAULT 1 CHECK(SessionIndex>0),
            OptimizationRunId INTEGER REFERENCES OptimizationRun(Id) ON DELETE SET NULL,
            IsManual INTEGER NOT NULL DEFAULT 0 CHECK(IsManual IN(0,1)),
            Note TEXT NOT NULL DEFAULT '',
            UNIQUE(LessonRequestId,OpenDateId,TimeSlotId)
        );
        CREATE TABLE IF NOT EXISTS OutputSetting (
            ProjectId INTEGER PRIMARY KEY REFERENCES CourseProject(Id) ON DELETE CASCADE,
            PaperSize TEXT NOT NULL DEFAULT 'A4',
            Orientation TEXT NOT NULL DEFAULT 'landscape',
            VisibleFieldsJson TEXT NOT NULL DEFAULT '{}',
            DaysPerPage INTEGER NOT NULL DEFAULT 7 CHECK(DaysPerPage BETWEEN 1 AND 7),
            TeacherColumnsPerPage INTEGER NOT NULL DEFAULT 10 CHECK(TeacherColumnsPerPage BETWEEN 1 AND 20),
            FontSize REAL NOT NULL DEFAULT 9 CHECK(FontSize BETWEEN 5 AND 18),
            MarginMm REAL NOT NULL DEFAULT 8 CHECK(MarginMm BETWEEN 0 AND 30),
            FileNamePattern TEXT NOT NULL DEFAULT '{title}_{kind}',
            DefaultOutputDirectory TEXT,
            StudentPageMode TEXT NOT NULL DEFAULT 'one_per_page',
            CsvWithBom INTEGER NOT NULL DEFAULT 1 CHECK(CsvWithBom IN(0,1)),
            StyleRulesJson TEXT NOT NULL DEFAULT '{}'
        );
        CREATE TABLE IF NOT EXISTS GroupLessonClass (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProjectId INTEGER NOT NULL REFERENCES CourseProject(Id) ON DELETE CASCADE,
            Name TEXT NOT NULL CHECK(length(trim(Name))>0),
            Grade TEXT NOT NULL CHECK(length(trim(Grade))>0),
            AllowOtherGrades INTEGER NOT NULL DEFAULT 0 CHECK(AllowOtherGrades IN(0,1)),
            Active INTEGER NOT NULL DEFAULT 1 CHECK(Active IN(0,1)),
            UNIQUE(ProjectId,Name)
        );
        CREATE TABLE IF NOT EXISTS GroupLessonSession (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ClassId INTEGER NOT NULL REFERENCES GroupLessonClass(Id) ON DELETE CASCADE,
            OpenDateId INTEGER NOT NULL REFERENCES OpenDate(Id) ON DELETE CASCADE,
            StartTime TEXT NOT NULL,
            EndTime TEXT NOT NULL CHECK(EndTime>StartTime),
            UNIQUE(ClassId,OpenDateId,StartTime,EndTime)
        );
        CREATE TABLE IF NOT EXISTS GroupLessonEnrollment (
            ClassId INTEGER NOT NULL REFERENCES GroupLessonClass(Id) ON DELETE CASCADE,
            StudentId INTEGER NOT NULL REFERENCES Student(Id) ON DELETE CASCADE,
            PRIMARY KEY(ClassId,StudentId)
        );
        CREATE INDEX IF NOT EXISTS IX_AuditLog_Project_Timestamp ON AuditLog(ProjectId,TimestampUtc);
        CREATE INDEX IF NOT EXISTS IX_ValidationIssue_Project_Resolved ON ValidationIssue(ProjectId,Resolved,Severity);
        CREATE INDEX IF NOT EXISTS IX_OptimizationRun_Project_Started ON OptimizationRun(ProjectId,StartedUtc);
        """;
}
