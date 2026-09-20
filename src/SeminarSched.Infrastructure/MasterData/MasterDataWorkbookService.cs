using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.MasterData;

public sealed class MasterDataWorkbookService : IMasterDataWorkbookService
{
    private static readonly string[] SheetNames = ["生徒", "講師", "科目", "講師対応科目", "受講希望"];
    private const long MaximumWorkbookBytes = 25 * 1024 * 1024;
    private const int ReferenceValidationMaxRow = 1000;
    private const int ReferenceFormulaMaxRow = 200;

    public async Task ExportAsync(string projectPath, string destinationPath, CancellationToken cancellationToken = default)
    {
        RequireXlsxPath(destinationPath);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);

        using var workbook = new XLWorkbook();
        await WriteStudentsAsync(workbook, connection, cancellationToken).ConfigureAwait(false);
        await WriteTeachersAsync(workbook, connection, cancellationToken).ConfigureAwait(false);
        await WriteSubjectsAsync(workbook, connection, cancellationToken).ConfigureAwait(false);
        await WriteQualificationsAsync(workbook, connection, cancellationToken).ConfigureAwait(false);
        await WriteLessonRequestsAsync(workbook, connection, cancellationToken).ConfigureAwait(false);

        var fullPath = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("出力先フォルダーを判別できません。"));
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileNameWithoutExtension(fullPath)}.tmp-{Guid.NewGuid():N}.xlsx");
        try
        {
            await Task.Run(() => workbook.SaveAs(temporaryPath), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<MasterWorkbookPreview> PreviewAsync(string projectPath, string sourcePath, CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateBusinessRules(parsed, await LoadExistingAsync(connection, cancellationToken).ConfigureAwait(false));
        var counts = await CountChangesAsync(connection, parsed, cancellationToken).ConfigureAwait(false);
        return new MasterWorkbookPreview(Path.GetFullPath(sourcePath), parsed.Sha256, counts.New, counts.Update, parsed.Issues);
    }

    public async Task<MasterWorkbookImportResult> ApplyAsync(string projectPath, MasterWorkbookPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var parsed = await ParseAsync(preview.SourcePath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parsed.Sha256), Convert.FromHexString(preview.Sha256)))
            throw new InvalidDataException("プレビュー後にExcelファイルが変更されました。もう一度プレビューしてください。");

        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateBusinessRules(parsed, await LoadExistingAsync(connection, cancellationToken).ConfigureAwait(false));
        if (parsed.Issues.Any(issue => issue.Severity == MasterWorkbookIssueSeverity.Error))
            throw new InvalidDataException("取込エラーがあるため反映できません。" + Environment.NewLine + FormatIssue(parsed.Issues.First(issue => issue.Severity == MasterWorkbookIssueSeverity.Error)));

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var students = await UpsertStudentsAsync(connection, transaction, parsed.Students, cancellationToken).ConfigureAwait(false);
            var teachers = await UpsertTeachersAsync(connection, transaction, parsed.Teachers, cancellationToken).ConfigureAwait(false);
            var subjects = await UpsertSubjectsAsync(connection, transaction, parsed.Subjects, cancellationToken).ConfigureAwait(false);
            await UpsertQualificationsAsync(connection, transaction, parsed.Qualifications, teachers, subjects, cancellationToken).ConfigureAwait(false);
            await UpsertRequestsAsync(connection, transaction, parsed.Requests, students, teachers, subjects, cancellationToken).ConfigureAwait(false);
            await SaveImportEvidenceAsync(connection, transaction, preview.SourcePath, parsed, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new MasterWorkbookImportResult(parsed.RowCount, parsed.Issues.Count(issue => issue.Severity == MasterWorkbookIssueSeverity.Warning));
    }

    private static async Task<ParsedWorkbook> ParseAsync(string sourcePath, CancellationToken cancellationToken)
    {
        RequireXlsxPath(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Excelファイルが見つかりません。", fullPath);
        if (info.Length > MaximumWorkbookBytes) throw new InvalidDataException("Excelファイルは25MB以下にしてください。");
        var content = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return await Task.Run(() => ParseWorkbook(fullPath, sha256, content), cancellationToken).ConfigureAwait(false);
    }

    private static ParsedWorkbook ParseWorkbook(string path, string sha256, byte[] content)
    {
        var result = new ParsedWorkbook(path, sha256, content);
        try
        {
            using var workbook = new XLWorkbook(path);
            foreach (var name in SheetNames)
            {
                if (!workbook.TryGetWorksheet(name, out _))
                    result.Issues.Add(Error(name, null, null, "必須シートがありません。"));
            }
            if (result.Issues.Count != 0) return result;
            ReadRows(workbook.Worksheet("生徒"), StudentHeaders, result, ParseStudent, result.Students);
            ReadRows(workbook.Worksheet("講師"), TeacherHeaders, result, ParseTeacher, result.Teachers);
            ReadRows(workbook.Worksheet("科目"), SubjectHeaders, result, ParseSubject, result.Subjects);
            ReadRows(workbook.Worksheet("講師対応科目"), QualificationHeaders, result, ParseQualification, result.Qualifications);
            ReadRows(workbook.Worksheet("受講希望"), RequestHeaders, result, ParseRequest, result.Requests);
        }
        catch (MasterCellException exception)
        {
            result.Issues.Add(Error(exception.SheetName, exception.RowNumber, exception.ColumnName, exception.Message));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.Issues.Add(Error("", null, null, $"Excelファイルを読み取れません: {exception.Message}"));
        }
        AddDuplicateIssues(result.Students, row => row.ExternalId, "生徒", "生徒ID", result.Issues);
        AddDuplicateIssues(result.Teachers, row => row.ExternalId, "講師", "講師ID", result.Issues);
        AddDuplicateIssues(result.Subjects, row => row.Code, "科目", "科目コード", result.Issues);
        AddDuplicateIssues(result.Qualifications, row => $"{row.TeacherExternalId}\u001f{row.SubjectCode}", "講師対応科目", "講師ID・科目コード", result.Issues);
        AddDuplicateIssues(result.Requests, row => $"{row.StudentExternalId}\u001f{row.SubjectCode}", "受講希望", "生徒ID・科目コード", result.Issues);
        return result;
    }

    private static void ReadRows<T>(IXLWorksheet sheet, IReadOnlyCollection<string> requiredHeaders, ParsedWorkbook result, Func<RowReader, T> parse, List<T> destination)
        where T : IWorkbookRow
    {
        var headers = sheet.Row(1).CellsUsed().ToDictionary(cell => CanonicalHeader(cell.GetString()), cell => cell.Address.ColumnNumber, StringComparer.Ordinal);
        foreach (var required in requiredHeaders.Where(required => !headers.ContainsKey(required)))
            result.Issues.Add(Error(sheet.Name, 1, required, "必須列がありません。"));
        if (requiredHeaders.Any(required => !headers.ContainsKey(required))) return;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (sheet.Row(rowNumber).CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString()))) continue;
            var reader = new RowReader(sheet, rowNumber, headers);
            try
            {
                if (reader.Boolean("例示行", false, false) == true) continue;
                destination.Add(parse(reader));
            }
            catch (MasterCellException exception)
            {
                result.Issues.Add(Error(exception.SheetName, exception.RowNumber, exception.ColumnName, exception.Message));
            }
        }
    }

    private static StudentRow ParseStudent(RowReader row) => new(
        row.RowNumber,
        row.Text("生徒ID", true)!,
        row.Text("氏名", true)!,
        row.Text("学年", true)!,
        row.Integer("標準最大連続コマ数", false, 2, 1)!.Value,
        row.Boolean("空きコマ許可", false, false)!.Value,
        row.Text("備考", false) ?? "",
        row.Boolean("有効", false, true)!.Value);

    private static TeacherRow ParseTeacher(RowReader row) => new(
        row.RowNumber,
        row.Text("講師ID", true)!,
        row.Text("氏名", true)!,
        row.Boolean("空きコマ許可", false, false)!.Value,
        row.Text("備考", false) ?? "",
        row.Boolean("有効", false, true)!.Value);

    private static SubjectRow ParseSubject(RowReader row)
    {
        var displayName = row.Text("表示名", true)!;
        var code = row.Text("科目コード", true)!;
        return new SubjectRow(row.RowNumber, code, displayName, SubjectAbbreviation.Resolve(displayName, row.Text("略称", false), code), row.Text("学校段階", true)!, row.Integer("並び順", true, null, 1)!.Value, row.Boolean("有効", false, true)!.Value);
    }

    private static QualificationRow ParseQualification(RowReader row) => new(
        row.RowNumber,
        row.Text("講師ID", true)!,
        row.Text("科目コード", true)!,
        row.Boolean("指導可能", false, true)!.Value,
        row.Text("備考", false) ?? "");

    private static RequestRow ParseRequest(RowReader row) => new(
        row.RowNumber,
        row.Text("生徒ID", true)!,
        row.Text("科目コード", true)!,
        row.Integer("必要授業回数", true, null, 1)!.Value,
        row.Text("通常担当講師ID", false),
        row.Integer("担当講師優先度", false, 3, 1, 5)!.Value,
        row.Text("第1希望講師ID", false),
        row.Text("第2希望講師ID", false),
        row.Text("第3希望講師ID", false),
        row.Boolean("1対1必須", false, false)!.Value,
        row.Integer("最大連続コマ数上書き", false, null, 1),
        row.Boolean("空きコマ許可上書き", false, null),
        row.Text("備考", false) ?? "");

    private static void ValidateBusinessRules(ParsedWorkbook parsed, ExistingMaster existing)
    {
        var students = existing.Students.Concat(parsed.Students.Select(row => row.ExternalId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teachers = new Dictionary<string, bool>(existing.Teachers, StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Teachers) teachers[row.ExternalId] = row.Active;
        var subjects = existing.Subjects.Concat(parsed.Subjects.Select(row => row.Code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var qualifications = existing.Qualifications.ToDictionary(item => item.Key, item => item.Value, StringTupleComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Qualifications)
        {
            if (!teachers.ContainsKey(row.TeacherExternalId)) parsed.Issues.Add(Error("講師対応科目", row.RowNumber, "講師ID", "講師シートまたは既存データに存在しません。"));
            if (!subjects.Contains(row.SubjectCode)) parsed.Issues.Add(Error("講師対応科目", row.RowNumber, "科目コード", "科目シートまたは既存データに存在しません。"));
            qualifications[(row.TeacherExternalId, row.SubjectCode)] = row.CanTeach;
        }
        foreach (var row in parsed.Requests)
        {
            if (!students.Contains(row.StudentExternalId)) parsed.Issues.Add(Error("受講希望", row.RowNumber, "生徒ID", "生徒シートまたは既存データに存在しません。"));
            if (!subjects.Contains(row.SubjectCode)) parsed.Issues.Add(Error("受講希望", row.RowNumber, "科目コード", "科目シートまたは既存データに存在しません。"));
            var teacherIds = new[] { row.RegularTeacherExternalId, row.PreferredTeacher1ExternalId, row.PreferredTeacher2ExternalId, row.PreferredTeacher3ExternalId }.Where(id => id is not null).Cast<string>().ToArray();
            foreach (var teacherId in teacherIds.Where(id => !teachers.ContainsKey(id))) parsed.Issues.Add(Error("受講希望", row.RowNumber, "講師ID", $"講師ID {teacherId} は存在しません。"));
            foreach (var teacherId in teacherIds.Where(id => teachers.TryGetValue(id, out var active) && !active)) parsed.Issues.Add(Warning("受講希望", row.RowNumber, "講師ID", $"使用停止中の講師ID {teacherId} が指定されています。"));
            if (row.RegularTeacherPriority == 5 && row.RegularTeacherExternalId is null) parsed.Issues.Add(Error("受講希望", row.RowNumber, "通常担当講師ID", "担当講師優先度5では通常担当講師IDが必須です。"));
            if (row.RegularTeacherExternalId is not null && subjects.Contains(row.SubjectCode) && teachers.ContainsKey(row.RegularTeacherExternalId) && !qualifications.GetValueOrDefault((row.RegularTeacherExternalId, row.SubjectCode))) parsed.Issues.Add(Error("受講希望", row.RowNumber, "通常担当講師ID", "通常担当講師はこの科目を指導可能にしてください。"));
            var preferred = new[] { row.PreferredTeacher1ExternalId, row.PreferredTeacher2ExternalId, row.PreferredTeacher3ExternalId }.Where(id => id is not null).Cast<string>().ToArray();
            if (preferred.Distinct(StringComparer.OrdinalIgnoreCase).Count() != preferred.Length) parsed.Issues.Add(Warning("受講希望", row.RowNumber, "希望講師ID", "同じ希望講師が複数順位に指定されています。"));
        }
    }

    private static async Task<ExistingMaster> LoadExistingAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new ExistingMaster();
        await ReadExistingAsync(connection, "SELECT ExternalId,Active FROM Teacher", reader => result.Teachers[reader.GetString(0)] = reader.GetBoolean(1), cancellationToken).ConfigureAwait(false);
        await ReadExistingAsync(connection, "SELECT ExternalId FROM Student", reader => result.Students.Add(reader.GetString(0)), cancellationToken).ConfigureAwait(false);
        await ReadExistingAsync(connection, "SELECT Code FROM Subject", reader => result.Subjects.Add(reader.GetString(0)), cancellationToken).ConfigureAwait(false);
        await ReadExistingAsync(connection, "SELECT t.ExternalId,s.Code,q.CanTeach FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId JOIN Subject s ON s.Id=q.SubjectId", reader => result.Qualifications[(reader.GetString(0), reader.GetString(1))] = reader.GetBoolean(2), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task ReadExistingAsync(SqliteConnection connection, string sql, Action<SqliteDataReader> read, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) read(reader);
    }

    private static async Task<(Dictionary<string, int> New, Dictionary<string, int> Update)> CountChangesAsync(SqliteConnection connection, ParsedWorkbook parsed, CancellationToken cancellationToken)
    {
        var existing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["生徒"] = await ReadKeysAsync(connection, "SELECT ExternalId FROM Student", cancellationToken).ConfigureAwait(false),
            ["講師"] = await ReadKeysAsync(connection, "SELECT ExternalId FROM Teacher", cancellationToken).ConfigureAwait(false),
            ["科目"] = await ReadKeysAsync(connection, "SELECT Code FROM Subject", cancellationToken).ConfigureAwait(false),
            ["講師対応科目"] = await ReadKeysAsync(connection, "SELECT t.ExternalId||char(31)||s.Code FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId JOIN Subject s ON s.Id=q.SubjectId", cancellationToken).ConfigureAwait(false),
            ["受講希望"] = await ReadKeysAsync(connection, "SELECT st.ExternalId||char(31)||su.Code FROM LessonRequest r JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId WHERE r.ProjectId=1", cancellationToken).ConfigureAwait(false),
        };
        var incoming = new Dictionary<string, IEnumerable<string>>(StringComparer.Ordinal)
        {
            ["生徒"] = parsed.Students.Select(row => row.ExternalId), ["講師"] = parsed.Teachers.Select(row => row.ExternalId), ["科目"] = parsed.Subjects.Select(row => row.Code),
            ["講師対応科目"] = parsed.Qualifications.Select(row => $"{row.TeacherExternalId}\u001f{row.SubjectCode}"), ["受講希望"] = parsed.Requests.Select(row => $"{row.StudentExternalId}\u001f{row.SubjectCode}"),
        };
        var added = SheetNames.ToDictionary(name => name, name => incoming[name].Count(key => !existing[name].Contains(key)), StringComparer.Ordinal);
        var updated = SheetNames.ToDictionary(name => name, name => incoming[name].Count(key => existing[name].Contains(key)), StringComparer.Ordinal);
        return (added, updated);
    }

    private static async Task<HashSet<string>> ReadKeysAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase); await using var command = connection.CreateCommand(); command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0)); return result;
    }

    private static async Task<Dictionary<string, long>> UpsertStudentsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<StudentRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) result[row.ExternalId] = await UpsertIdAsync(connection, transaction, "INSERT INTO Student(ExternalId,Name,Grade,DefaultMaxConsecutiveSlots,AllowGap,Note,Active) VALUES($key,$name,$grade,$maximum,$gap,$note,$active) ON CONFLICT(ExternalId) DO UPDATE SET Name=excluded.Name,Grade=excluded.Grade,DefaultMaxConsecutiveSlots=excluded.DefaultMaxConsecutiveSlots,AllowGap=excluded.AllowGap,Note=excluded.Note,Active=excluded.Active RETURNING Id;", command => { Bind(command,"$key",row.ExternalId);Bind(command,"$name",row.Name);Bind(command,"$grade",row.Grade);Bind(command,"$maximum",row.DefaultMaximum);Bind(command,"$gap",row.AllowGap);Bind(command,"$note",row.Note);Bind(command,"$active",row.Active); }, cancellationToken).ConfigureAwait(false);
        await AddMissingIdsAsync(connection, transaction, "Student", "ExternalId", result, cancellationToken).ConfigureAwait(false); return result;
    }

    private static async Task<Dictionary<string, long>> UpsertTeachersAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<TeacherRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) result[row.ExternalId] = await UpsertIdAsync(connection, transaction, "INSERT INTO Teacher(ExternalId,Name,AllowGap,Note,Active) VALUES($key,$name,$gap,$note,$active) ON CONFLICT(ExternalId) DO UPDATE SET Name=excluded.Name,AllowGap=excluded.AllowGap,Note=excluded.Note,Active=excluded.Active RETURNING Id;", command => { Bind(command,"$key",row.ExternalId);Bind(command,"$name",row.Name);Bind(command,"$gap",row.AllowGap);Bind(command,"$note",row.Note);Bind(command,"$active",row.Active); }, cancellationToken).ConfigureAwait(false);
        await AddMissingIdsAsync(connection, transaction, "Teacher", "ExternalId", result, cancellationToken).ConfigureAwait(false); return result;
    }

    private static async Task<Dictionary<string, long>> UpsertSubjectsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<SubjectRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) result[row.Code] = await UpsertIdAsync(connection, transaction, "INSERT INTO Subject(Code,DisplayName,ShortName,SchoolLevel,SortOrder,Active) VALUES($key,$name,$short,$level,$sort,$active) ON CONFLICT(Code) DO UPDATE SET DisplayName=excluded.DisplayName,ShortName=excluded.ShortName,SchoolLevel=excluded.SchoolLevel,SortOrder=excluded.SortOrder,Active=excluded.Active RETURNING Id;", command => { Bind(command,"$key",row.Code);Bind(command,"$name",row.DisplayName);Bind(command,"$short",row.ShortName);Bind(command,"$level",row.SchoolLevel);Bind(command,"$sort",row.SortOrder);Bind(command,"$active",row.Active); }, cancellationToken).ConfigureAwait(false);
        await AddMissingIdsAsync(connection, transaction, "Subject", "Code", result, cancellationToken).ConfigureAwait(false); return result;
    }

    private static async Task AddMissingIdsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string keyColumn, Dictionary<string, long> result, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = $"SELECT Id,{keyColumn} FROM {table};";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.TryAdd(reader.GetString(1), reader.GetInt64(0));
    }

    private static async Task UpsertQualificationsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<QualificationRow> rows, IReadOnlyDictionary<string, long> teachers, IReadOnlyDictionary<string, long> subjects, CancellationToken cancellationToken)
    {
        foreach (var row in rows) { await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO TeacherQualification(TeacherId,SubjectId,CanTeach,Note) VALUES($teacher,$subject,$can,$note) ON CONFLICT(TeacherId,SubjectId) DO UPDATE SET CanTeach=excluded.CanTeach,Note=excluded.Note;";Bind(command,"$teacher",teachers[row.TeacherExternalId]);Bind(command,"$subject",subjects[row.SubjectCode]);Bind(command,"$can",row.CanTeach);Bind(command,"$note",row.Note);await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
    }

    private static async Task UpsertRequestsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<RequestRow> rows, IReadOnlyDictionary<string, long> students, IReadOnlyDictionary<string, long> teachers, IReadOnlyDictionary<string, long> subjects, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
                INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority,PreferredTeacher1Id,PreferredTeacher2Id,PreferredTeacher3Id,OneToOneRequired,MaxConsecutiveSlotsOverride,AllowGapOverride,Note)
                VALUES(1,$student,$subject,$sessions,$regular,$priority,$preferred1,$preferred2,$preferred3,$one,$maximum,$gap,$note)
                ON CONFLICT(ProjectId,StudentId,SubjectId) DO UPDATE SET RequiredSessions=excluded.RequiredSessions,RegularTeacherId=excluded.RegularTeacherId,RegularTeacherPriority=excluded.RegularTeacherPriority,PreferredTeacher1Id=excluded.PreferredTeacher1Id,PreferredTeacher2Id=excluded.PreferredTeacher2Id,PreferredTeacher3Id=excluded.PreferredTeacher3Id,OneToOneRequired=excluded.OneToOneRequired,MaxConsecutiveSlotsOverride=excluded.MaxConsecutiveSlotsOverride,AllowGapOverride=excluded.AllowGapOverride,Note=excluded.Note;
                """;
            Bind(command,"$student",students[row.StudentExternalId]);Bind(command,"$subject",subjects[row.SubjectCode]);Bind(command,"$sessions",row.RequiredSessions);Bind(command,"$regular",TeacherId(teachers,row.RegularTeacherExternalId));Bind(command,"$priority",row.RegularTeacherPriority);Bind(command,"$preferred1",TeacherId(teachers,row.PreferredTeacher1ExternalId));Bind(command,"$preferred2",TeacherId(teachers,row.PreferredTeacher2ExternalId));Bind(command,"$preferred3",TeacherId(teachers,row.PreferredTeacher3ExternalId));Bind(command,"$one",row.OneToOneRequired);Bind(command,"$maximum",row.MaximumOverride);Bind(command,"$gap",row.AllowGapOverride);Bind(command,"$note",row.Note);await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static object? TeacherId(IReadOnlyDictionary<string,long> teachers,string? externalId)=>externalId is null?null:teachers[externalId];
    private static async Task<long> UpsertIdAsync(SqliteConnection connection,SqliteTransaction transaction,string sql,Action<SqliteCommand> bind,CancellationToken cancellationToken){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=sql;bind(command);return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),CultureInfo.InvariantCulture);}
    private static void Bind(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);

    private static async Task SaveImportEvidenceAsync(SqliteConnection connection,SqliteTransaction transaction,string sourcePath,ParsedWorkbook parsed,CancellationToken cancellationToken)
    {
        var now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);var fileName=Path.GetFileName(sourcePath);
        await using(var batch=connection.CreateCommand()){batch.Transaction=transaction;batch.CommandText="INSERT INTO ImportBatch(ProjectId,ImportType,SourceFileName,ImportedUtc,RowCount,SuccessCount,WarningCount,ErrorCount,MappingJson) VALUES(1,'master_workbook',$file,$utc,$rows,$rows,$warnings,0,$mapping);";Bind(batch,"$file",fileName);Bind(batch,"$utc",now);Bind(batch,"$rows",parsed.RowCount);Bind(batch,"$warnings",parsed.Issues.Count(issue=>issue.Severity==MasterWorkbookIssueSeverity.Warning));Bind(batch,"$mapping",JsonSerializer.Serialize(new{Sheets=SheetNames}));await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
        await using(var snapshot=connection.CreateCommand()){snapshot.Transaction=transaction;snapshot.CommandText="INSERT INTO ImportSourceSnapshot(ProjectId,ImportType,SourceFileName,Content,Sha256,SizeBytes,ImportedUtc) VALUES(1,'master_workbook',$file,$content,$sha,$size,$utc) ON CONFLICT(ProjectId,ImportType) DO UPDATE SET SourceFileName=excluded.SourceFileName,Content=excluded.Content,Sha256=excluded.Sha256,SizeBytes=excluded.SizeBytes,ImportedUtc=excluded.ImportedUtc;";Bind(snapshot,"$file",fileName);Bind(snapshot,"$content",parsed.Content);Bind(snapshot,"$sha",parsed.Sha256);Bind(snapshot,"$size",parsed.Content.LongLength);Bind(snapshot,"$utc",now);await snapshot.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
        await using(var audit=connection.CreateCommand()){audit.Transaction=transaction;audit.CommandText="INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,'master_workbook_import','master_data','project:1',$summary,'共通基本情報Excel取込','import',$operation);";Bind(audit,"$utc",now);Bind(audit,"$summary",JsonSerializer.Serialize(new{parsed.RowCount,SheetCount=SheetNames.Length,parsed.Sha256}));Bind(audit,"$operation",Guid.NewGuid().ToString("N"));await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);}
    }

    private static async Task WriteStudentsAsync(XLWorkbook workbook,SqliteConnection connection,CancellationToken cancellationToken)=>await WriteSheetAsync(workbook,connection,"生徒",StudentExportHeaders,"SELECT 0,ExternalId,Name,Grade,DefaultMaxConsecutiveSlots,AllowGap,Note,Active FROM Student ORDER BY Active DESC,ExternalId",["はい","S-EXAMPLE","架空 花子","J2",2,"いいえ","この行は取込時に無視されます。","はい"],cancellationToken).ConfigureAwait(false);
    private static async Task WriteTeachersAsync(XLWorkbook workbook,SqliteConnection connection,CancellationToken cancellationToken)=>await WriteSheetAsync(workbook,connection,"講師",TeacherExportHeaders,"SELECT 0,ExternalId,Name,AllowGap,Note,Active FROM Teacher ORDER BY Active DESC,ExternalId",["はい","T-EXAMPLE","架空 太郎","いいえ","この行は取込時に無視されます。","はい"],cancellationToken).ConfigureAwait(false);
    private static async Task WriteSubjectsAsync(XLWorkbook workbook,SqliteConnection connection,CancellationToken cancellationToken)=>await WriteSheetAsync(workbook,connection,"科目",SubjectExportHeaders,"SELECT 0,Code,DisplayName,ShortName,SchoolLevel,SortOrder,Active FROM Subject ORDER BY SortOrder,Code",["はい","JH-MATH","中学校・数学（例）","数学","中学校",1,"はい"],cancellationToken).ConfigureAwait(false);
    private static async Task WriteQualificationsAsync(XLWorkbook workbook, SqliteConnection connection, CancellationToken cancellationToken)
    {
        var sheet = workbook.AddWorksheet("講師対応科目");
        var headers = QualificationExportHeaders;
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Cell(2,1).Value="はい";sheet.Cell(2,2).Value="T-EXAMPLE";sheet.Cell(2,5).Value="JH-MATH";sheet.Cell(2,8).Value="はい";sheet.Cell(2,9).Value="この行は取込時に無視されます。";

        var row = 3;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT t.ExternalId,s.Code,q.CanTeach,q.Note FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId JOIN Subject s ON s.Id=q.SubjectId ORDER BY t.ExternalId,s.Code";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sheet.Cell(row,2).Value = reader.GetString(0);
            sheet.Cell(row,5).Value = reader.GetString(1);
            sheet.Cell(row,8).Value = ToCellValue(reader.GetBoolean(2));
            sheet.Cell(row,9).Value = reader.GetString(3);
            row++;
        }
        var lastDataRow = row - 1;
        AddReferenceHelperColumns(workbook, sheet, idColumn: 2, selectColumn: 3, confirmColumn: 4, sourceSheetName: "講師", lastDataRow, idRequired: true);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 5, selectColumn: 6, confirmColumn: 7, sourceSheetName: "科目", lastDataRow, idRequired: true);
        FinalizeSheetStyle(sheet, headers.Length);
    }

    private static async Task WriteLessonRequestsAsync(XLWorkbook workbook, SqliteConnection connection, CancellationToken cancellationToken)
    {
        var sheet = workbook.AddWorksheet("受講希望");
        var headers = RequestExportHeaders;
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Cell(2,1).Value="はい";sheet.Cell(2,2).Value="S-EXAMPLE";sheet.Cell(2,5).Value="JH-MATH";sheet.Cell(2,8).Value=4;sheet.Cell(2,9).Value="T-EXAMPLE";sheet.Cell(2,12).Value=3;sheet.Cell(2,13).Value="T-EXAMPLE";sheet.Cell(2,22).Value="いいえ";sheet.Cell(2,25).Value="この行は取込時に無視されます。";

        var row = 3;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT st.ExternalId,su.Code,r.RequiredSessions,rt.ExternalId,r.RegularTeacherPriority,p1.ExternalId,p2.ExternalId,p3.ExternalId,r.OneToOneRequired,r.MaxConsecutiveSlotsOverride,r.AllowGapOverride,r.Note FROM LessonRequest r JOIN Student st ON st.Id=r.StudentId JOIN Subject su ON su.Id=r.SubjectId LEFT JOIN Teacher rt ON rt.Id=r.RegularTeacherId LEFT JOIN Teacher p1 ON p1.Id=r.PreferredTeacher1Id LEFT JOIN Teacher p2 ON p2.Id=r.PreferredTeacher2Id LEFT JOIN Teacher p3 ON p3.Id=r.PreferredTeacher3Id WHERE r.ProjectId=1 ORDER BY st.ExternalId,su.Code";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sheet.Cell(row,2).Value = reader.GetString(0);
            sheet.Cell(row,5).Value = reader.GetString(1);
            sheet.Cell(row,8).Value = reader.GetInt32(2);
            sheet.Cell(row,9).Value = ToCellValue(reader.IsDBNull(3) ? null : reader.GetString(3));
            sheet.Cell(row,12).Value = reader.GetInt32(4);
            sheet.Cell(row,13).Value = ToCellValue(reader.IsDBNull(5) ? null : reader.GetString(5));
            sheet.Cell(row,16).Value = ToCellValue(reader.IsDBNull(6) ? null : reader.GetString(6));
            sheet.Cell(row,19).Value = ToCellValue(reader.IsDBNull(7) ? null : reader.GetString(7));
            sheet.Cell(row,22).Value = ToCellValue(reader.GetBoolean(8));
            sheet.Cell(row,23).Value = ToCellValue(reader.IsDBNull(9) ? null : reader.GetInt32(9));
            sheet.Cell(row,24).Value = ToCellValue(reader.IsDBNull(10) ? null : reader.GetBoolean(10));
            sheet.Cell(row,25).Value = reader.GetString(11);
            row++;
        }
        var lastDataRow = row - 1;
        AddReferenceHelperColumns(workbook, sheet, idColumn: 2, selectColumn: 3, confirmColumn: 4, sourceSheetName: "生徒", lastDataRow, idRequired: true);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 5, selectColumn: 6, confirmColumn: 7, sourceSheetName: "科目", lastDataRow, idRequired: true);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 9, selectColumn: 10, confirmColumn: 11, sourceSheetName: "講師", lastDataRow, idRequired: false);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 13, selectColumn: 14, confirmColumn: 15, sourceSheetName: "講師", lastDataRow, idRequired: false);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 16, selectColumn: 17, confirmColumn: 18, sourceSheetName: "講師", lastDataRow, idRequired: false);
        AddReferenceHelperColumns(workbook, sheet, idColumn: 19, selectColumn: 20, confirmColumn: 21, sourceSheetName: "講師", lastDataRow, idRequired: false);
        FinalizeSheetStyle(sheet, headers.Length);
    }

    private static async Task WriteSheetAsync(XLWorkbook workbook,SqliteConnection connection,string name,string[] headers,string sql,object?[] example,CancellationToken cancellationToken)
    {
        var sheet=workbook.AddWorksheet(name);for(var column=0;column<headers.Length;column++){sheet.Cell(1,column+1).Value=headers[column];sheet.Cell(2,column+1).Value=ToCellValue(example[column]);}
        var row=3;await using var command=connection.CreateCommand();command.CommandText=sql;await using var reader=await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false)){for(var column=0;column<reader.FieldCount;column++)sheet.Cell(row,column+1).Value=ToCellValue(reader.IsDBNull(column)?null:reader.GetValue(column));row++;}
        FinalizeSheetStyle(sheet, headers.Length);
    }

    // Dropdown on the id column + a name-picker dropdown that auto-fills blank rows' ids + a reverse-lookup confirm column.
    private static void AddReferenceHelperColumns(XLWorkbook workbook, IXLWorksheet sheet, int idColumn, int selectColumn, int confirmColumn, string sourceSheetName, int lastDataRow, bool idRequired)
    {
        var sourceSheet = workbook.Worksheet(sourceSheetName);
        var idSourceRange = sourceSheet.Range(3, 2, ReferenceValidationMaxRow, 2);
        var nameSourceRange = sourceSheet.Range(3, 3, ReferenceValidationMaxRow, 3);
        var idLetter = ColumnLetter(idColumn);
        var selectLetter = ColumnLetter(selectColumn);
        var idSourceRef = $"'{sourceSheetName}'!$B$3:$B${ReferenceValidationMaxRow}";
        var nameSourceRef = $"'{sourceSheetName}'!$C$3:$C${ReferenceValidationMaxRow}";

        var idValidation = sheet.Range(3, idColumn, ReferenceValidationMaxRow, idColumn).CreateDataValidation();
        idValidation.List(idSourceRange, true);
        idValidation.IgnoreBlanks = !idRequired;
        idValidation.ShowErrorMessage = true;
        idValidation.ErrorTitle = "一覧にない値です";
        idValidation.ErrorMessage = $"{sourceSheetName}シートに登録済みのIDまたはコードを入力するか、右の「名前から選択」列で選んでください。";

        var selectValidation = sheet.Range(3, selectColumn, ReferenceValidationMaxRow, selectColumn).CreateDataValidation();
        selectValidation.List(nameSourceRange, true);
        selectValidation.IgnoreBlanks = true;
        selectValidation.ShowErrorMessage = true;
        selectValidation.ErrorTitle = "一覧にない名前です";
        selectValidation.ErrorMessage = $"{sourceSheetName}シートに登録済みの名前を選択してください。";

        var formulaEnd = Math.Max(lastDataRow, ReferenceFormulaMaxRow);
        for (var row = 3; row <= formulaEnd; row++)
            sheet.Cell(row, confirmColumn).FormulaA1 = $"IF({idLetter}{row}=\"\",\"\",IFERROR(INDEX({nameSourceRef},MATCH({idLetter}{row},{idSourceRef},0)),\"ID不明\"))";

        for (var row = lastDataRow + 1; row <= ReferenceFormulaMaxRow; row++)
            sheet.Cell(row, idColumn).FormulaA1 = $"IF({selectLetter}{row}=\"\",\"\",IF(COUNTIF({nameSourceRef},{selectLetter}{row})=1,INDEX({idSourceRef},MATCH({selectLetter}{row},{nameSourceRef},0)),\"\"))";
    }

    private static void FinalizeSheetStyle(IXLWorksheet sheet, int headerColumnCount)
    {
        var header = sheet.Range(1, 1, 1, headerColumnCount);
        header.Style.Font.Bold = true; header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1");
        sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 36); sheet.RangeUsed()?.SetAutoFilter();
    }

    private static string ColumnLetter(int columnNumber)
    {
        var letters = string.Empty;
        while (columnNumber > 0)
        {
            var remainder = (columnNumber - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            columnNumber = (columnNumber - 1) / 26;
        }
        return letters;
    }

    private static XLCellValue ToCellValue(object? value)=>value switch{null=>Blank.Value,bool boolean=>boolean?"はい":"いいえ",long number=>number,int number=>number,double number=>number,string text=>text,_=>Convert.ToString(value,CultureInfo.InvariantCulture)??string.Empty};
    private static string CanonicalHeader(string header)=>header.Trim().Replace("（必須）",string.Empty,StringComparison.Ordinal);
    private static string FormatIssue(MasterWorkbookIssue issue)=>$"{issue.SheetName}{(issue.RowNumber is null?"":$" {issue.RowNumber}行")}{(issue.ColumnName is null?"":$" [{issue.ColumnName}]")}: {issue.Message}";
    private static MasterWorkbookIssue Error(string sheet,int? row,string? column,string message)=>new(MasterWorkbookIssueSeverity.Error,sheet,row,column,message);
    private static MasterWorkbookIssue Warning(string sheet,int? row,string? column,string message)=>new(MasterWorkbookIssueSeverity.Warning,sheet,row,column,message);
    private static void RequireXlsxPath(string path){ArgumentException.ThrowIfNullOrWhiteSpace(path);if(!string.Equals(Path.GetExtension(path),".xlsx",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("共通基本情報には.xlsxファイルを指定してください。");}
    private static async Task<SqliteConnection> OpenAsync(string path,CancellationToken cancellationToken){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,ForeignKeys=true,Pooling=false}.ToString());await connection.OpenAsync(cancellationToken).ConfigureAwait(false);return connection;}
    private static void AddDuplicateIssues<T>(IEnumerable<T> rows,Func<T,string> key,string sheet,string column,List<MasterWorkbookIssue> issues) where T:IWorkbookRow{foreach(var group in rows.GroupBy(key,StringComparer.OrdinalIgnoreCase).Where(group=>group.Count()>1))foreach(var row in group)issues.Add(Error(sheet,row.RowNumber,column,"ファイル内で重複しています。"));}

    private static readonly string[] StudentHeaders=["例示行","生徒ID","氏名","学年","標準最大連続コマ数","空きコマ許可","備考","有効"];
    private static readonly string[] TeacherHeaders=["例示行","講師ID","氏名","空きコマ許可","備考","有効"];
    private static readonly string[] SubjectHeaders=["例示行","科目コード","表示名","学校段階","並び順","有効"];
    private static readonly string[] QualificationHeaders=["例示行","講師ID","科目コード","指導可能","備考"];
    private static readonly string[] RequestHeaders=["例示行","生徒ID","科目コード","必要授業回数","通常担当講師ID","担当講師優先度","第1希望講師ID","第2希望講師ID","第3希望講師ID","1対1必須","最大連続コマ数上書き","空きコマ許可上書き","備考"];
    private static readonly string[] StudentExportHeaders=["例示行","生徒ID（必須）","氏名（必須）","学年（必須）","標準最大連続コマ数","空きコマ許可","備考","有効"];
    private static readonly string[] TeacherExportHeaders=["例示行","講師ID（必須）","氏名（必須）","空きコマ許可","備考","有効"];
    private static readonly string[] SubjectExportHeaders=["例示行","科目コード（必須）","表示名（必須）","略称","学校段階（必須）","並び順（必須）","有効"];
    private static readonly string[] QualificationExportHeaders=["例示行","講師ID（必須）","講師名から選択","講師名（確認）","科目コード（必須）","科目名から選択","科目名（確認）","指導可能","備考"];
    private static readonly string[] RequestExportHeaders=["例示行","生徒ID（必須）","生徒名から選択","生徒氏名（確認）","科目コード（必須）","科目名から選択","科目名（確認）","必要授業回数（必須）","通常担当講師ID","通常担当講師名から選択","通常担当講師名（確認）","担当講師優先度","第1希望講師ID","第1希望講師名から選択","第1希望講師名（確認）","第2希望講師ID","第2希望講師名から選択","第2希望講師名（確認）","第3希望講師ID","第3希望講師名から選択","第3希望講師名（確認）","1対1必須","最大連続コマ数上書き","空きコマ許可上書き","備考"];

    private interface IWorkbookRow{int RowNumber{get;}}
    private sealed record StudentRow(int RowNumber,string ExternalId,string Name,string Grade,int DefaultMaximum,bool AllowGap,string Note,bool Active):IWorkbookRow;
    private sealed record TeacherRow(int RowNumber,string ExternalId,string Name,bool AllowGap,string Note,bool Active):IWorkbookRow;
    private sealed record SubjectRow(int RowNumber,string Code,string DisplayName,string ShortName,string SchoolLevel,int SortOrder,bool Active):IWorkbookRow;
    private sealed record QualificationRow(int RowNumber,string TeacherExternalId,string SubjectCode,bool CanTeach,string Note):IWorkbookRow;
    private sealed record RequestRow(int RowNumber,string StudentExternalId,string SubjectCode,int RequiredSessions,string? RegularTeacherExternalId,int RegularTeacherPriority,string? PreferredTeacher1ExternalId,string? PreferredTeacher2ExternalId,string? PreferredTeacher3ExternalId,bool OneToOneRequired,int? MaximumOverride,bool? AllowGapOverride,string Note):IWorkbookRow;

    private sealed class ParsedWorkbook(string path,string sha256,byte[] content)
    {
        public string Path{get;}=path;public string Sha256{get;}=sha256;public byte[] Content{get;}=content;public List<MasterWorkbookIssue> Issues{get;}=[];public List<StudentRow> Students{get;}=[];public List<TeacherRow> Teachers{get;}=[];public List<SubjectRow> Subjects{get;}=[];public List<QualificationRow> Qualifications{get;}=[];public List<RequestRow> Requests{get;}=[];public int RowCount=>Students.Count+Teachers.Count+Subjects.Count+Qualifications.Count+Requests.Count;
    }
    private sealed class ExistingMaster{public HashSet<string> Students{get;}=new(StringComparer.OrdinalIgnoreCase);public Dictionary<string,bool> Teachers{get;}=new(StringComparer.OrdinalIgnoreCase);public HashSet<string> Subjects{get;}=new(StringComparer.OrdinalIgnoreCase);public Dictionary<(string,string),bool> Qualifications{get;}=new(StringTupleComparer.OrdinalIgnoreCase);}
    private sealed class StringTupleComparer(StringComparer comparer):IEqualityComparer<(string,string)>{public static StringTupleComparer OrdinalIgnoreCase{get;}=new(StringComparer.OrdinalIgnoreCase);public bool Equals((string,string)x,(string,string)y)=>comparer.Equals(x.Item1,y.Item1)&&comparer.Equals(x.Item2,y.Item2);public int GetHashCode((string,string)obj)=>HashCode.Combine(comparer.GetHashCode(obj.Item1),comparer.GetHashCode(obj.Item2));}

    private sealed class RowReader(IXLWorksheet sheet,int rowNumber,IReadOnlyDictionary<string,int> headers)
    {
        public int RowNumber=>rowNumber;
        public string? Text(string header,bool required){var value=Raw(header);if(string.IsNullOrWhiteSpace(value)){if(required)throw Failure(header,"必須項目です。");return null;}return value.Trim();}
        public int? Integer(string header,bool required,int? defaultValue,int? minimum=null,int? maximum=null){var value=Raw(header);if(string.IsNullOrWhiteSpace(value)){if(defaultValue is not null)return defaultValue;if(required)throw Failure(header,"必須項目です。");return null;}if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number))throw Failure(header,"整数で入力してください。");if(minimum is not null&&number<minimum)throw Failure(header,$"{minimum}以上で入力してください。");if(maximum is not null&&number>maximum)throw Failure(header,$"{maximum}以下で入力してください。");return number;}
        public bool? Boolean(string header,bool required,bool? defaultValue){var value=Raw(header);if(string.IsNullOrWhiteSpace(value)){if(defaultValue is not null)return defaultValue;if(required)throw Failure(header,"必須項目です。");return null;}return value.Trim().ToLowerInvariant() switch{"はい" or "true" or "1" or "有効" or "可" or "○"=>true,"いいえ" or "false" or "0" or "無効" or "不可" or "×"=>false,_=>throw Failure(header,"「はい」または「いいえ」で入力してください。")};}
        private string Raw(string header)=>headers.TryGetValue(header,out var column)?sheet.Cell(rowNumber,column).GetString():string.Empty;
        private MasterCellException Failure(string header,string message)=>new(sheet.Name,rowNumber,header,message);
    }
    private sealed class MasterCellException(string sheetName,int rowNumber,string columnName,string message):Exception(message){public string SheetName{get;}=sheetName;public int RowNumber{get;}=rowNumber;public string ColumnName{get;}=columnName;}
}
