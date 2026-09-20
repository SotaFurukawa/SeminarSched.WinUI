using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SharedRosterImportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewAndApply_PythonSharedRosterFormat_ImportsAllFiveSheetsWithGradeConversion()
    {
        var project = await CreateProjectAsync();
        var workbookPath = Path.Combine(_directory, "生徒・講師_基本情報.xlsx");
        BuildSharedRosterWorkbook(workbookPath);

        var service = new SharedRosterImportService();
        var preview = await service.PreviewAsync(project, workbookPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(i => i.Message)));
        Assert.Equal(1, preview.StudentCount); Assert.Equal(1, preview.TeacherCount); Assert.Equal(1, preview.SubjectCount);
        Assert.Equal(1, preview.QualificationCount); Assert.Equal(1, preview.RegularLessonCount);

        var result = await service.ApplyAsync(project, preview);
        Assert.Equal(5, result.ImportedRows);

        await using var connection = new SqliteConnection($"Data Source={project};Pooling=False");
        await connection.OpenAsync();
        // Excel short grade code "J2" must convert to the internal "中2" representation.
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Student WHERE ExternalId='S-0001' AND Grade='中2' AND Name='架空 太郎'"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Teacher WHERE ExternalId='T-0001' AND Active=1"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId JOIN Subject s ON s.Id=q.SubjectId WHERE t.ExternalId='T-0001' AND s.Code='JH_MATH' AND q.CanTeach=1"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM RegularLessonProfile r JOIN Teacher t ON t.Id=r.RegularTeacherId WHERE r.RegularTeacherPriority=3 AND t.ExternalId='T-0001'"));
        // 「科目」シートに略称列が無いため、新規科目の略称は表示名から自動推定される（バグ修正の回帰防止:
        // 以前はDefaultShortNameが表示名をそのまま（10文字まで切り詰めて）入れてしまい、時間割出力の
        // 科目表記が一文字にならない不具合の原因になっていた）。
        Assert.Equal("数", await ScalarStringAsync(connection, "SELECT ShortName FROM Subject WHERE Code='JH_MATH'"));
    }

    [Fact]
    public async Task ApplyAsync_ReImportingSameSubject_PreservesManuallyCorrectedShortName()
    {
        var project = await CreateProjectAsync();
        var workbookPath = Path.Combine(_directory, "生徒・講師_基本情報.xlsx");
        BuildSharedRosterWorkbook(workbookPath);
        var service = new SharedRosterImportService();
        await service.ApplyAsync(project, await service.PreviewAsync(project, workbookPath));

        await using (var connection = new SqliteConnection($"Data Source={project};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Subject SET ShortName='算' WHERE Code='JH_MATH';";
            await command.ExecuteNonQueryAsync();
        }

        await service.ApplyAsync(project, await service.PreviewAsync(project, workbookPath));

        await using var verify = new SqliteConnection($"Data Source={project};Pooling=False");
        await verify.OpenAsync();
        Assert.Equal("算", await ScalarStringAsync(verify, "SELECT ShortName FROM Subject WHERE Code='JH_MATH'"));
    }

    [Fact]
    public async Task PreviewAsync_MissingSheet_ReportsErrorWithoutRequiringLessonRequestSheet()
    {
        var project = await CreateProjectAsync();
        var workbookPath = Path.Combine(_directory, "incomplete.xlsx");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("生徒");
            workbook.SaveAs(workbookPath);
        }

        var service = new SharedRosterImportService();
        var preview = await service.PreviewAsync(project, workbookPath);
        Assert.True(preview.HasErrors);
        Assert.Contains(preview.Issues, issue => issue.Message.Contains("必須シートがありません") && issue.SheetName == "通常授業");
        Assert.DoesNotContain(preview.Issues, issue => issue.SheetName == "受講希望");
    }

    private static void BuildSharedRosterWorkbook(string path)
    {
        using var workbook = new XLWorkbook();

        var student = workbook.AddWorksheet("生徒");
        string[] studentHeaders = ["在籍", "生徒ID（自動・入力不要）", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考"];
        for (var i = 0; i < studentHeaders.Length; i++) student.Cell(1, i + 1).Value = studentHeaders[i];
        object[] studentRow = ["TRUE", "S-0001", "架空", "太郎", "架空 太郎", "J2", 2, "なし", ""];
        for (var i = 0; i < studentRow.Length; i++) student.Cell(2, i + 1).Value = XLCellValue.FromObject(studentRow[i]);

        var teacher = workbook.AddWorksheet("講師");
        string[] teacherHeaders = ["在籍", "講師ID（自動・入力不要）", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考"];
        for (var i = 0; i < teacherHeaders.Length; i++) teacher.Cell(1, i + 1).Value = teacherHeaders[i];
        object[] teacherRow = ["TRUE", "T-0001", "架空", "花子", "架空 花子", "なし", ""];
        for (var i = 0; i < teacherRow.Length; i++) teacher.Cell(2, i + 1).Value = XLCellValue.FromObject(teacherRow[i]);

        var subject = workbook.AddWorksheet("科目");
        string[] subjectHeaders = ["科目コード（必須）", "表示名（必須）", "学校段階（必須）", "並び順（必須）", "有効"];
        for (var i = 0; i < subjectHeaders.Length; i++) subject.Cell(1, i + 1).Value = subjectHeaders[i];
        object[] subjectRow = ["JH_MATH", "中学校・数学", "中学校", 1, "はい"];
        for (var i = 0; i < subjectRow.Length; i++) subject.Cell(2, i + 1).Value = XLCellValue.FromObject(subjectRow[i]);

        var qualification = workbook.AddWorksheet("講師対応科目");
        string[] qualificationHeaders = ["講師名から選択", "講師ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "指導可能（デフォルトははい）", "備考"];
        for (var i = 0; i < qualificationHeaders.Length; i++) qualification.Cell(1, i + 1).Value = qualificationHeaders[i];
        object[] qualificationRow = ["架空 花子", "T-0001", "中学校・数学", "JH_MATH", "はい", ""];
        for (var i = 0; i < qualificationRow.Length; i++) qualification.Cell(2, i + 1).Value = XLCellValue.FromObject(qualificationRow[i]);

        var regular = workbook.AddWorksheet("通常授業");
        string[] regularHeaders = ["生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考"];
        for (var i = 0; i < regularHeaders.Length; i++) regular.Cell(1, i + 1).Value = regularHeaders[i];
        object[] regularRow = ["架空 太郎", "S-0001", "中学校・数学", "JH_MATH", "架空 花子", "T-0001", 3, "いいえ", ""];
        for (var i = 0; i < regularRow.Length; i++) regular.Cell(2, i + 1).Value = XLCellValue.FromObject(regularRow[i]);

        workbook.SaveAs(path);
    }

    private async Task<string> CreateProjectAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 8, 20)));
        return path;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
