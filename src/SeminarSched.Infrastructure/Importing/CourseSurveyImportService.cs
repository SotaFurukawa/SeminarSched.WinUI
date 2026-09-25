using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using SeminarSched.Application.Importing;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Importing;

/// <summary>
/// Python版CourseSurveyServiceの移植。生成済みGoogleフォームの生徒・講師の生回答
/// （質問文そのままの列名、日付ごとの受講/出勤不可日時列、教科ごとの受講教科・受講回数・
/// 学校区分列）を2ファイルまとめて検証し、生徒・講師ごとに受講希望・可用性を全置換する。
/// </summary>
public sealed class CourseSurveyImportService : ICourseSurveyImportService
{
    private const CourseSurveyIssueSeverity Error = CourseSurveyIssueSeverity.Error;
    private const CourseSurveyIssueSeverity Warning = CourseSurveyIssueSeverity.Warning;
    private static readonly Regex DatePattern = new(@"(20\d{2})[-/年](\d{1,2})[-/月](\d{1,2})", RegexOptions.Compiled);

    // Python版のgrade_from_excel相当。フォームの学年選択肢は内部表記（"中1"等）をそのまま使うため、
    // Excel短縮コードが来た場合のみ変換し、それ以外はそのまま通す。
    private static readonly Dictionary<string, string> GradeFromExcelCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S1"] = "小1", ["S2"] = "小2", ["S3"] = "小3", ["S4"] = "小4", ["S5"] = "小5", ["S6"] = "小6",
        ["J1"] = "中1", ["J2"] = "中2", ["J3"] = "中3",
        ["H1"] = "高1", ["H2"] = "高2", ["H3"] = "高3",
    };

    public Task<CourseSurveyPreview> PreviewAsync(string projectPath, string studentPath, string teacherPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Preview(projectPath, studentPath, teacherPath), cancellationToken);

    public async Task<CourseSurveyApplyResult> ApplyAsync(string projectPath, CourseSurveyPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.HasErrors) throw new InvalidOperationException("エラーがあるため反映できません。");
        if (Hash(preview.StudentPath) != preview.StudentSha256 || Hash(preview.TeacherPath) != preview.TeacherSha256)
            throw new InvalidOperationException("検証後に回答ファイルが変更されました。再度検証してください。");

        var studentTable = ReadTable(preview.StudentPath);
        var teacherTable = ReadTable(preview.TeacherPath);

        await using var connection = await OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
        await SqliteProjectSchema.EnsureCurrentAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transactionRaw = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (SqliteTransaction)transactionRaw;
        try
        {
            if (CountAssignments(connection, transaction) > 0)
                throw new InvalidOperationException("時間割配置後は回答の一括置換ができません。先に時間割を未配置へ戻すか、従来の差分取込を使用してください。");

            var openDates = LoadOpenDates(connection, transaction);
            var slots = LoadActiveSlots(connection, transaction);
            var masterStudents = LoadActiveStudents(connection, transaction);
            var masterTeachers = LoadActiveTeachers(connection, transaction);
            var subjects = LoadActiveSubjects(connection, transaction);
            var profiles = LoadRegularLessonProfiles(connection, transaction);
            var openDateSet = openDates.Select(date => date.Date).ToHashSet();

            var reparseIssues = new List<CourseSurveyIssue>();
            var students = ParseStudentResponses(studentTable, openDateSet, slots, masterStudents, subjects, reparseIssues);
            var teachers = ParseTeacherResponses(teacherTable, openDateSet, slots, masterTeachers, reparseIssues);
            if (reparseIssues.Any(issue => issue.Severity == Error))
                throw new InvalidOperationException("反映直前の再検証でエラーが見つかりました。もう一度検証してください。" + Environment.NewLine + FormatIssue(reparseIssues.First(issue => issue.Severity == Error)));

            var studentByKey = masterStudents.ToDictionary(student => (NameKey(student.Name), student.Grade), student => student);
            var teacherByName = masterTeachers.ToDictionary(teacher => NameKey(teacher.Name), teacher => teacher);
            var subjectByName = subjects.ToDictionary(subject => TextKey(subject.DisplayName), subject => subject);

            var trialCount = 0;
            var requestCount = 0;
            foreach (var response in students)
            {
                var key = (NameKey(response.Name), response.Grade);
                if (!studentByKey.TryGetValue(key, out var student))
                {
                    if (response.EnrollmentType != "体験生") throw new InvalidOperationException($"未登録の在籍生です: {response.Name}");
                    var externalId = NextTrialId(connection, transaction);
                    var id = InsertTrialStudent(connection, transaction, externalId, response.Name, response.Grade);
                    student = new StudentEntity(id, response.Name, response.Grade);
                    studentByKey[key] = student;
                    trialCount++;
                }
                DeleteStudentAvailability(connection, transaction, student.Id);
                DeleteLessonRequests(connection, transaction, student.Id);
                foreach (var (subjectName, requiredSessions) in response.Requests)
                {
                    var subject = subjectByName[TextKey(subjectName)];
                    profiles.TryGetValue((student.Id, subject.Id), out var profile);
                    InsertLessonRequest(connection, transaction, student.Id, subject.Id, requiredSessions, profile, response.Note);
                    requestCount++;
                }
                InsertStudentAvailability(connection, transaction, student.Id, openDates, slots, response.Unavailable);
            }
            foreach (var response in teachers)
            {
                var teacher = teacherByName[NameKey(response.Name)];
                ReplaceTeacherAvailability(connection, transaction, teacher.Id, openDates, slots, response.Unavailable);
            }

            var warningCount = preview.Issues.Count(issue => issue.Severity == Warning);
            await SaveImportEvidenceAsync(connection, transaction, preview.StudentPath, preview.TeacherPath, preview.StudentSha256, preview.TeacherSha256, students.Count, teachers.Count, requestCount, warningCount, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CourseSurveyApplyResult(students.Count, teachers.Count, requestCount, trialCount, warningCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static CourseSurveyPreview Preview(string projectPath, string studentPath, string teacherPath)
    {
        if (!File.Exists(studentPath)) throw new FileNotFoundException("生徒回答ファイルが見つかりません。", studentPath);
        if (!File.Exists(teacherPath)) throw new FileNotFoundException("講師回答ファイルが見つかりません。", teacherPath);
        var studentTable = ReadTable(studentPath);
        var teacherTable = ReadTable(teacherPath);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(projectPath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        var openDates = LoadOpenDates(connection);
        var slots = LoadActiveSlots(connection);
        var students = LoadActiveStudents(connection);
        var teachers = LoadActiveTeachers(connection);
        var subjects = LoadActiveSubjects(connection);
        var openDateSet = openDates.Select(date => date.Date).ToHashSet();

        var issues = new List<CourseSurveyIssue>();
        var studentResponses = ParseStudentResponses(studentTable, openDateSet, slots, students, subjects, issues);
        var teacherResponses = ParseTeacherResponses(teacherTable, openDateSet, slots, teachers, issues);
        var requestCount = studentResponses.Sum(response => response.Requests.Count);
        return new CourseSurveyPreview(Path.GetFullPath(studentPath), Path.GetFullPath(teacherPath), Hash(studentPath), Hash(teacherPath), studentResponses.Count, teacherResponses.Count, requestCount, issues);
    }

    private static List<StudentResponse> ParseStudentResponses(RawTable table, HashSet<DateOnly> openDates, IReadOnlyList<SlotInfo> slots, IReadOnlyList<StudentEntity> masterStudents, IReadOnlyList<SubjectEntity> subjects, List<CourseSurveyIssue> issues)
    {
        var surnameHeader = FindHeader(table.Headers, "姓（苗字）");
        var givenHeader = FindHeader(table.Headers, "名（必須）");
        var gradeHeader = FindHeader(table.Headers, "学年（必須）");
        var enrollmentHeader = FindHeader(table.Headers, "在籍区分");
        RequireHeaders([(surnameHeader, "姓"), (givenHeader, "名"), (gradeHeader, "学年")], "生徒回答");
        var requestColumns = StudentRequestColumns(table.Headers);
        var dateHeaders = DateHeaders(table.Headers, "受講不可日時");
        ValidateDates(dateHeaders, openDates, "生徒回答", issues);
        var knownStudents = masterStudents.Select(student => (NameKey(student.Name), student.Grade)).ToHashSet();
        var knownSubjects = subjects.Select(subject => TextKey(subject.DisplayName)).ToHashSet();
        var slotCodes = slots.Select(slot => slot.Code).ToArray();

        var result = new List<StudentResponse>();
        var seen = new HashSet<(string, string)>();
        foreach (var row in table.Rows)
        {
            var values = row.Values;
            var name = FullName(values.GetValueOrDefault(surnameHeader, ""), values.GetValueOrDefault(givenHeader, ""));
            var grade = ConvertGrade(Text(values.GetValueOrDefault(gradeHeader, "")));
            var enrollment = Text(values.GetValueOrDefault(enrollmentHeader, ""));
            if (enrollment.Length == 0) enrollment = "在籍生";
            var identity = (NameKey(name), grade);
            if (!seen.Add(identity))
                issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, "同じ生徒の回答が重複しています。", "Googleフォーム側で正しい1回答だけを残す"));
            if (!knownStudents.Contains(identity))
            {
                issues.Add(enrollment == "体験生"
                    ? Issue(Warning, "生徒回答", row.RowNumber, name, "基本情報にない体験生です。プロジェクト内だけに自動登録します。", "体験生として登録")
                    : Issue(Error, "生徒回答", row.RowNumber, name, "生徒・講師の基本情報に一致する在籍生がいません。", "共通名簿へ追加して再反映、または回答を体験生へ修正"));
            }

            var requests = new List<(string Subject, int Count)>();
            var requestKeys = new HashSet<string>();
            var automaticRequestNumbers = requestColumns
                .Where(column => column.SchoolHeader.Length == 0 && SchoolLevelInHeader(column.SubjectHeader).Length > 0
                    && (Text(values.GetValueOrDefault(column.SubjectHeader, "")).Length > 0 || Text(values.GetValueOrDefault(column.CountHeader, "")).Length > 0))
                .Select(column => QuestionNumber(column.SubjectHeader))
                .ToHashSet();
            foreach (var (subjectHeader, countHeader, schoolHeader) in requestColumns)
            {
                var subjectName = Text(values.GetValueOrDefault(subjectHeader, ""));
                var countText = Text(values.GetValueOrDefault(countHeader, ""));
                var schoolLevel = schoolHeader.Length > 0 ? Text(values.GetValueOrDefault(schoolHeader, "")) : "";
                if (subjectName.Length == 0 && countText.Length == 0 && schoolLevel.Length == 0) continue;
                if (subjectName.Length == 0 && countText.Length == 0 && schoolLevel.Length > 0 && automaticRequestNumbers.Contains(QuestionNumber(subjectHeader))) continue;
                if (subjectName.Length == 0 || countText.Length == 0)
                {
                    issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, "受講教科と受講回数は組で入力してください。", "フォーム回答を修正"));
                    continue;
                }
                if (schoolHeader.Length > 0 && schoolLevel is not ("小学校" or "中学校" or "高校"))
                {
                    issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, "学校区分は小学校・中学校・高校から選択してください。", "フォーム回答を修正"));
                    continue;
                }
                var count = int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) ? parsedCount : 0;
                if (count < 1) issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, "受講回数は1以上で指定してください。", "フォーム回答を修正"));
                var canonical = CanonicalQuestionnaireSubject(subjectName, subjectHeader, schoolLevel);
                var key = TextKey(canonical);
                if (!knownSubjects.Contains(key)) issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, $"未登録の科目です: {subjectName}", "フォームをアプリから再生成するか科目名を修正"));
                if (!requestKeys.Add(key)) issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, $"同じ科目が重複しています: {subjectName}", "重複する受講教科を修正"));
                requests.Add((canonical, count));
            }
            if (requests.Count == 0) issues.Add(Issue(Error, "生徒回答", row.RowNumber, name, "受講教科が1件もありません。", "少なくとも1教科を回答"));
            result.Add(new StudentResponse(row.RowNumber, name, grade, enrollment, requests, Unavailable(values, dateHeaders, slotCodes), FindNote(table.Headers, values, ["特記事項"])));
        }
        return result;
    }

    private static List<TeacherResponse> ParseTeacherResponses(RawTable table, HashSet<DateOnly> openDates, IReadOnlyList<SlotInfo> slots, IReadOnlyList<TeacherEntity> masterTeachers, List<CourseSurveyIssue> issues)
    {
        var surnameHeader = FindHeader(table.Headers, "姓（苗字）");
        var givenHeader = FindHeader(table.Headers, "名（必須）");
        RequireHeaders([(surnameHeader, "姓"), (givenHeader, "名")], "講師回答");
        var dateHeaders = DateHeaders(table.Headers, "出勤不可日時");
        ValidateDates(dateHeaders, openDates, "講師回答", issues);
        var known = masterTeachers.Select(teacher => NameKey(teacher.Name)).ToHashSet();
        var slotCodes = slots.Select(slot => slot.Code).ToArray();

        var result = new List<TeacherResponse>();
        var seen = new HashSet<string>();
        foreach (var row in table.Rows)
        {
            var values = row.Values;
            var name = FullName(values.GetValueOrDefault(surnameHeader, ""), values.GetValueOrDefault(givenHeader, ""));
            var key = NameKey(name);
            if (!seen.Add(key)) issues.Add(Issue(Error, "講師回答", row.RowNumber, name, "同じ講師の回答が重複しています。", "Googleフォーム側で正しい1回答だけを残す"));
            if (!known.Contains(key)) issues.Add(Issue(Error, "講師回答", row.RowNumber, name, "生徒・講師の基本情報に一致する講師がいません。", "共通名簿へ追加して再反映、または氏名を修正"));
            result.Add(new TeacherResponse(row.RowNumber, name, Unavailable(values, dateHeaders, slotCodes), FindNote(table.Headers, values, ["勤務に関する特記事項", "特記事項"])));
        }
        return result;
    }

    private static string CanonicalQuestionnaireSubject(string value, string header, string schoolLevel)
    {
        var normalized = value.Replace("（中学受験以外）", "（中学受験以外なら可能）");
        var selectedPrefix = schoolLevel switch { "小学校" => "小学校・", "中学校" => "中学校・", "高校" => "高校・", _ => null };
        if (selectedPrefix is not null)
        {
            foreach (var existingPrefix in new[] { "小学校・", "中学校・", "高校・" })
                if (normalized.StartsWith(existingPrefix, StringComparison.Ordinal)) { normalized = normalized[existingPrefix.Length..]; break; }
            return selectedPrefix + normalized;
        }
        if (normalized.StartsWith("小学校・", StringComparison.Ordinal) || normalized.StartsWith("中学校・", StringComparison.Ordinal) || normalized.StartsWith("高校・", StringComparison.Ordinal))
            return normalized;
        if (header.Contains("他学年", StringComparison.Ordinal))
        {
            var match = Regex.Match(normalized, "^(.+)[(（]([小中高])[)）]$");
            if (match.Success)
            {
                var prefix = match.Groups[2].Value switch { "小" => "小学校・", "中" => "中学校・", "高" => "高校・", _ => "" };
                return prefix + match.Groups[1].Value;
            }
        }
        foreach (var (marker, prefix) in new[] { ("小学校", "小学校・"), ("中学校", "中学校・"), ("高校", "高校・") })
            if (header.Contains(marker, StringComparison.Ordinal)) return prefix + normalized;
        return normalized;
    }

    private static List<(string SubjectHeader, string CountHeader, string SchoolHeader)> StudentRequestColumns(string[] headers)
    {
        var subjectHeaders = headers.Where(header => header.Contains("受講教科（", StringComparison.Ordinal)).ToArray();
        var countHeaders = headers.Where(header => header.Contains("受講回数（", StringComparison.Ordinal)).ToArray();
        var schoolHeaders = headers.Where(header => header.Contains("学校区分（", StringComparison.Ordinal)).ToArray();
        if (subjectHeaders.Length == 0 || countHeaders.Length == 0) throw new InvalidDataException("生徒回答の受講教科・受講回数列を判別できません。");

        (int Number, string School) Signature(string header) => (QuestionNumber(header), SchoolLevelInHeader(header));

        var countsBySignature = new Dictionary<(int, string), List<string>>();
        foreach (var header in countHeaders)
        {
            var key = Signature(header);
            if (!countsBySignature.TryGetValue(key, out var list)) countsBySignature[key] = list = [];
            list.Add(header);
        }
        var schoolsByNumber = new Dictionary<int, List<string>>();
        foreach (var header in schoolHeaders)
        {
            var number = QuestionNumber(header);
            if (!schoolsByNumber.TryGetValue(number, out var list)) schoolsByNumber[number] = list = [];
            list.Add(header);
        }

        var columns = new List<(string, string, string)>();
        var usedCounts = new HashSet<string>();
        var usedSchools = new HashSet<string>();
        foreach (var subjectHeader in subjectHeaders)
        {
            var (number, automaticSchoolLevel) = Signature(subjectHeader);
            var matchingCounts = countsBySignature.GetValueOrDefault((number, automaticSchoolLevel), []);
            if (number == 0 || matchingCounts.Count != 1) throw new InvalidDataException("生徒回答の受講教科・受講回数列の組合せが不正です。");
            var countHeader = matchingCounts[0];
            if (!usedCounts.Add(countHeader)) throw new InvalidDataException("生徒回答の受講教科・受講回数列の組合せが不正です。");

            var schoolHeader = "";
            if (automaticSchoolLevel.Length == 0 && schoolHeaders.Length > 0)
            {
                var matchingSchools = schoolsByNumber.GetValueOrDefault(number, []);
                if (matchingSchools.Count != 1) throw new InvalidDataException("生徒回答の学校区分・受講教科・受講回数列の組合せが不正です。");
                schoolHeader = matchingSchools[0];
                usedSchools.Add(schoolHeader);
            }
            columns.Add((subjectHeader, countHeader, schoolHeader));
        }
        if (!usedCounts.SetEquals(countHeaders) || (schoolHeaders.Length > 0 && !usedSchools.SetEquals(schoolHeaders)))
            throw new InvalidDataException("生徒回答の学校区分・受講教科・受講回数列の組合せが不正です。");
        return columns;
    }

    private static string SchoolLevelInHeader(string header)
    {
        foreach (var level in new[] { "小学校", "中学校", "高校" }) if (header.Contains(level, StringComparison.Ordinal)) return level;
        return "";
    }

    private static int QuestionNumber(string header)
    {
        var match = Regex.Match(header, @"(\d+)教科目");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static Dictionary<string, DateOnly> DateHeaders(string[] headers, string marker)
    {
        var result = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            if (!header.Contains(marker, StringComparison.Ordinal)) continue;
            var match = DatePattern.Match(header);
            if (match.Success)
                result[header] = new DateOnly(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        return result;
    }

    private static void ValidateDates(Dictionary<string, DateOnly> headers, HashSet<DateOnly> openDates, string source, List<CourseSurveyIssue> issues)
    {
        var represented = headers.Values.ToHashSet();
        foreach (var missing in openDates.Except(represented).OrderBy(date => date))
            issues.Add(Issue(Error, source, 1, "", $"開校日 {missing:yyyy-MM-dd} の不可時間列がありません。", "現在のプロジェクトからGoogleフォームを再生成"));
        foreach (var extra in represented.Except(openDates).OrderBy(date => date))
            issues.Add(Issue(Warning, source, 1, "", $"現在は開校日でない {extra:yyyy-MM-dd} の回答列は無視します。", "対応不要"));
    }

    private static HashSet<(DateOnly Date, string Slot)> Unavailable(IReadOnlyDictionary<string, string> values, Dictionary<string, DateOnly> dateHeaders, string[] slotCodes)
    {
        var result = new HashSet<(DateOnly, string)>();
        foreach (var (header, day) in dateHeaders)
        {
            var cell = Text(values.GetValueOrDefault(header, ""));
            if (cell.Length == 0) continue;
            foreach (var slot in slotCodes)
            {
                var pattern = $@"(?:^|[,、;\s]){Regex.Escape(slot)}(?:$|[,、;\s])";
                if (Regex.IsMatch(cell, pattern)) result.Add((day, slot));
            }
        }
        return result;
    }

    private static string FindHeader(string[] headers, string marker) => headers.FirstOrDefault(header => header.Contains(marker, StringComparison.Ordinal)) ?? "";

    private static void RequireHeaders((string Header, string Label)[] headers, string source)
    {
        var missing = headers.Where(header => header.Header.Length == 0).Select(header => header.Label).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"{source}に必要な列がありません: {string.Join("、", missing)}");
    }

    private static string FindNote(string[] headers, IReadOnlyDictionary<string, string> values, string[] markers)
    {
        foreach (var header in headers)
            if (markers.Any(marker => header.Contains(marker, StringComparison.Ordinal))) return Text(values.GetValueOrDefault(header, ""));
        return "";
    }

    private static string Text(string? value) => value?.Trim() ?? "";
    private static string FullName(string surname, string given) => string.Join(" ", new[] { Text(surname), Text(given) }.Where(value => value.Length > 0));
    private static string NameKey(string value) => new(value.Where(character => !char.IsWhiteSpace(character)).Select(char.ToLowerInvariant).ToArray());
    private static string TextKey(string value) => NameKey(value);
    private static string ConvertGrade(string value) => GradeFromExcelCode.TryGetValue(value.Trim(), out var converted) ? converted : value.Trim();
    private static CourseSurveyIssue Issue(CourseSurveyIssueSeverity severity, string source, int row, string name, string message, string resolution) => new(severity, source, row, name, message, resolution);
    private static string FormatIssue(CourseSurveyIssue issue) => $"{issue.Source} 行{issue.Row} {issue.PersonName}: {issue.Message}";
    private static string JoinedNote(string? profileNote, string responseNote) => string.Join(" / ", new[] { profileNote ?? "", responseNote }.Select(part => part.Trim()).Where(part => part.Length > 0));

    // --- DB reads (プレビューと反映で共有する読取専用ヘルパー) ---

    private static List<StudentEntity> LoadActiveStudents(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Id,Name,Grade FROM Student WHERE Active=1;";
        using var reader = command.ExecuteReader();
        var result = new List<StudentEntity>();
        while (reader.Read()) result.Add(new StudentEntity(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static List<TeacherEntity> LoadActiveTeachers(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Id,Name FROM Teacher WHERE Active=1;";
        using var reader = command.ExecuteReader();
        var result = new List<TeacherEntity>();
        while (reader.Read()) result.Add(new TeacherEntity(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    private static List<SubjectEntity> LoadActiveSubjects(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Id,DisplayName FROM Subject WHERE Active=1;";
        using var reader = command.ExecuteReader();
        var result = new List<SubjectEntity>();
        while (reader.Read()) result.Add(new SubjectEntity(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    private static List<SlotInfo> LoadActiveSlots(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Id,Code FROM TimeSlot WHERE Active=1 ORDER BY SortOrder;";
        using var reader = command.ExecuteReader();
        var result = new List<SlotInfo>();
        while (reader.Read()) result.Add(new SlotInfo(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    private static List<OpenDateInfo> LoadOpenDates(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Id,Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";
        using var reader = command.ExecuteReader();
        var result = new List<OpenDateInfo>();
        while (reader.Read()) result.Add(new OpenDateInfo(reader.GetInt64(0), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return result;
    }

    private static Dictionary<(long StudentId, long SubjectId), RegularProfile> LoadRegularLessonProfiles(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT StudentId,SubjectId,RegularTeacherId,RegularTeacherPriority,OneToOneRequired,Note FROM RegularLessonProfile WHERE ProjectId=1;";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<(long, long), RegularProfile>();
        while (reader.Read())
            result[(reader.GetInt64(0), reader.GetInt64(1))] = new RegularProfile(reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4) == 1, reader.GetString(5));
        return result;
    }

    private static long CountAssignments(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM Assignment a JOIN LessonRequest lr ON lr.Id=a.LessonRequestId WHERE lr.ProjectId=1;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string NextTrialId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT ExternalId FROM Student;";
        using var reader = command.ExecuteReader();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) existing.Add(reader.GetString(0));
        var number = 1;
        while (existing.Contains($"TRIAL-{number:D4}")) number++;
        return $"TRIAL-{number:D4}";
    }

    // --- DB writes (反映のみで使用) ---

    private static long InsertTrialStudent(SqliteConnection connection, SqliteTransaction transaction, string externalId, string name, string grade)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Student(ExternalId,Name,Grade,DefaultMaxConsecutiveSlots,AllowGap,Note,Active) VALUES($id,$name,$grade,2,0,$note,1) RETURNING Id;";
        Bind(command, "$id", externalId); Bind(command, "$name", name); Bind(command, "$grade", grade); Bind(command, "$note", "在籍区分: 体験生（アンケート取込で作成）");
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void DeleteStudentAvailability(SqliteConnection connection, SqliteTransaction transaction, long studentId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "DELETE FROM StudentAvailability WHERE ProjectId=1 AND StudentId=$id;";
        Bind(command, "$id", studentId); command.ExecuteNonQuery();
    }

    private static void DeleteLessonRequests(SqliteConnection connection, SqliteTransaction transaction, long studentId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "DELETE FROM LessonRequest WHERE ProjectId=1 AND StudentId=$id;";
        Bind(command, "$id", studentId); command.ExecuteNonQuery();
    }

    private static void InsertLessonRequest(SqliteConnection connection, SqliteTransaction transaction, long studentId, long subjectId, int requiredSessions, RegularProfile? profile, string responseNote)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,RegularTeacherId,RegularTeacherPriority,OneToOneRequired,Note) VALUES(1,$student,$subject,$count,$teacher,$priority,$one,$note);";
        Bind(command, "$student", studentId); Bind(command, "$subject", subjectId); Bind(command, "$count", requiredSessions);
        Bind(command, "$teacher", profile?.RegularTeacherId); Bind(command, "$priority", profile?.Priority ?? 3); Bind(command, "$one", profile?.OneToOne ?? false);
        Bind(command, "$note", JoinedNote(profile?.Note, responseNote));
        command.ExecuteNonQuery();
    }

    private static void InsertStudentAvailability(SqliteConnection connection, SqliteTransaction transaction, long studentId, IReadOnlyList<OpenDateInfo> openDates, IReadOnlyList<SlotInfo> slots, HashSet<(DateOnly Date, string Slot)> unavailable)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO StudentAvailability(ProjectId,StudentId,OpenDateId,TimeSlotId,AvailabilityLevel) VALUES(1,$student,$date,$slot,$level);";
        var studentParam = command.CreateParameter(); studentParam.ParameterName = "$student"; studentParam.Value = studentId; command.Parameters.Add(studentParam);
        var dateParam = command.CreateParameter(); dateParam.ParameterName = "$date"; command.Parameters.Add(dateParam);
        var slotParam = command.CreateParameter(); slotParam.ParameterName = "$slot"; command.Parameters.Add(slotParam);
        var levelParam = command.CreateParameter(); levelParam.ParameterName = "$level"; command.Parameters.Add(levelParam);
        foreach (var day in openDates)
            foreach (var slot in slots)
            {
                dateParam.Value = day.Id; slotParam.Value = slot.Id; levelParam.Value = unavailable.Contains((day.Date, slot.Code)) ? 0 : 1;
                command.ExecuteNonQuery();
            }
    }

    private static void ReplaceTeacherAvailability(SqliteConnection connection, SqliteTransaction transaction, long teacherId, IReadOnlyList<OpenDateInfo> openDates, IReadOnlyList<SlotInfo> slots, HashSet<(DateOnly Date, string Slot)> unavailable)
    {
        using (var clearAvailability = connection.CreateCommand()) { clearAvailability.Transaction = transaction; clearAvailability.CommandText = "DELETE FROM TeacherAvailability WHERE ProjectId=1 AND TeacherId=$id;"; Bind(clearAvailability, "$id", teacherId); clearAvailability.ExecuteNonQuery(); }
        using (var clearUnavailable = connection.CreateCommand()) { clearUnavailable.Transaction = transaction; clearUnavailable.CommandText = "DELETE FROM TeacherUnavailability WHERE TeacherId=$id;"; Bind(clearUnavailable, "$id", teacherId); clearUnavailable.ExecuteNonQuery(); }

        using var insertAvailability = connection.CreateCommand();
        insertAvailability.Transaction = transaction;
        insertAvailability.CommandText = "INSERT INTO TeacherAvailability(ProjectId,TeacherId,OpenDateId,TimeSlotId,AvailabilityLevel) VALUES(1,$teacher,$date,$slot,$level);";
        var availabilityTeacherParam = insertAvailability.CreateParameter(); availabilityTeacherParam.ParameterName = "$teacher"; availabilityTeacherParam.Value = teacherId; insertAvailability.Parameters.Add(availabilityTeacherParam);
        var availabilityDateParam = insertAvailability.CreateParameter(); availabilityDateParam.ParameterName = "$date"; insertAvailability.Parameters.Add(availabilityDateParam);
        var availabilitySlotParam = insertAvailability.CreateParameter(); availabilitySlotParam.ParameterName = "$slot"; insertAvailability.Parameters.Add(availabilitySlotParam);
        var availabilityLevelParam = insertAvailability.CreateParameter(); availabilityLevelParam.ParameterName = "$level"; insertAvailability.Parameters.Add(availabilityLevelParam);

        using var insertUnavailable = connection.CreateCommand();
        insertUnavailable.Transaction = transaction;
        insertUnavailable.CommandText = "INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId) VALUES($teacher,$date,$slot);";
        var unavailableTeacherParam = insertUnavailable.CreateParameter(); unavailableTeacherParam.ParameterName = "$teacher"; unavailableTeacherParam.Value = teacherId; insertUnavailable.Parameters.Add(unavailableTeacherParam);
        var unavailableDateParam = insertUnavailable.CreateParameter(); unavailableDateParam.ParameterName = "$date"; insertUnavailable.Parameters.Add(unavailableDateParam);
        var unavailableSlotParam = insertUnavailable.CreateParameter(); unavailableSlotParam.ParameterName = "$slot"; insertUnavailable.Parameters.Add(unavailableSlotParam);

        foreach (var day in openDates)
            foreach (var slot in slots)
            {
                var level = unavailable.Contains((day.Date, slot.Code)) ? 0 : 1;
                availabilityDateParam.Value = day.Id; availabilitySlotParam.Value = slot.Id; availabilityLevelParam.Value = level;
                insertAvailability.ExecuteNonQuery();
                if (level == 0)
                {
                    unavailableDateParam.Value = day.Id; unavailableSlotParam.Value = slot.Id;
                    insertUnavailable.ExecuteNonQuery();
                }
            }
    }

    private static async Task SaveImportEvidenceAsync(SqliteConnection connection, SqliteTransaction transaction, string studentPath, string teacherPath, string studentSha256, string teacherSha256, int studentCount, int teacherCount, int requestCount, int warningCount, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var (importType, path, sha256) in new[] { ("student_availability", studentPath, studentSha256), ("teacher_availability", teacherPath, teacherSha256) })
        {
            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            await using var snapshot = connection.CreateCommand();
            snapshot.Transaction = transaction;
            snapshot.CommandText = "INSERT INTO ImportSourceSnapshot(ProjectId,ImportType,SourceFileName,Content,Sha256,SizeBytes,ImportedUtc) VALUES(1,$type,$name,$content,$sha,$size,$utc) ON CONFLICT(ProjectId,ImportType) DO UPDATE SET SourceFileName=excluded.SourceFileName,Content=excluded.Content,Sha256=excluded.Sha256,SizeBytes=excluded.SizeBytes,ImportedUtc=excluded.ImportedUtc;";
            Bind(snapshot, "$type", importType); Bind(snapshot, "$name", Path.GetFileName(path)); Bind(snapshot, "$content", content); Bind(snapshot, "$sha", sha256); Bind(snapshot, "$size", content.LongLength); Bind(snapshot, "$utc", now);
            await snapshot.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var batch = connection.CreateCommand())
        {
            batch.Transaction = transaction;
            batch.CommandText = "INSERT INTO ImportBatch(ProjectId,ImportType,SourceFileName,ImportedUtc,RowCount,SuccessCount,WarningCount,ErrorCount,MappingJson) VALUES(1,'combined_course_survey',$name,$utc,$rows,$rows,$warnings,0,$mapping);";
            Bind(batch, "$name", "講習アンケート統合"); Bind(batch, "$utc", now); Bind(batch, "$rows", studentCount + teacherCount); Bind(batch, "$warnings", warningCount);
            Bind(batch, "$mapping", JsonSerializer.Serialize(new { format = "raw_google_forms_v1" }));
            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO AuditLog(ProjectId,TimestampUtc,Action,EntityType,EntityId,AfterJson,Reason,Source) VALUES(1,$utc,'combined_import','course_survey','project:1',$after,'生徒・講師Googleフォーム回答の一括取込','import');";
        Bind(audit, "$utc", now); Bind(audit, "$after", JsonSerializer.Serialize(new { students = studentCount, teachers = teacherCount, lessonRequests = requestCount }));
        await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken) { var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString()); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); return connection; }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }

    // --- 生CSV/XLSX読み取り（重複ヘッダーを許容する） ---

    private static RawTable ReadTable(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => ReadCsvTable(path),
        ".xlsx" => ReadWorkbookTable(path),
        _ => throw new InvalidDataException("取込にはCSVまたはXLSXファイルを指定してください。"),
    };

    private static RawTable ReadCsvTable(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = DetectEncoding(File.ReadAllBytes(path));
        using var parser = new TextFieldParser(path, encoding) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = DedupeHeaders(parser.ReadFields() ?? []);
        var rows = new List<RawRow>();
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            var values = parser.ReadFields() ?? [];
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new RawRow(rowNumber, ToRowValues(headers, values)));
        }
        return new RawTable(headers, rows);
    }

    private static RawTable ReadWorkbookTable(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.FirstOrDefault(worksheet => worksheet.LastRowUsed() is not null) ?? throw new InvalidDataException("XLSXにデータのあるsheetがありません。");
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var headers = DedupeHeaders(Enumerable.Range(1, lastColumn).Select(column => sheet.Cell(1, column).GetString().Trim()).ToArray());
        var rows = new List<RawRow>();
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var values = Enumerable.Range(1, lastColumn).Select(column => sheet.Cell(rowNumber, column).GetFormattedString().Trim()).ToArray();
            if (values.All(value => value.Length == 0)) continue;
            rows.Add(new RawRow(rowNumber, ToRowValues(headers, values)));
        }
        return new RawTable(headers, rows);
    }

    private static Dictionary<string, string> ToRowValues(string[] headers, string[] values)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < headers.Length; index++) map[headers[index]] = index < values.Length ? values[index] : string.Empty;
        return map;
    }

    // Google Formsは複数の分岐ページで同一の質問文を出力することがあるため、固定形式importerと
    // 異なりヘッダー重複をエラーにせず、Python版のreaders.pyと同じ命名規則で区別する。
    private static string[] DedupeHeaders(string[] rawHeaders)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new string[rawHeaders.Length];
        for (var index = 0; index < rawHeaders.Length; index++)
        {
            var header = rawHeaders[index].Trim();
            if (header.Length == 0) header = $"__column_{index + 1}";
            if (!seen.Add(header))
            {
                var duplicateNumber = 2;
                string candidate;
                do { candidate = $"{header} [重複{duplicateNumber}]"; duplicateNumber++; } while (!seen.Add(candidate));
                header = candidate;
            }
            result[index] = header;
        }
        return result;
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

    private sealed record RawRow(int RowNumber, IReadOnlyDictionary<string, string> Values);
    private sealed record RawTable(string[] Headers, List<RawRow> Rows);

    private sealed record StudentEntity(long Id, string Name, string Grade);
    private sealed record TeacherEntity(long Id, string Name);
    private sealed record SubjectEntity(long Id, string DisplayName);
    private sealed record SlotInfo(long Id, string Code);
    private sealed record OpenDateInfo(long Id, DateOnly Date);
    private sealed record RegularProfile(long? RegularTeacherId, int Priority, bool OneToOne, string Note);

    private sealed record StudentResponse(int RowNumber, string Name, string Grade, string EnrollmentType, List<(string Subject, int Count)> Requests, HashSet<(DateOnly Date, string Slot)> Unavailable, string Note);
    private sealed record TeacherResponse(int RowNumber, string Name, HashSet<(DateOnly Date, string Slot)> Unavailable, string Note);
}
