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
        Assert.Contains(students, s => s.ExternalId == "S-0001" && s.FullName == "架空 太郎");
    }

    [Fact]
    public async Task AdvanceStudentGradesAsync_AdvancesActiveStudentsAndGraduatesHigh3()
    {
        var storeDirectory = Path.Combine(_directory, "store5");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var sourcePath = Path.Combine(_directory, "source5.xlsx");
        BuildRosterWorkbookWithGrades(sourcePath, [("S-0001", "架空 太郎", "中2", "TRUE"), ("S-0002", "架空 花子", "高3", "TRUE"), ("S-0003", "架空 次郎", "中1", "FALSE")]);
        var preview = await store.PreviewImportAsync(sourcePath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await store.ApplyImportAsync(preview);
        await store.EnsureWorkbookAsync();
        Assert.True(File.Exists(store.WorkbookPath));

        var summary = await store.AdvanceStudentGradesAsync();
        Assert.Equal(2, summary.AdvancedCount);
        Assert.Equal(1, summary.GraduatedCount);
        // 次回「Excelで編集」を開いたとき、繰り上げ後の内容から再生成させるためキャッシュを消す。
        Assert.False(File.Exists(store.WorkbookPath));

        await store.EnsureWorkbookAsync();
        using var workbook = new XLWorkbook(store.WorkbookPath);
        var sheet = workbook.Worksheet("生徒");
        var rows = sheet.RowsUsed().Skip(1).Select(r => (Id: r.Cell(2).GetString(), Grade: r.Cell(6).GetString(), Active: r.Cell(1).GetString())).ToArray();
        Assert.Contains(rows, r => r.Id == "S-0001" && r.Grade == "中3" && r.Active == "TRUE");
        Assert.Contains(rows, r => r.Id == "S-0002" && r.Grade == "既卒" && r.Active == "FALSE");
        Assert.Contains(rows, r => r.Id == "S-0003" && r.Grade == "中1" && r.Active == "FALSE"); // 元々在籍停止の生徒は対象外で変更されない。
    }

    private static void BuildRosterWorkbookWithGrades(string path, (string ExternalId, string Name, string Grade, string Active)[] students)
    {
        using var workbook = new XLWorkbook();
        var student = workbook.AddWorksheet("生徒");
        string[] studentHeaders = ["在籍", "生徒ID", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考"];
        for (var i = 0; i < studentHeaders.Length; i++) student.Cell(1, i + 1).Value = studentHeaders[i];
        var row = 2;
        foreach (var (externalId, name, grade, active) in students)
        {
            var spaceIndex = name.IndexOf(' ');
            var (surname, given) = spaceIndex < 0 ? (name, "") : (name[..spaceIndex], name[(spaceIndex + 1)..]);
            object[] values = [active, externalId, surname, given, name, grade, 2, "なし", ""];
            for (var i = 0; i < values.Length; i++) student.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]);
            row++;
        }

        foreach (var (name, headers) in new (string, string[])[]
        {
            ("講師", ["在籍", "講師ID", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考"]),
            ("科目", ["科目コード（必須）", "表示名（必須）", "学校段階（必須）", "並び順（必須）", "有効"]),
            ("講師対応科目", ["講師名から選択", "講師ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "指導可能（デフォルトははい）", "備考"]),
            ("通常授業", ["生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考"]),
        })
        {
            var sheet = workbook.AddWorksheet(name);
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        }

        workbook.SaveAs(path);
    }

    // ユーザー指示（Python版同様の仕様）: 通常授業・講師対応科目シートで生徒・講師・科目を
    // 名前のドロップダウンから選択できるようにする。選択列・ID列双方にドロップダウンがあり、
    // まだ値の無い行（3行目以降）のID列には選んだ名前からIDを自動算出する数式が入ること、
    // 既存データの行（2行目）のID値は数式ではなく従来通りの直接値のままであることを確認する。
    [Fact]
    public async Task EnsureWorkbookAsync_AddsNameDropdownsAndIdAutoFillFormulasForRegularLessonAndQualification()
    {
        var storeDirectory = Path.Combine(_directory, "store6");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var sourcePath = Path.Combine(_directory, "source6.xlsx");
        BuildRosterWorkbookWithRegularLessonAndQualification(sourcePath);
        var preview = await store.PreviewImportAsync(sourcePath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await store.ApplyImportAsync(preview);

        var path = await store.EnsureWorkbookAsync();
        using var workbook = new XLWorkbook(path);

        var regular = workbook.Worksheet("通常授業");
        Assert.True(regular.Cell(2, 1).HasDataValidation); // 生徒名から選択（既存行にも選び直せるようドロップダウンを付ける）
        Assert.True(regular.Cell(2, 2).HasDataValidation); // 生徒ID（自動・入力不要）
        Assert.Equal("S-0001", regular.Cell(2, 2).GetString()); // 既存行のID値は数式ではなく直接値のまま
        Assert.False(regular.Cell(2, 2).HasFormula);
        Assert.True(regular.Cell(3, 1).HasDataValidation); // まだ値の無い3行目にもドロップダウンがある
        Assert.True(regular.Cell(3, 2).HasFormula); // 3行目のIDは選択列から自動算出する数式
        Assert.Contains("MATCH", regular.Cell(3, 2).FormulaA1);
        Assert.True(regular.Cell(3, 6).HasFormula); // 通常担当講師ID（任意）も同様

        var qualification = workbook.Worksheet("講師対応科目");
        Assert.True(qualification.Cell(2, 1).HasDataValidation);
        Assert.Equal("T-0001", qualification.Cell(2, 2).GetString());
        Assert.True(qualification.Cell(3, 2).HasFormula);
    }

    private static void BuildRosterWorkbookWithRegularLessonAndQualification(string path)
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
