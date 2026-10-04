using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.MasterData;

public sealed class SharedRosterImportService : ISharedRosterImportService
{
    private static readonly string[] SheetNames = ["生徒", "講師", "科目", "講師対応科目", "通常授業"];
    private const long MaximumWorkbookBytes = 25 * 1024 * 1024;

    // Python版のgrade_from_excel相当。学年列は"S1"/"J2"/"H3"のExcel短縮表記で保存されているため、
    // 内部表記（"小1"等）へ変換する。既に内部表記の値が入っていればそのまま通す。
    private static readonly Dictionary<string, string> GradeFromExcelCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S1"] = "小1", ["S2"] = "小2", ["S3"] = "小3", ["S4"] = "小4", ["S5"] = "小5", ["S6"] = "小6",
        ["J1"] = "中1", ["J2"] = "中2", ["J3"] = "中3",
        ["H1"] = "高1", ["H2"] = "高2", ["H3"] = "高3",
    };

    public async Task<SharedRosterPreview> PreviewAsync(string projectPath, string sourcePath, CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateBusinessRules(parsed);
        return new SharedRosterPreview(Path.GetFullPath(sourcePath), parsed.Sha256,
            parsed.Students.Count, parsed.Teachers.Count, parsed.Subjects.Count, parsed.Qualifications.Count, parsed.RegularLessons.Count,
            parsed.Issues);
    }

    public async Task<SharedRosterImportResult> ApplyAsync(string projectPath, SharedRosterPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var parsed = await ParseAsync(preview.SourcePath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parsed.Sha256), Convert.FromHexString(preview.Sha256)))
            throw new InvalidDataException("プレビュー後にExcelファイルが変更されました。もう一度プレビューしてください。");

        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateBusinessRules(parsed);
        if (parsed.Issues.Any(issue => issue.Severity == SharedRosterIssueSeverity.Error))
            throw new InvalidDataException("取込エラーがあるため反映できません。" + Environment.NewLine + FormatIssue(parsed.Issues.First(issue => issue.Severity == SharedRosterIssueSeverity.Error)));

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var students = await UpsertStudentsAsync(connection, transaction, parsed.Students, cancellationToken).ConfigureAwait(false);
            var teachers = await UpsertTeachersAsync(connection, transaction, parsed.Teachers, cancellationToken).ConfigureAwait(false);
            var subjects = await UpsertSubjectsAsync(connection, transaction, parsed.Subjects, cancellationToken).ConfigureAwait(false);
            await UpsertQualificationsAsync(connection, transaction, parsed.Qualifications, teachers, subjects, cancellationToken).ConfigureAwait(false);
            await UpsertRegularLessonsAsync(connection, transaction, parsed.RegularLessons, students, teachers, subjects, cancellationToken).ConfigureAwait(false);
            await SaveImportEvidenceAsync(connection, transaction, preview.SourcePath, parsed, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new SharedRosterImportResult(parsed.RowCount, parsed.Issues.Count(issue => issue.Severity == SharedRosterIssueSeverity.Warning));
    }

    private static async Task<ParsedRoster> ParseAsync(string sourcePath, CancellationToken cancellationToken)
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

    private static ParsedRoster ParseWorkbook(string path, string sha256, byte[] content)
    {
        var result = new ParsedRoster(path, sha256, content);
        try
        {
            using var workbook = new XLWorkbook(path);
            foreach (var name in SheetNames)
                if (!workbook.TryGetWorksheet(name, out _))
                    result.Issues.Add(Error(name, null, null, "必須シートがありません。"));
            if (result.Issues.Count != 0) return result;

            ReadRows(workbook.Worksheet("生徒"), StudentRequiredHeaders, result, ParseStudent, result.Students);
            ReadRows(workbook.Worksheet("講師"), TeacherRequiredHeaders, result, ParseTeacher, result.Teachers);
            ReadRows(workbook.Worksheet("科目"), SubjectRequiredHeaders, result, ParseSubject, result.Subjects);
            ReadRows(workbook.Worksheet("講師対応科目"), QualificationRequiredHeaders, result, ParseQualification, result.Qualifications);
            ReadRows(workbook.Worksheet("通常授業"), RegularLessonRequiredHeaders, result, ParseRegularLesson, result.RegularLessons);
        }
        catch (RosterCellException exception)
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
        AddDuplicateIssues(result.Qualifications, row => $"{row.TeacherExternalId}{row.SubjectCode}", "講師対応科目", "講師ID・科目コード", result.Issues);
        AddDuplicateIssues(result.RegularLessons, row => $"{row.StudentExternalId}{row.SubjectCode}", "通常授業", "生徒ID・科目コード", result.Issues);
        return result;
    }

    private static void ReadRows<T>(IXLWorksheet sheet, IReadOnlyCollection<string> requiredHeaders, ParsedRoster result, Func<RowReader, T?> parse, List<T> destination)
        where T : class, IRosterRow
    {
        var headers = sheet.Row(1).CellsUsed().ToDictionary(cell => CanonicalHeader(cell.GetString()), cell => cell.Address.ColumnNumber, StringComparer.Ordinal);
        foreach (var required in requiredHeaders.Where(required => !headers.ContainsKey(required)))
            result.Issues.Add(Error(sheet.Name, 1, required, "必須列がありません。"));
        if (requiredHeaders.Any(required => !headers.ContainsKey(required))) return;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var reader = new RowReader(sheet, rowNumber, headers);
            try
            {
                var row = parse(reader);
                if (row is not null) destination.Add(row);
            }
            catch (RosterCellException exception)
            {
                result.Issues.Add(Error(exception.SheetName, exception.RowNumber, exception.ColumnName, exception.Message));
            }
        }
    }

    // IDはPython版の名前選択helper列による数式で既に解決済みの値として保存されているため、
    // helper列（"◯◯名から選択"）はそのまま無視し、ID列の値をそのまま使用する。
    private static StudentRow? ParseStudent(RowReader row)
    {
        var id = row.Text("生徒ID", false);
        if (id is null) return null;
        var (familyName, givenName) = ReadName(row);
        return new StudentRow(row.RowNumber, row.Boolean("在籍", true, null)!.Value, id, familyName, givenName,
            ConvertGrade(row.Text("学年", true)!), row.Integer("標準最大連続コマ数", false, 2, 1)!.Value,
            row.Boolean("空きコマ許可", false, false)!.Value, row.Text("備考", false) ?? "");
    }

    private static TeacherRow? ParseTeacher(RowReader row)
    {
        var id = row.Text("講師ID", false);
        if (id is null) return null;
        var (familyName, givenName) = ReadName(row);
        return new TeacherRow(row.RowNumber, row.Boolean("在籍", true, null)!.Value, id, familyName, givenName,
            row.Boolean("空きコマ許可", false, false)!.Value, row.Text("備考", false) ?? "");
    }

    // ユーザー要望（checkpoint145）「姓と名を分けて保存」。SharedRosterWorkbookWriterは現在、
    // 「姓（必須）」「名」「氏名（確認）」の3列を出力するため、新しい「姓」列がある場合はそれを
    // 優先して読む。「姓」列が無い（このアプリの旧版で出力された、または手作業で作られた）
    // 古いExcelファイルでは「氏名」列のみを最初の半角スペースで分割する（後方互換）。
    private static (string FamilyName, string GivenName) ReadName(RowReader row)
    {
        if (row.HasColumn("姓"))
            return (row.Text("姓", true)!, row.Text("名", false) ?? "");
        var fullName = row.Text("氏名", true)!;
        var spaceIndex = fullName.IndexOf(' ');
        return spaceIndex < 0 ? (fullName, "") : (fullName[..spaceIndex], fullName[(spaceIndex + 1)..]);
    }

    private static SubjectRow? ParseSubject(RowReader row)
    {
        var code = row.Text("科目コード", false);
        if (code is null) return null;
        var displayName = row.Text("表示名", true)!;
        return new SubjectRow(row.RowNumber, code, displayName, row.Text("学校段階", true)!,
            row.Integer("並び順", true, null, 1)!.Value, row.Boolean("有効", false, true)!.Value);
    }

    private static QualificationRow? ParseQualification(RowReader row)
    {
        var teacherId = row.Text("講師ID", false);
        if (teacherId is null) return null;
        return new QualificationRow(row.RowNumber, teacherId, row.Text("科目コード", true)!,
            row.Boolean("指導可能", false, true)!.Value, row.Text("備考", false) ?? "");
    }

    private static RegularLessonRow? ParseRegularLesson(RowReader row)
    {
        var studentId = row.Text("生徒ID", false);
        if (studentId is null) return null;
        return new RegularLessonRow(row.RowNumber, studentId, row.Text("科目コード", true)!,
            row.Text("通常担当講師ID", false), row.Integer("担当講師優先度", false, 3, 1, 5)!.Value,
            row.Boolean("1対1必須", false, false)!.Value, row.Text("備考", false) ?? "");
    }

    private static string ConvertGrade(string value) => GradeFromExcelCode.TryGetValue(value.Trim(), out var converted) ? converted : value.Trim();

    private static void ValidateBusinessRules(ParsedRoster parsed)
    {
        var teachers = parsed.Teachers.Select(row => row.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var students = parsed.Students.Select(row => row.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subjects = parsed.Subjects.Select(row => row.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Qualifications)
        {
            if (!teachers.Contains(row.TeacherExternalId)) parsed.Issues.Add(Error("講師対応科目", row.RowNumber, "講師ID", "講師シートに存在しません。"));
            if (!subjects.Contains(row.SubjectCode)) parsed.Issues.Add(Error("講師対応科目", row.RowNumber, "科目コード", "科目シートに存在しません。"));
        }
        foreach (var row in parsed.RegularLessons)
        {
            if (!students.Contains(row.StudentExternalId)) parsed.Issues.Add(Error("通常授業", row.RowNumber, "生徒ID", "生徒シートに存在しません。"));
            if (!subjects.Contains(row.SubjectCode)) parsed.Issues.Add(Error("通常授業", row.RowNumber, "科目コード", "科目シートに存在しません。"));
            if (row.RegularTeacherExternalId is not null && !teachers.Contains(row.RegularTeacherExternalId))
                parsed.Issues.Add(Error("通常授業", row.RowNumber, "通常担当講師ID", $"講師ID {row.RegularTeacherExternalId} は存在しません。"));
        }
    }

    private static async Task<Dictionary<string, long>> UpsertStudentsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<StudentRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            result[row.ExternalId] = await UpsertIdAsync(connection, transaction,
                "INSERT INTO Student(ExternalId,Name,FamilyName,GivenName,Grade,DefaultMaxConsecutiveSlots,AllowGap,Note,Active) VALUES($key,$name,$family,$given,$grade,$maximum,$gap,$note,$active) ON CONFLICT(ExternalId) DO UPDATE SET Name=excluded.Name,FamilyName=excluded.FamilyName,GivenName=excluded.GivenName,Grade=excluded.Grade,DefaultMaxConsecutiveSlots=excluded.DefaultMaxConsecutiveSlots,AllowGap=excluded.AllowGap,Note=excluded.Note,Active=excluded.Active RETURNING Id;",
                command => { Bind(command, "$key", row.ExternalId); Bind(command, "$name", ComposeFullName(row.FamilyName, row.GivenName)); Bind(command, "$family", row.FamilyName); Bind(command, "$given", row.GivenName); Bind(command, "$grade", row.Grade); Bind(command, "$maximum", row.DefaultMaximum); Bind(command, "$gap", row.AllowGap); Bind(command, "$note", row.Note); Bind(command, "$active", row.Active); },
                cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<Dictionary<string, long>> UpsertTeachersAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<TeacherRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            result[row.ExternalId] = await UpsertIdAsync(connection, transaction,
                "INSERT INTO Teacher(ExternalId,Name,FamilyName,GivenName,AllowGap,Note,Active) VALUES($key,$name,$family,$given,$gap,$note,$active) ON CONFLICT(ExternalId) DO UPDATE SET Name=excluded.Name,FamilyName=excluded.FamilyName,GivenName=excluded.GivenName,AllowGap=excluded.AllowGap,Note=excluded.Note,Active=excluded.Active RETURNING Id;",
                command => { Bind(command, "$key", row.ExternalId); Bind(command, "$name", ComposeFullName(row.FamilyName, row.GivenName)); Bind(command, "$family", row.FamilyName); Bind(command, "$given", row.GivenName); Bind(command, "$gap", row.AllowGap); Bind(command, "$note", row.Note); Bind(command, "$active", row.Active); },
                cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Student/Teacher.FullNameと同じ合成規則（名が空なら姓のみ）。Name列を保存のたびに
    // 計算して維持するため、Domain recordを経由せずここでも同じ規則を複製する。
    private static string ComposeFullName(string familyName, string givenName) => givenName.Length == 0 ? familyName : $"{familyName} {givenName}";

    // 共通名簿Excelの「科目」シートには略称列が無いため、新規科目の略称は表示名から自動推定する
    // （SubjectAbbreviation.Resolve、Python版default_subject_short_name相当）。既存科目を再取込みで
    // 更新する際はShortNameを上書きしない（①設定「科目」タブで手動修正した略称を再取込みで
    // 消さないため。Python版shared_roster_service.pyも既存行のshort_nameは更新時に触れない）。
    private static async Task<Dictionary<string, long>> UpsertSubjectsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<SubjectRow> rows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            result[row.Code] = await UpsertIdAsync(connection, transaction,
                "INSERT INTO Subject(Code,DisplayName,ShortName,SchoolLevel,SortOrder,Active) VALUES($key,$name,$short,$level,$sort,$active) ON CONFLICT(Code) DO UPDATE SET DisplayName=excluded.DisplayName,SchoolLevel=excluded.SchoolLevel,SortOrder=excluded.SortOrder,Active=excluded.Active RETURNING Id;",
                command => { Bind(command, "$key", row.Code); Bind(command, "$name", row.DisplayName); Bind(command, "$short", SubjectAbbreviation.Resolve(row.DisplayName, null, row.Code)); Bind(command, "$level", row.SchoolLevel); Bind(command, "$sort", row.SortOrder); Bind(command, "$active", row.Active); },
                cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task UpsertQualificationsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<QualificationRow> rows, IReadOnlyDictionary<string, long> teachers, IReadOnlyDictionary<string, long> subjects, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO TeacherQualification(TeacherId,SubjectId,CanTeach,Note) VALUES($teacher,$subject,$can,$note) ON CONFLICT(TeacherId,SubjectId) DO UPDATE SET CanTeach=excluded.CanTeach,Note=excluded.Note;";
            Bind(command, "$teacher", teachers[row.TeacherExternalId]); Bind(command, "$subject", subjects[row.SubjectCode]); Bind(command, "$can", row.CanTeach); Bind(command, "$note", row.Note);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertRegularLessonsAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<RegularLessonRow> rows, IReadOnlyDictionary<string, long> students, IReadOnlyDictionary<string, long> teachers, IReadOnlyDictionary<string, long> subjects, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO RegularLessonProfile(ProjectId,StudentId,SubjectId,RegularTeacherId,RegularTeacherPriority,OneToOneRequired,Note) VALUES(1,$student,$subject,$teacher,$priority,$one,$note) ON CONFLICT(ProjectId,StudentId,SubjectId) DO UPDATE SET RegularTeacherId=excluded.RegularTeacherId,RegularTeacherPriority=excluded.RegularTeacherPriority,OneToOneRequired=excluded.OneToOneRequired,Note=excluded.Note;";
            Bind(command, "$student", students[row.StudentExternalId]); Bind(command, "$subject", subjects[row.SubjectCode]);
            Bind(command, "$teacher", row.RegularTeacherExternalId is null ? null : teachers[row.RegularTeacherExternalId]);
            Bind(command, "$priority", row.Priority); Bind(command, "$one", row.OneToOne); Bind(command, "$note", row.Note);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<long> UpsertIdAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; bind(command);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static void Bind(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static async Task SaveImportEvidenceAsync(SqliteConnection connection, SqliteTransaction transaction, string sourcePath, ParsedRoster parsed, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var fileName = Path.GetFileName(sourcePath);
        await using (var batch = connection.CreateCommand())
        {
            batch.Transaction = transaction;
            batch.CommandText = "INSERT INTO ImportBatch(ProjectId,ImportType,SourceFileName,ImportedUtc,RowCount,SuccessCount,WarningCount,ErrorCount,MappingJson) VALUES(1,'shared_roster',$file,$utc,$rows,$rows,$warnings,0,$mapping);";
            Bind(batch, "$file", fileName); Bind(batch, "$utc", now); Bind(batch, "$rows", parsed.RowCount);
            Bind(batch, "$warnings", parsed.Issues.Count(issue => issue.Severity == SharedRosterIssueSeverity.Warning));
            Bind(batch, "$mapping", JsonSerializer.Serialize(new { Sheets = SheetNames }));
            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var snapshot = connection.CreateCommand())
        {
            snapshot.Transaction = transaction;
            snapshot.CommandText = "INSERT INTO ImportSourceSnapshot(ProjectId,ImportType,SourceFileName,Content,Sha256,SizeBytes,ImportedUtc) VALUES(1,'shared_roster',$file,$content,$sha,$size,$utc) ON CONFLICT(ProjectId,ImportType) DO UPDATE SET SourceFileName=excluded.SourceFileName,Content=excluded.Content,Sha256=excluded.Sha256,SizeBytes=excluded.SizeBytes,ImportedUtc=excluded.ImportedUtc;";
            Bind(snapshot, "$file", fileName); Bind(snapshot, "$content", parsed.Content); Bind(snapshot, "$sha", parsed.Sha256); Bind(snapshot, "$size", parsed.Content.LongLength); Bind(snapshot, "$utc", now);
            await snapshot.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source,OperationId) VALUES(1,$utc,'shared_roster_import','master_data','project:1',$summary,'共通名簿Excel取込','import',$operation);";
            Bind(audit, "$utc", now); Bind(audit, "$summary", JsonSerializer.Serialize(new { parsed.RowCount, SheetCount = SheetNames.Length, parsed.Sha256 })); Bind(audit, "$operation", Guid.NewGuid().ToString("N"));
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // ヘッダーには"（必須）"・"（自動・入力不要）"等さまざまな注記が付くため、最初の全角"（"より前だけを
    // canonical名として扱う（複数の注記パターンに対応するため、"（必須）"限定の除去ではなく汎用化した）。
    private static string CanonicalHeader(string header)
    {
        var trimmed = header.Trim();
        var index = trimmed.IndexOf('（');
        return index < 0 ? trimmed : trimmed[..index];
    }

    private static string FormatIssue(SharedRosterIssue issue) => $"{issue.SheetName}{(issue.RowNumber is null ? "" : $" {issue.RowNumber}行")}{(issue.ColumnName is null ? "" : $" [{issue.ColumnName}]")}: {issue.Message}";
    private static SharedRosterIssue Error(string sheet, int? row, string? column, string message) => new(SharedRosterIssueSeverity.Error, sheet, row, column, message);
    private static void RequireXlsxPath(string path) { ArgumentException.ThrowIfNullOrWhiteSpace(path); if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("共通名簿には.xlsxファイルを指定してください。"); }
    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken) { var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString()); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }
    private static void AddDuplicateIssues<T>(IEnumerable<T> rows, Func<T, string> key, string sheet, string column, List<SharedRosterIssue> issues) where T : IRosterRow
    {
        foreach (var group in rows.GroupBy(key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var row in group)
                issues.Add(Error(sheet, row.RowNumber, column, "ファイル内で重複しています。"));
    }

    private static readonly string[] StudentRequiredHeaders = ["在籍", "生徒ID", "氏名", "学年", "標準最大連続コマ数", "空きコマ許可", "備考"];
    private static readonly string[] TeacherRequiredHeaders = ["在籍", "講師ID", "氏名", "空きコマ許可", "備考"];
    private static readonly string[] SubjectRequiredHeaders = ["科目コード", "表示名", "学校段階", "並び順", "有効"];
    private static readonly string[] QualificationRequiredHeaders = ["講師ID", "科目コード", "指導可能", "備考"];
    private static readonly string[] RegularLessonRequiredHeaders = ["生徒ID", "科目コード", "通常担当講師ID", "担当講師優先度", "1対1必須", "備考"];

    private interface IRosterRow { int RowNumber { get; } }
    private sealed record StudentRow(int RowNumber, bool Active, string ExternalId, string FamilyName, string GivenName, string Grade, int DefaultMaximum, bool AllowGap, string Note) : IRosterRow;
    private sealed record TeacherRow(int RowNumber, bool Active, string ExternalId, string FamilyName, string GivenName, bool AllowGap, string Note) : IRosterRow;
    private sealed record SubjectRow(int RowNumber, string Code, string DisplayName, string SchoolLevel, int SortOrder, bool Active) : IRosterRow;
    private sealed record QualificationRow(int RowNumber, string TeacherExternalId, string SubjectCode, bool CanTeach, string Note) : IRosterRow;
    private sealed record RegularLessonRow(int RowNumber, string StudentExternalId, string SubjectCode, string? RegularTeacherExternalId, int Priority, bool OneToOne, string Note) : IRosterRow;

    private sealed class ParsedRoster(string path, string sha256, byte[] content)
    {
        public string Path { get; } = path;
        public string Sha256 { get; } = sha256;
        public byte[] Content { get; } = content;
        public List<SharedRosterIssue> Issues { get; } = [];
        public List<StudentRow> Students { get; } = [];
        public List<TeacherRow> Teachers { get; } = [];
        public List<SubjectRow> Subjects { get; } = [];
        public List<QualificationRow> Qualifications { get; } = [];
        public List<RegularLessonRow> RegularLessons { get; } = [];
        public int RowCount => Students.Count + Teachers.Count + Subjects.Count + Qualifications.Count + RegularLessons.Count;
    }

    private sealed class RowReader(IXLWorksheet sheet, int rowNumber, IReadOnlyDictionary<string, int> headers)
    {
        public int RowNumber => rowNumber;
        public string? Text(string header, bool required)
        {
            var value = Raw(header);
            if (string.IsNullOrWhiteSpace(value)) { if (required) throw Failure(header, "必須項目です。"); return null; }
            return value.Trim();
        }
        public int? Integer(string header, bool required, int? defaultValue, int? minimum = null, int? maximum = null)
        {
            var value = Raw(header);
            if (string.IsNullOrWhiteSpace(value)) { if (defaultValue is not null) return defaultValue; if (required) throw Failure(header, "必須項目です。"); return null; }
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw Failure(header, "整数で入力してください。");
            if (minimum is not null && number < minimum) throw Failure(header, $"{minimum}以上で入力してください。");
            if (maximum is not null && number > maximum) throw Failure(header, $"{maximum}以下で入力してください。");
            return number;
        }
        public bool? Boolean(string header, bool required, bool? defaultValue)
        {
            var value = Raw(header);
            if (string.IsNullOrWhiteSpace(value)) { if (defaultValue is not null) return defaultValue; if (required) throw Failure(header, "必須項目です。"); return null; }
            return value.Trim().ToLowerInvariant() switch
            {
                "true" or "はい" or "1" or "有効" or "可" or "○" or "あり" => true,
                "false" or "いいえ" or "0" or "無効" or "不可" or "×" or "なし" => false,
                _ => throw Failure(header, "真偽値として認識できません。"),
            };
        }
        public bool HasColumn(string header) => headers.ContainsKey(header);
        private string Raw(string header) => headers.TryGetValue(header, out var column) ? sheet.Cell(rowNumber, column).GetString() : string.Empty;
        private RosterCellException Failure(string header, string message) => new(sheet.Name, rowNumber, header, message);
    }

    private sealed class RosterCellException(string sheetName, int rowNumber, string columnName, string message) : Exception(message)
    {
        public string SheetName { get; } = sheetName;
        public int RowNumber { get; } = rowNumber;
        public string ColumnName { get; } = columnName;
    }
}
