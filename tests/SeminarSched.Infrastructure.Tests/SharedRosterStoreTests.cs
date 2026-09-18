using ClosedXML.Excel;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SharedRosterStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnsureWorkbookAsync_CreatesBlankTemplateOnceAndDoesNotRegenerate()
    {
        var storeDirectory = Path.Combine(_directory, "store");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var path = await store.EnsureWorkbookAsync();
        Assert.True(File.Exists(path));
        using (var workbook = new XLWorkbook(path))
        {
            Assert.Equal(["生徒", "講師", "科目", "講師対応科目", "通常授業"], workbook.Worksheets.Select(sheet => sheet.Name));
            Assert.Equal(1, workbook.Worksheet("生徒").LastRowUsed()?.RowNumber());
        }

        // Simulate the user editing the file; EnsureWorkbookAsync must not clobber it on a second call.
        using (var workbook = new XLWorkbook(path)) { workbook.Worksheet("生徒").Cell(2, 1).Value = "EDITED"; workbook.Save(); }
        await store.EnsureWorkbookAsync();
        using var reopened = new XLWorkbook(path);
        Assert.Equal("EDITED", reopened.Worksheet("生徒").Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExportBlankTemplateAsync_DoesNotTouchCanonicalStore()
    {
        var storeDirectory = Path.Combine(_directory, "store2");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);
        var targetPath = Path.Combine(_directory, "template.xlsx");

        await store.ExportBlankTemplateAsync(targetPath);
        Assert.True(File.Exists(targetPath));
        Assert.False(File.Exists(store.WorkbookPath));
    }

    [Fact]
    public async Task PreviewAndApplyImport_PopulatesCanonicalStore_AndCopyIntoProjectPropagates()
    {
        var storeDirectory = Path.Combine(_directory, "store3");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var sourcePath = Path.Combine(_directory, "source.xlsx");
        BuildRosterWorkbook(sourcePath);
        var preview = await store.PreviewImportAsync(sourcePath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        var applyResult = await store.ApplyImportAsync(preview);
        Assert.Equal(2, applyResult.ImportedRows);

        var projectPath = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(projectPath, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var copyResult = await store.CopyIntoProjectAsync(projectPath);
        Assert.NotNull(copyResult);
        Assert.Equal(2, copyResult!.ImportedRows);

        var master = new SqliteMasterDataRepository();
        var students = await master.GetStudentsAsync(projectPath);
        Assert.Contains(students, s => s.ExternalId == "S-0001" && s.Name == "架空 太郎");
    }

    [Fact]
    public async Task CopyIntoProjectAsync_WhenCanonicalStoreEmpty_ReturnsNull()
    {
        var storeDirectory = Path.Combine(_directory, "store4");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);
        var projectPath = Path.Combine(_directory, "project2.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(projectPath, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));

        var result = await store.CopyIntoProjectAsync(projectPath);
        Assert.Null(result);
    }

    private static void BuildRosterWorkbook(string path)
    {
        using var workbook = new XLWorkbook();
        var student = workbook.AddWorksheet("生徒");
        string[] studentHeaders = ["在籍", "生徒ID", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考"];
        for (var i = 0; i < studentHeaders.Length; i++) student.Cell(1, i + 1).Value = studentHeaders[i];
        object[] studentRow = ["TRUE", "S-0001", "架空", "太郎", "架空 太郎", "中2", 2, "なし", ""];
        for (var i = 0; i < studentRow.Length; i++) student.Cell(2, i + 1).Value = XLCellValue.FromObject(studentRow[i]);

        var teacher = workbook.AddWorksheet("講師");
        string[] teacherHeaders = ["在籍", "講師ID", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考"];
        for (var i = 0; i < teacherHeaders.Length; i++) teacher.Cell(1, i + 1).Value = teacherHeaders[i];
        object[] teacherRow = ["TRUE", "T-0001", "架空", "花子", "架空 花子", "なし", ""];
        for (var i = 0; i < teacherRow.Length; i++) teacher.Cell(2, i + 1).Value = XLCellValue.FromObject(teacherRow[i]);

        var subject = workbook.AddWorksheet("科目");
        string[] subjectHeaders = ["科目コード（必須）", "表示名（必須）", "学校段階（必須）", "並び順（必須）", "有効"];
        for (var i = 0; i < subjectHeaders.Length; i++) subject.Cell(1, i + 1).Value = subjectHeaders[i];

        var qualification = workbook.AddWorksheet("講師対応科目");
        string[] qualificationHeaders = ["講師名から選択", "講師ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "指導可能（デフォルトははい）", "備考"];
        for (var i = 0; i < qualificationHeaders.Length; i++) qualification.Cell(1, i + 1).Value = qualificationHeaders[i];

        var regular = workbook.AddWorksheet("通常授業");
        string[] regularHeaders = ["生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考"];
        for (var i = 0; i < regularHeaders.Length; i++) regular.Cell(1, i + 1).Value = regularHeaders[i];

        workbook.SaveAs(path);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
