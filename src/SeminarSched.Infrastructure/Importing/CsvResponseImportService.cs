using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using SeminarSched.Application.Importing;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Importing;

public sealed class CsvResponseImportService : IResponseImportService
{
    private static readonly string[] PreferredTeacherHeaders = ["第1希望講師ID", "第2希望講師ID", "第3希望講師ID"];

    public Task<ResponseImportPreview> PreviewAsync(string projectPath, string studentFilePath, string teacherFilePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Preview(projectPath, studentFilePath, teacherFilePath), cancellationToken);

    public async Task ApplyAsync(string projectPath, ResponseImportPreview preview, bool removeUnlistedAvailability = false, CancellationToken cancellationToken = default)
    {
        if (!preview.CanApply) throw new InvalidOperationException("エラーがあるため反映できません。");
        var current = Preview(projectPath, preview.StudentFilePath, preview.TeacherFilePath);
        if (!current.CanApply || current.StudentFileSha256 != preview.StudentFileSha256 || current.TeacherFileSha256 != preview.TeacherFileSha256)
            throw new InvalidOperationException("検証後にファイルまたはprojectが変更されました。再検証してください。");

        var students = Read(preview.StudentFilePath);
        var teachers = Read(preview.TeacherFilePath);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ApplyStudentRowsAsync(connection, (SqliteTransaction)transaction, students, cancellationToken).ConfigureAwait(false);
        await ApplyTeacherRowsAsync(connection, (SqliteTransaction)transaction, teachers, cancellationToken).ConfigureAwait(false);
        var removedStudentDates = 0; var removedTeacherDates = 0;
        if (removeUnlistedAvailability)
        {
            removedStudentDates = await RemoveUnlistedAvailabilityAsync(connection, (SqliteTransaction)transaction, "Student", "StudentAvailability", "StudentId", preview.Diff.StudentRemovalCandidates, cancellationToken).ConfigureAwait(false);
            removedTeacherDates = await RemoveUnlistedAvailabilityAsync(connection, (SqliteTransaction)transaction, "Teacher", "TeacherAvailability", "TeacherId", preview.Diff.TeacherRemovalCandidates, cancellationToken).ConfigureAwait(false);
        }
        await SaveImportEvidenceAsync(connection, (SqliteTransaction)transaction, "student_availability", preview.StudentFilePath, preview.StudentFileSha256, students.Rows.Count, cancellationToken).ConfigureAwait(false);
        await SaveImportEvidenceAsync(connection, (SqliteTransaction)transaction, "teacher_availability", preview.TeacherFilePath, preview.TeacherFileSha256, teachers.Rows.Count, cancellationToken).ConfigureAwait(false);

        await using var audit = connection.CreateCommand();
        audit.Transaction = (SqliteTransaction)transaction;
        audit.CommandText = """
            INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Source)
            VALUES(1,$now,'availability_imported','response_import','student_and_teacher',$after,'import');
            """;
        audit.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        audit.Parameters.AddWithValue("$after", JsonSerializer.Serialize(new
        {
            StudentRows = students.Rows.Count,
            TeacherRows = teachers.Rows.Count,
            StudentSha256 = preview.StudentFileSha256,
            TeacherSha256 = preview.TeacherFileSha256,
            RemovedStudentDates = removedStudentDates,
            RemovedTeacherDates = removedTeacherDates,
        }));
        await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RemoveUnlistedAvailabilityAsync(SqliteConnection connection, SqliteTransaction transaction, string entityTable, string availabilityTable, string entityColumn, IReadOnlyList<AvailabilityDiffKey> removals, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var removal in removals)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM {availabilityTable}
                WHERE {entityColumn}=(SELECT Id FROM {entityTable} WHERE ExternalId=$external)
                  AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date=$date);
                """;
            command.Parameters.AddWithValue("$external", removal.ExternalId);
            command.Parameters.AddWithValue("$date", removal.Date);
            removed += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0 ? 1 : 0;
        }
        return removed;
    }

    private static ResponseImportPreview Preview(string projectPath, string studentPath, string teacherPath)
    {
        var issues = new List<ImportIssue>();
        var students = ReadOrIssue(studentPath, "生徒回答", issues);
        var teachers = ReadOrIssue(teacherPath, "講師回答", issues);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(projectPath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        ValidateStudentRows(connection, students, issues);
        ValidateTeacherRows(connection, teachers, issues);
        var slotCodes = ActiveSlotCodes(connection);
        var diff = issues.Any(issue => issue.IsError) ? ResponseImportDiff.Empty : ComputeDiff(connection, students, teachers, slotCodes);
        return new(studentPath, teacherPath, HashIfPresent(studentPath), HashIfPresent(teacherPath), students.Rows.Count, teachers.Rows.Count, issues, diff);
    }

    private static ResponseImportDiff ComputeDiff(SqliteConnection connection, TabularData students, TabularData teachers, IReadOnlyList<string> slotCodes)
    {
        var (studentAdded, studentChanged, studentUnchanged, studentRemovals) = students.Has("日付")
            ? ComputeAvailabilityDiff(connection, students, "生徒ID", "Student", "StudentAvailability", "StudentId", slotCodes)
            : (0, 0, 0, (IReadOnlyList<AvailabilityDiffKey>)[]);
        var (teacherAdded, teacherChanged, teacherUnchanged, teacherRemovals) = teachers.Has("日付")
            ? ComputeAvailabilityDiff(connection, teachers, "講師ID", "Teacher", "TeacherAvailability", "TeacherId", slotCodes)
            : (0, 0, 0, (IReadOnlyList<AvailabilityDiffKey>)[]);
        return new(studentAdded, studentChanged, studentUnchanged, studentRemovals, teacherAdded, teacherChanged, teacherUnchanged, teacherRemovals);
    }

    private static (int Added, int Changed, int Unchanged, IReadOnlyList<AvailabilityDiffKey> Removals) ComputeAvailabilityDiff(
        SqliteConnection connection, TabularData data, string externalHeader, string entityTable, string availabilityTable, string entityColumn, IReadOnlyList<string> slotCodes)
    {
        var added = 0; var changed = 0; var unchanged = 0;
        var fileDatesByExternalId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in data.Rows)
        {
            var external = data.Value(row, externalHeader);
            if (external.Length == 0 || !TryNormalizeDate(data.Value(row, "日付"), out var date)) continue;
            if (!fileDatesByExternalId.TryGetValue(external, out var dates)) { dates = []; fileDatesByExternalId[external] = dates; }
            dates.Add(date);

            var isNew = true; var isChanged = false;
            foreach (var slotCode in slotCodes)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT a.AvailabilityLevel FROM {availabilityTable} a
                    JOIN {entityTable} e ON e.Id=a.{entityColumn}
                    JOIN OpenDate d ON d.Id=a.OpenDateId
                    JOIN TimeSlot s ON s.Id=a.TimeSlotId
                    WHERE e.ExternalId=$external AND d.Date=$date AND s.Code=$slot;
                    """;
                command.Parameters.AddWithValue("$external", external);
                command.Parameters.AddWithValue("$date", date);
                command.Parameters.AddWithValue("$slot", slotCode);
                var existing = command.ExecuteScalar();
                if (existing is not null) isNew = false;
                var existingLevel = existing is null ? (int?)null : Convert.ToInt32(existing, CultureInfo.InvariantCulture);
                var fileLevel = int.TryParse(data.Value(row, slotCode), out var level) ? level : (int?)null;
                if (existingLevel != fileLevel) isChanged = true;
            }
            if (isNew) added++; else if (isChanged) changed++; else unchanged++;
        }

        var removals = new List<AvailabilityDiffKey>();
        foreach (var (external, fileDates) in fileDatesByExternalId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT DISTINCT d.Date FROM {availabilityTable} a
                JOIN {entityTable} e ON e.Id=a.{entityColumn}
                JOIN OpenDate d ON d.Id=a.OpenDateId
                WHERE e.ExternalId=$external;
                """;
            command.Parameters.AddWithValue("$external", external);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var existingDate = reader.GetString(0);
                if (!fileDates.Contains(existingDate)) removals.Add(new AvailabilityDiffKey(external, existingDate));
            }
        }
        return (added, changed, unchanged, removals);
    }

    private static TabularData ReadOrIssue(string path, string label, List<ImportIssue> issues)
    {
        try { return Read(path); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or MalformedLineException)
        {
            issues.Add(new ImportIssue(0, label, exception.Message));
            return TabularData.Empty;
        }
    }

    private static void ValidateStudentRows(SqliteConnection connection, TabularData data, List<ImportIssue> issues)
    {
        var hasRequests = data.Has("必要回数");
        var hasAvailability = data.Has("日付");
        if (!hasRequests && !hasAvailability) issues.Add(new(1, "生徒回答", "「必要回数」または「日付」列が必要です。"));
        RequireHeaders(data, hasRequests ? ["生徒ID", "科目コード", "必要回数"] : ["生徒ID", "科目コード", "日付"], issues, "生徒回答");
        var slotCodes = ActiveSlotCodes(connection);
        if (hasAvailability) RequireHeaders(data, slotCodes, issues, "生徒回答");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < data.Rows.Count; index++)
        {
            var row = data.Rows[index];
            var line = index + 2;
            var studentId = data.Value(row, "生徒ID");
            var subjectCode = data.Value(row, "科目コード");
            if (!Exists(connection, "Student", "ExternalId", studentId)) issues.Add(new(line, "生徒ID", "登録されていません。"));
            if (!Exists(connection, "Subject", "Code", subjectCode)) issues.Add(new(line, "科目コード", "登録されていません。"));
            if (hasRequests && (!int.TryParse(data.Value(row, "必要回数"), out var count) || count < 1)) issues.Add(new(line, "必要回数", "1以上の整数を指定してください。"));
            var key = hasAvailability ? $"{studentId}|{data.Value(row, "日付")}" : $"{studentId}|{subjectCode}";
            if (!seen.Add(key)) issues.Add(new(line, "重複", "同じ生徒と日付／科目の行が重複しています。"));
            if (hasAvailability) ValidateAvailabilityRow(connection, data, row, line, slotCodes, issues);
            ValidatePreferredTeachers(connection, data, row, line, issues);
        }
    }

    private static void ValidateTeacherRows(SqliteConnection connection, TabularData data, List<ImportIssue> issues)
    {
        var canonical = data.Has("日付");
        RequireHeaders(data, canonical ? ["講師ID", "日付"] : ["講師ID", "勤務不可"], issues, "講師回答");
        var slotCodes = ActiveSlotCodes(connection);
        if (canonical) RequireHeaders(data, slotCodes, issues, "講師回答");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < data.Rows.Count; index++)
        {
            var row = data.Rows[index];
            var line = index + 2;
            var teacherId = data.Value(row, "講師ID");
            if (!Exists(connection, "Teacher", "ExternalId", teacherId)) issues.Add(new(line, "講師ID", "登録されていません。"));
            if (canonical)
            {
                if (!seen.Add($"{teacherId}|{data.Value(row, "日付")}")) issues.Add(new(line, "重複", "同じ講師と日付の行が重複しています。"));
                ValidateAvailabilityRow(connection, data, row, line, slotCodes, issues);
            }
            else foreach (var token in SplitAvailability(data.Value(row, "勤務不可")))
            {
                var parts = token.Split('|');
                if (parts.Length != 2 || !ExistsPair(connection, parts[0], parts[1])) issues.Add(new(line, "勤務不可", $"日付・コマが不正です: {token}"));
            }
        }
    }

    private static void ValidateAvailabilityRow(SqliteConnection connection, TabularData data, string[] row, int line, IReadOnlyList<string> slotCodes, List<ImportIssue> issues)
    {
        if (!TryNormalizeDate(data.Value(row, "日付"), out var date))
        {
            issues.Add(new(line, "日付", "日付はYYYY-MM-DDまたはYYYY/MM/DD形式で入力してください。"));
            return;
        }
        foreach (var slotCode in slotCodes)
        {
            if (!int.TryParse(data.Value(row, slotCode), out var level) || level is < 0 or > 2) issues.Add(new(line, slotCode, "コマ値は0、1、2のいずれかで入力してください。"));
            if (!ExistsPair(connection, date, slotCode)) issues.Add(new(line, slotCode, "この日付で使用できないコマです。"));
        }
    }

    private static void ValidatePreferredTeachers(SqliteConnection connection, TabularData data, string[] row, int line, List<ImportIssue> issues)
    {
        foreach (var header in PreferredTeacherHeaders.Where(data.Has))
        {
            var value = data.Value(row, header);
            if (value.Length > 0 && !Exists(connection, "Teacher", "ExternalId", value)) issues.Add(new(line, header, "登録されていない講師IDです（希望講師は空欄として扱われます）。", IsError: false));
        }
    }

    private static async Task ApplyStudentRowsAsync(SqliteConnection connection, SqliteTransaction transaction, TabularData data, CancellationToken cancellationToken)
    {
        foreach (var row in data.Rows)
        {
            if (data.Has("必要回数"))
            {
                await using var request = connection.CreateCommand();
                request.Transaction = transaction;
                request.CommandText = """
                    INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions)
                    SELECT 1,s.Id,sub.Id,$count FROM Student s,Subject sub WHERE s.ExternalId=$student AND sub.Code=$subject
                    ON CONFLICT(ProjectId,StudentId,SubjectId) DO UPDATE SET RequiredSessions=excluded.RequiredSessions;
                    """;
                request.Parameters.AddWithValue("$student", data.Value(row, "生徒ID"));
                request.Parameters.AddWithValue("$subject", data.Value(row, "科目コード"));
                request.Parameters.AddWithValue("$count", int.Parse(data.Value(row, "必要回数"), CultureInfo.InvariantCulture));
                await request.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            if (data.Has("日付")) await SaveAvailabilityRowAsync(connection, transaction, data, row, true, cancellationToken).ConfigureAwait(false);
            if (PreferredTeacherHeaders.Any(data.Has)) await ApplyPreferencesAsync(connection, transaction, data, row, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ApplyPreferencesAsync(SqliteConnection connection, SqliteTransaction transaction, TabularData data, string[] row, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE LessonRequest SET
              PreferredTeacher1Id=(SELECT Id FROM Teacher WHERE ExternalId=$p1),
              PreferredTeacher2Id=(SELECT Id FROM Teacher WHERE ExternalId=$p2),
              PreferredTeacher3Id=(SELECT Id FROM Teacher WHERE ExternalId=$p3)
            WHERE StudentId=(SELECT Id FROM Student WHERE ExternalId=$student)
              AND SubjectId=(SELECT Id FROM Subject WHERE Code=$subject);
            """;
        command.Parameters.AddWithValue("$student", data.Value(row, "生徒ID"));
        command.Parameters.AddWithValue("$subject", data.Value(row, "科目コード"));
        for (var index = 0; index < PreferredTeacherHeaders.Length; index++) command.Parameters.AddWithValue($"$p{index + 1}", data.Has(PreferredTeacherHeaders[index]) ? data.Value(row, PreferredTeacherHeaders[index]) : string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyTeacherRowsAsync(SqliteConnection connection, SqliteTransaction transaction, TabularData data, CancellationToken cancellationToken)
    {
        if (data.Has("日付"))
        {
            foreach (var row in data.Rows) await SaveAvailabilityRowAsync(connection, transaction, data, row, false, cancellationToken).ConfigureAwait(false);
            return;
        }
        foreach (var row in data.Rows)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=(SELECT Id FROM Teacher WHERE ExternalId=$id);";
            clear.Parameters.AddWithValue("$id", data.Value(row, "講師ID"));
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var token in SplitAvailability(data.Value(row, "勤務不可")))
            {
                var parts = token.Split('|');
                await InsertTeacherUnavailableAsync(connection, transaction, data.Value(row, "講師ID"), parts[0], parts[1], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SaveAvailabilityRowAsync(SqliteConnection connection, SqliteTransaction transaction, TabularData data, string[] row, bool isStudent, CancellationToken cancellationToken)
    {
        var externalHeader = isStudent ? "生徒ID" : "講師ID";
        var entityTable = isStudent ? "Student" : "Teacher";
        var availabilityTable = isStudent ? "StudentAvailability" : "TeacherAvailability";
        var entityColumn = isStudent ? "StudentId" : "TeacherId";
        var date = NormalizeDate(data.Value(row, "日付"));
        foreach (var slotCode in ActiveSlotCodes(connection))
        {
            var level = int.Parse(data.Value(row, slotCode), CultureInfo.InvariantCulture);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {availabilityTable}(ProjectId,{entityColumn},OpenDateId,TimeSlotId,AvailabilityLevel)
                SELECT 1,e.Id,d.Id,s.Id,$level FROM {entityTable} e,OpenDate d,TimeSlot s
                JOIN OpenDateTimeSlot ds ON ds.OpenDateId=d.Id AND ds.TimeSlotId=s.Id
                WHERE e.ExternalId=$external AND d.Date=$date AND s.Code=$slot
                ON CONFLICT(ProjectId,{entityColumn},OpenDateId,TimeSlotId) DO UPDATE SET AvailabilityLevel=excluded.AvailabilityLevel;
                """;
            command.Parameters.AddWithValue("$external", data.Value(row, externalHeader));
            command.Parameters.AddWithValue("$date", date);
            command.Parameters.AddWithValue("$slot", slotCode);
            command.Parameters.AddWithValue("$level", level);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (!isStudent) await SyncTeacherUnavailableAsync(connection, transaction, data.Value(row, externalHeader), date, slotCode, level, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SyncTeacherUnavailableAsync(SqliteConnection connection, SqliteTransaction transaction, string teacher, string date, string slot, int level, CancellationToken cancellationToken)
    {
        await using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=(SELECT Id FROM Teacher WHERE ExternalId=$teacher) AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date=$date) AND TimeSlotId=(SELECT Id FROM TimeSlot WHERE Code=$slot);";
        remove.Parameters.AddWithValue("$teacher", teacher); remove.Parameters.AddWithValue("$date", date); remove.Parameters.AddWithValue("$slot", slot);
        await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (level == 0) await InsertTeacherUnavailableAsync(connection, transaction, teacher, date, slot, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTeacherUnavailableAsync(SqliteConnection connection, SqliteTransaction transaction, string teacher, string date, string slot, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId)
            SELECT t.Id,d.Id,s.Id FROM Teacher t,OpenDate d,TimeSlot s
            JOIN OpenDateTimeSlot ds ON ds.OpenDateId=d.Id AND ds.TimeSlotId=s.Id
            WHERE t.ExternalId=$teacher AND d.Date=$date AND s.Code=$slot;
            """;
        command.Parameters.AddWithValue("$teacher", teacher); command.Parameters.AddWithValue("$date", NormalizeDate(date)); command.Parameters.AddWithValue("$slot", slot);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveImportEvidenceAsync(SqliteConnection connection, SqliteTransaction transaction, string importType, string path, string hash, int rowCount, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ImportBatch(ProjectId,ImportType,SourceFileName,ImportedUtc,RowCount,SuccessCount,WarningCount,ErrorCount,MappingJson)
            VALUES(1,$type,$name,$now,$rows,$rows,0,0,'{}');
            INSERT INTO ImportSourceSnapshot(ProjectId,ImportType,SourceFileName,Content,Sha256,SizeBytes,ImportedUtc)
            VALUES(1,$type,$name,$content,$hash,$size,$now)
            ON CONFLICT(ProjectId,ImportType) DO UPDATE SET SourceFileName=excluded.SourceFileName,Content=excluded.Content,Sha256=excluded.Sha256,SizeBytes=excluded.SizeBytes,ImportedUtc=excluded.ImportedUtc;
            """;
        command.Parameters.AddWithValue("$type", importType); command.Parameters.AddWithValue("$name", Path.GetFileName(path)); command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$rows", rowCount); command.Parameters.AddWithValue("$content", content); command.Parameters.AddWithValue("$hash", hash); command.Parameters.AddWithValue("$size", content.Length);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ActiveSlotCodes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Code FROM TimeSlot WHERE Active=1 ORDER BY SortOrder;";
        using var reader = command.ExecuteReader(); var result = new List<string>(); while (reader.Read()) result.Add(reader.GetString(0)); return result;
    }

    private static bool Exists(SqliteConnection connection, string table, string column, string value)
    {
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE {column}=$value AND Active=1);"; command.Parameters.AddWithValue("$value", value.Trim());
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static bool ExistsPair(SqliteConnection connection, string date, string slot)
    {
        if (!TryNormalizeDate(date, out var normalized)) return false;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM OpenDate d JOIN OpenDateTimeSlot ds ON ds.OpenDateId=d.Id JOIN TimeSlot s ON s.Id=ds.TimeSlotId WHERE d.Date=$date AND d.IsOpen=1 AND s.Code=$slot AND s.Active=1);";
        command.Parameters.AddWithValue("$date", normalized); command.Parameters.AddWithValue("$slot", slot.Trim());
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static IEnumerable<string> SplitAvailability(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void RequireHeaders(TabularData data, IEnumerable<string> expected, List<ImportIssue> issues, string file)
    {
        var missing = expected.Where(header => !data.Has(header)).ToArray();
        if (missing.Length > 0) issues.Add(new(1, file, $"必須列がありません: {string.Join(", ", missing)}"));
    }

    private static TabularData Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("取込ファイルが見つかりません。", path);
        return Path.GetExtension(path).ToLowerInvariant() switch { ".csv" => ReadCsv(path), ".xlsx" => ReadWorkbook(path), _ => throw new InvalidDataException("取込にはCSVまたはXLSXファイルを指定してください。") };
    }

    private static TabularData ReadCsv(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = DetectEncoding(File.ReadAllBytes(path));
        using var parser = new TextFieldParser(path, encoding) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = NormalizeHeaders(parser.ReadFields() ?? []); var rows = new List<string[]>();
        while (!parser.EndOfData) { var row = parser.ReadFields() ?? []; if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row.Select(value => value.Trim()).ToArray()); }
        return new(headers, rows);
    }

    private static Encoding DetectEncoding(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) return new UTF8Encoding(true, true);
        try { _ = new UTF8Encoding(false, true).GetString(content); return new UTF8Encoding(false, true); }
        catch (DecoderFallbackException)
        {
            var cp932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try { _ = cp932.GetString(content); return cp932; }
            catch (DecoderFallbackException exception) { throw new InvalidDataException("CSVの文字コードを判定できません。UTF-8またはCP932を使ってください。", exception); }
        }
    }

    private static TabularData ReadWorkbook(string path)
    {
        try
        {
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.FirstOrDefault(worksheet => worksheet.LastRowUsed() is not null) ?? throw new InvalidDataException("XLSXにデータのあるsheetがありません。");
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0; var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var headers = NormalizeHeaders(Enumerable.Range(1, lastColumn).Select(column => CellText(sheet.Cell(1, column))).ToArray()); var rows = new List<string[]>();
            for (var row = 2; row <= lastRow; row++) { var values = Enumerable.Range(1, lastColumn).Select(column => CellText(sheet.Cell(row, column))).ToArray(); if (values.Any(value => value.Length > 0)) rows.Add(values); }
            return new(headers, rows);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is IOException or FormatException) { throw new InvalidDataException($"XLSXを読み込めません: {exception.Message}", exception); }
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number) && Math.Abs(number - Math.Round(number)) < 0.0000001) return Math.Round(number).ToString(CultureInfo.InvariantCulture);
        return cell.GetFormattedString().Trim();
    }

    private static string[] NormalizeHeaders(string[] headers)
    {
        var result = headers.Select(header => header.Trim().Normalize(NormalizationForm.FormKC)).ToArray();
        if (result.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("ヘッダーに空の列名があります。");
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length) throw new InvalidDataException("ヘッダーの列名が重複しています。");
        return result;
    }

    private static bool TryNormalizeDate(string value, out string normalized)
    {
        if (DateOnly.TryParseExact(value, ["yyyy-MM-dd", "yyyy/M/d", "yyyy.MM.dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return true; }
        normalized = string.Empty; return false;
    }

    private static string NormalizeDate(string value) => TryNormalizeDate(value, out var result) ? result : throw new FormatException("日付が不正です。");
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string HashIfPresent(string path) => File.Exists(path) ? Hash(path) : string.Empty;
    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken) { var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString()); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }

    private sealed record TabularData(string[] Headers, List<string[]> Rows)
    {
        internal static TabularData Empty { get; } = new([], []);
        internal bool Has(string header) => Headers.Contains(header, StringComparer.OrdinalIgnoreCase);
        internal string Value(string[] row, string header) { var index = Array.FindIndex(Headers, candidate => string.Equals(candidate, header, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index < row.Length ? row[index].Trim() : string.Empty; }
    }
}
