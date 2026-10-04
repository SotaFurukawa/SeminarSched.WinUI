using System.Text;
using Microsoft.Data.Sqlite;
using SeminarSched.Application.Importing;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.Importing;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class CourseSurveyImportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewAndApply_RawGoogleFormsResponses_ImportsStudentAndTeacher()
    {
        var state = await CreateStateAsync();
        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        WriteStudentCsv(studentPath, "架空", "太郎", "中2", "在籍生", "中学校", "英語", "2", "生徒備考",
            [(state.Date1, "1 09:00-10:00"), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "講師備考",
            [(state.Date1, ""), (state.Date2, "2 10:10-11:10")]);

        var service = new CourseSurveyImportService();
        var preview = await service.PreviewAsync(state.Path, studentPath, teacherPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        Assert.Equal(1, preview.StudentCount); Assert.Equal(1, preview.TeacherCount); Assert.Equal(1, preview.RequestCount);

        var result = await service.ApplyAsync(state.Path, preview);
        Assert.Equal(1, result.Students); Assert.Equal(1, result.Teachers); Assert.Equal(1, result.LessonRequests); Assert.Equal(0, result.TrialStudents);

        await using var connection = new SqliteConnection($"Data Source={state.Path};Pooling=False");
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM LessonRequest r JOIN Subject s ON s.Id=r.SubjectId WHERE s.Code='JH_ENG' AND r.RequiredSessions=2 AND r.Note='生徒備考'"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId={state.StudentId} AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date='{state.Date1:yyyy-MM-dd}') AND TimeSlotId={state.Slot1Id}"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId={state.StudentId} AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date='{state.Date1:yyyy-MM-dd}') AND TimeSlotId={state.Slot2Id}"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT AvailabilityLevel FROM TeacherAvailability WHERE TeacherId={state.TeacherId} AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date='{state.Date2:yyyy-MM-dd}') AND TimeSlotId={state.Slot2Id}"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM TeacherUnavailability"));
    }

    // ユーザー報告「アンケート取込時にこの優先度などの引継ぎが行われていない。設定にて、優先度が5に
    // なっているマッチングが、アンケート取り込み後に受講希望から生徒の優先度を見てみると変わって
    // いないことがわかる」を再現・検証する。「通常授業担当設定」（RegularLessonProfile）で
    // 生徒・科目の組み合わせに優先度5・通常担当講師を設定した状態でアンケート取込みを実行し、
    // 生成されるLessonRequestがこの設定を正しく引き継ぐことを確認する。
    [Fact]
    public async Task ApplyAsync_CarriesOverRegularTeacherPriorityFromExistingRegularLessonProfile()
    {
        var state = await CreateStateAsync();
        var master = new SqliteMasterDataRepository();
        await master.SaveRegularLessonAsync(state.Path, new RegularLessonProfile(0, state.StudentId, state.SubjectId, state.TeacherId, 5, false, ""));

        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        WriteStudentCsv(studentPath, "架空", "太郎", "中2", "在籍生", "中学校", "英語", "2", "",
            [(state.Date1, ""), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "", [(state.Date1, ""), (state.Date2, "")]);

        var service = new CourseSurveyImportService();
        var preview = await service.PreviewAsync(state.Path, studentPath, teacherPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await service.ApplyAsync(state.Path, preview);

        await using var connection = new SqliteConnection($"Data Source={state.Path};Pooling=False");
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection,
            $"SELECT COUNT(*) FROM LessonRequest WHERE StudentId={state.StudentId} AND SubjectId={state.SubjectId} AND RegularTeacherPriority=5 AND RegularTeacherId={state.TeacherId}"));
    }

    [Fact]
    public async Task Apply_TrialStudentNotInRoster_CreatesTrialStudentWithWarning()
    {
        var state = await CreateStateAsync();
        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        WriteStudentCsv(studentPath, "新規", "次郎", "中1", "体験生", "中学校", "英語", "1", "", [(state.Date1, ""), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "", [(state.Date1, ""), (state.Date2, "")]);

        var service = new CourseSurveyImportService();
        var preview = await service.PreviewAsync(state.Path, studentPath, teacherPath);
        Assert.False(preview.HasErrors);
        Assert.Contains(preview.Issues, issue => issue.Severity == CourseSurveyIssueSeverity.Warning && issue.Message.Contains("体験生"));

        var result = await service.ApplyAsync(state.Path, preview);
        Assert.Equal(1, result.TrialStudents);

        await using var connection = new SqliteConnection($"Data Source={state.Path};Pooling=False");
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Student WHERE ExternalId='TRIAL-0001' AND Grade='中1' AND Active=1"));
    }

    [Fact]
    public async Task PreviewAsync_UnknownRegularStudent_ReportsErrorAndBlocksApply()
    {
        var state = await CreateStateAsync();
        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        WriteStudentCsv(studentPath, "未登録", "三郎", "中1", "在籍生", "中学校", "英語", "1", "", [(state.Date1, ""), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "", [(state.Date1, ""), (state.Date2, "")]);

        var service = new CourseSurveyImportService();
        var preview = await service.PreviewAsync(state.Path, studentPath, teacherPath);
        Assert.True(preview.HasErrors);
        Assert.Contains(preview.Issues, issue => issue.Severity == CourseSurveyIssueSeverity.Error && issue.Message.Contains("在籍生がいません"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(state.Path, preview));
    }

    [Fact]
    public async Task ApplyAsync_WhenAssignmentsExist_ThrowsAndBlocksFullReplace()
    {
        var state = await CreateStateAsync();
        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        WriteStudentCsv(studentPath, "架空", "太郎", "中2", "在籍生", "中学校", "英語", "2", "", [(state.Date1, ""), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "", [(state.Date1, ""), (state.Date2, "")]);

        await using (var connection = new SqliteConnection($"Data Source={state.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,Source) VALUES(last_insert_rowid(),$teacher,(SELECT Id FROM OpenDate WHERE Date=$date),$slot,'cp-sat');
                """;
            command.Parameters.AddWithValue("$student", state.StudentId); command.Parameters.AddWithValue("$subject", state.SubjectId); command.Parameters.AddWithValue("$teacher", state.TeacherId);
            command.Parameters.AddWithValue("$date", state.Date1.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("$slot", state.Slot1Id);
            await command.ExecuteNonQueryAsync();
        }

        var service = new CourseSurveyImportService();
        var preview = await service.PreviewAsync(state.Path, studentPath, teacherPath);
        Assert.False(preview.HasErrors);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(state.Path, preview));
        Assert.Contains("時間割配置後", exception.Message);
    }

    [Fact]
    public async Task Apply_CalledTwiceWithDifferentAvailability_FullyReplacesPreviousData()
    {
        var state = await CreateStateAsync();
        var studentPath = Path.Combine(_directory, "student.csv");
        var teacherPath = Path.Combine(_directory, "teacher.csv");
        var service = new CourseSurveyImportService();

        WriteStudentCsv(studentPath, "架空", "太郎", "中2", "在籍生", "中学校", "英語", "2", "", [(state.Date1, "1 09:00-10:00"), (state.Date2, "")]);
        WriteTeacherCsv(teacherPath, "架空", "花子", "", [(state.Date1, ""), (state.Date2, "")]);
        await service.ApplyAsync(state.Path, await service.PreviewAsync(state.Path, studentPath, teacherPath));

        WriteStudentCsv(studentPath, "架空", "太郎", "中2", "在籍生", "中学校", "英語", "3", "", [(state.Date1, ""), (state.Date2, "2 10:10-11:10")]);
        await service.ApplyAsync(state.Path, await service.PreviewAsync(state.Path, studentPath, teacherPath));

        await using var connection = new SqliteConnection($"Data Source={state.Path};Pooling=False");
        await connection.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM LessonRequest"));
        Assert.Equal(3L, await ScalarAsync(connection, "SELECT RequiredSessions FROM LessonRequest"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId={state.StudentId} AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date='{state.Date1:yyyy-MM-dd}') AND TimeSlotId={state.Slot1Id}"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId={state.StudentId} AND OpenDateId=(SELECT Id FROM OpenDate WHERE Date='{state.Date2:yyyy-MM-dd}') AND TimeSlotId={state.Slot2Id}"));
    }

    private static void WriteStudentCsv(string path, string surname, string given, string grade, string enrollment, string schoolLevel, string subject, string count, string note, (DateOnly Date, string Cell)[] unavailability)
    {
        var headers = new List<string> { "Timestamp", "Email Address", "個人情報の利用目的への同意（必須）", "姓（苗字）（必須）", "名（必須）", "学年（必須）", "在籍区分（必須）", "学校区分（1教科目）", "受講教科（1教科目）（必須）", "受講回数（1教科目）（必須）" };
        headers.AddRange(unavailability.Select(entry => $"受講不可日時（チェックしたコマは受講不可） [{entry.Date:yyyy-MM-dd}（月）]"));
        headers.Add("特記事項");
        var values = new List<string> { "2026-09-11 0:37:00", "dummy@example.com", "同意します", surname, given, grade, enrollment, schoolLevel, subject, count };
        values.AddRange(unavailability.Select(entry => entry.Cell));
        values.Add(note);
        File.WriteAllText(path, CsvLine(headers) + "\n" + CsvLine(values) + "\n", new UTF8Encoding(false));
    }

    private static void WriteTeacherCsv(string path, string surname, string given, string note, (DateOnly Date, string Cell)[] unavailability)
    {
        var headers = new List<string> { "Timestamp", "個人情報の利用目的への同意（必須）", "姓（苗字）（必須）", "名（必須）" };
        headers.AddRange(unavailability.Select(entry => $"出勤不可日時（チェックしたコマは出勤不可） [{entry.Date:yyyy-MM-dd}（月）]"));
        headers.Add("勤務に関する特記事項");
        var values = new List<string> { "2026-09-11 0:40:00", "同意します", surname, given };
        values.AddRange(unavailability.Select(entry => entry.Cell));
        values.Add(note);
        File.WriteAllText(path, CsvLine(headers) + "\n" + CsvLine(values) + "\n", new UTF8Encoding(false));
    }

    private static string CsvLine(IEnumerable<string> fields) => string.Join(",", fields.Select(CsvField));
    private static string CsvField(string value) => value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private async Task<TestState> CreateStateAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var master = new SqliteMasterDataRepository();
        var course = new SqliteCourseSettingsRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-0001", "架空", "太郎", "中2"));
        var teacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-0001", "架空", "花子"));
        var subject = await master.SaveSubjectAsync(path, new Subject(0, "JH_ENG", "中学校・英語", "英", "中学校", 1));
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 10), new TimeOnly(11, 10), 2));
        var date1 = new DateOnly(2026, 7, 20);
        var date2 = new DateOnly(2026, 7, 21);
        await course.SaveCourseDayAsync(path, new CourseDay(date1, true, "", [slot1.Id, slot2.Id]));
        await course.SaveCourseDayAsync(path, new CourseDay(date2, true, "", [slot1.Id, slot2.Id]));
        return new TestState(path, student.Id, teacher.Id, subject.Id, slot1.Id, slot2.Id, date1, date2);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed record TestState(string Path, long StudentId, long TeacherId, long SubjectId, long Slot1Id, long Slot2Id, DateOnly Date1, DateOnly Date2);
}
