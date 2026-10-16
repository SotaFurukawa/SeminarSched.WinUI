using ClosedXML.Excel;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SharedRosterStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    // ユーザー要望「0 基本情報設定」画面での直接編集（共通正本DBを直接触る）を、保存の都度すぐに
    // 共通名簿Excelへ反映できるようにするための土台。DatabasePathで直接DBを編集したあと、
    // EnsureWorkbookAsync（既存ファイルがあれば触らない）では反映されず、RefreshWorkbookAsync
    // （無条件に再生成）でのみ反映されることを確認する。
    [Fact]
    public async Task RefreshWorkbookAsync_OverwritesExistingWorkbookWithCurrentDatabaseContent()
    {
        var storeDirectory = Path.Combine(_directory, "storeRefresh");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var sourcePath = Path.Combine(_directory, "source_refresh.xlsx");
        BuildRosterWorkbookWithGrades(sourcePath, [("S-0001", "架空 太郎", "中2", "TRUE")]);
        var preview = await store.PreviewImportAsync(sourcePath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(i => i.Message)));
        await store.ApplyImportAsync(preview);
        var path = await store.EnsureWorkbookAsync();
        Assert.True(File.Exists(path));

        // 「0 基本情報設定」画面が行うのと同じく、共通正本DBをExcel経由ではなく直接編集する。
        var master = new SqliteMasterDataRepository();
        var existing = (await master.GetStudentsAsync(store.DatabasePath)).Single();
        await master.SaveStudentAsync(store.DatabasePath, new Student(existing.Id, existing.ExternalId, existing.FamilyName, existing.GivenName,
            existing.Grade, existing.DefaultMaxConsecutiveSlots, existing.AllowGap, "直接編集で更新", existing.Active));

        // EnsureWorkbookAsyncは「無ければ作る」だけなので、既存ファイルはまだ古いまま。
        await store.EnsureWorkbookAsync();
        using (var stale = new XLWorkbook(path))
            Assert.Equal("", stale.Worksheet("生徒").Cell(2, 9).GetString());

        await store.RefreshWorkbookAsync();
        using var refreshed = new XLWorkbook(path);
        Assert.Equal("直接編集で更新", refreshed.Worksheet("生徒").Cell(2, 9).GetString());
    }

    [Fact]
    public async Task EnsureWorkbookAsync_CreatesBlankTemplateOnceAndDoesNotRegenerate()
    {
        var storeDirectory = Path.Combine(_directory, "store");
        var store = new SharedRosterStore(new SharedRosterImportService(), new SqliteMasterDataRepository(), storeDirectory);

        var path = await store.EnsureWorkbookAsync();
        Assert.True(File.Exists(path));
        using (var workbook = new XLWorkbook(path))
        {
            Assert.Equal(["生徒", "講師", "科目", "講師対応科目", "通常授業", "_入力補助"], workbook.Worksheets.Select(sheet => sheet.Name));
            Assert.Equal(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("_入力補助").Visibility);
            // ユーザー指示により、空白の新規作成テンプレートでも在籍チェック・ID自動入力・氏名確認・
            // 既定値の数式をPython版・実運用ファイルと同じ1000行分あらかじめ用意するため、
            // データが無くてもLastRowUsedは1000行目になる。
            Assert.Equal(1000, workbook.Worksheet("生徒").LastRowUsed()?.RowNumber());
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
        // 不具合修正の確認: 以前は削除だけで次回「Excelで編集」を開くまでWorkbookPathが
        // 存在しない状態になっていた。繰り上げ直後から、既に最新の内容で存在していること。
        Assert.True(File.Exists(store.WorkbookPath));

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
        Assert.True(regular.Cell(3, 7).HasFormula); // 通常担当講師ID（任意、学年列が増えた分1列右へ移動）も同様
        // ユーザー指示: 通常授業シートに生徒IDの隣（3列目）へ、その生徒の学年を表示する列を追加する。
        Assert.True(regular.Cell(2, 3).HasFormula);
        Assert.Contains("生徒", regular.Cell(2, 3).FormulaA1);

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

    // ユーザー報告「基本情報xlsxを反映すると、明らかに中身が変わってしまう」の調査用。
    // 実際にDBへ保存→SharedRosterWorkbookWriterでExcelへ書き出し→(ユーザーの手編集を挟まず)
    // そのままSharedRosterImportServiceで別のDBへ取り込み、という素のWriter→Importer往復だけでも
    // 値が変化してしまう箇所が無いかを、各フィールドを個別に突き合わせて確認する。
    [Fact]
    public async Task WriteThenReimport_RoundTrip_PreservesAllFieldsExactly()
    {
        var master = new SqliteMasterDataRepository();
        var sourceStoreDirectory = Path.Combine(_directory, "roundtrip_source");
        var sourceStore = new SharedRosterStore(new SharedRosterImportService(), master, sourceStoreDirectory);
        await sourceStore.EnsureWorkbookAsync();

        var student1 = await master.SaveStudentAsync(sourceStore.DatabasePath, new Student(0, "S-0001", "架空", "太郎", "中2", 3, true, "生徒備考テスト", true));
        var student2 = await master.SaveStudentAsync(sourceStore.DatabasePath, new Student(0, "S-0002", "架空", "花子", "小5", 2, false, "", false));
        var teacher1 = await master.SaveTeacherAsync(sourceStore.DatabasePath, new Teacher(0, "T-0001", "架空", "次郎", true, "講師備考テスト", true));
        var subject1 = await master.SaveSubjectAsync(sourceStore.DatabasePath, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 1, true));
        var subject2 = await master.SaveSubjectAsync(sourceStore.DatabasePath, new Subject(0, "ES_JPN", "小学校・国語", "国", "小学校", 2, true));
        await master.SaveQualificationAsync(sourceStore.DatabasePath, new TeacherQualification(teacher1.Id, subject1.Id, true, "資格備考テスト"));
        await master.SaveRegularLessonAsync(sourceStore.DatabasePath, new RegularLessonProfile(0, student1.Id, subject1.Id, teacher1.Id, 5, true, "通常授業備考テスト"));

        await sourceStore.RefreshWorkbookAsync();

        var targetStoreDirectory = Path.Combine(_directory, "roundtrip_target");
        var targetStore = new SharedRosterStore(new SharedRosterImportService(), master, targetStoreDirectory);
        var preview = await targetStore.PreviewImportAsync(sourceStore.WorkbookPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await targetStore.ApplyImportAsync(preview);

        var students = await master.GetStudentsAsync(targetStore.DatabasePath, includeInactive: true);
        var reimportedStudent1 = students.Single(s => s.ExternalId == "S-0001");
        Assert.Equal("架空", reimportedStudent1.FamilyName);
        Assert.Equal("太郎", reimportedStudent1.GivenName);
        Assert.Equal("中2", reimportedStudent1.Grade);
        Assert.Equal(3, reimportedStudent1.DefaultMaxConsecutiveSlots);
        Assert.True(reimportedStudent1.AllowGap);
        Assert.Equal("生徒備考テスト", reimportedStudent1.Note);
        Assert.True(reimportedStudent1.Active);
        var reimportedStudent2 = students.Single(s => s.ExternalId == "S-0002");
        Assert.Equal("小5", reimportedStudent2.Grade);
        Assert.False(reimportedStudent2.AllowGap);
        Assert.Equal("", reimportedStudent2.Note);
        Assert.False(reimportedStudent2.Active);

        var teachers = await master.GetTeachersAsync(targetStore.DatabasePath, includeInactive: true);
        var reimportedTeacher1 = teachers.Single(t => t.ExternalId == "T-0001");
        Assert.Equal("架空", reimportedTeacher1.FamilyName);
        Assert.Equal("次郎", reimportedTeacher1.GivenName);
        Assert.True(reimportedTeacher1.AllowGap);
        Assert.Equal("講師備考テスト", reimportedTeacher1.Note);

        var subjects = await master.GetSubjectsAsync(targetStore.DatabasePath, includeInactive: true);
        var reimportedSubject1 = subjects.Single(s => s.Code == "JH_MATH");
        Assert.Equal("中学校・数学", reimportedSubject1.DisplayName);
        Assert.Equal("中学校", reimportedSubject1.SchoolLevel);
        Assert.Equal(1, reimportedSubject1.SortOrder);

        var qualifications = await master.GetQualificationsAsync(targetStore.DatabasePath);
        var reimportedQualification = qualifications.Single();
        Assert.True(reimportedQualification.CanTeach);
        Assert.Equal("資格備考テスト", reimportedQualification.Note);

        var regularLessons = await master.GetRegularLessonsAsync(targetStore.DatabasePath);
        var reimportedRegularLesson = regularLessons.Single();
        Assert.Equal(5, reimportedRegularLesson.RegularTeacherPriority);
        Assert.True(reimportedRegularLesson.OneToOneRequired);
        Assert.Equal("通常授業備考テスト", reimportedRegularLesson.Note);
        Assert.NotNull(reimportedRegularLesson.RegularTeacherId);
    }

    // ユーザー報告「基本情報xlsxを反映すると、明らかに中身が変わってしまう」の調査用その2。
    // このセッションで対応した「同じ生徒・科目でも通常担当講師が異なれば複数行を許す」機能
    // （checkpoint167、v1.3.0）が、共通名簿Excel経由のWriter→Importer往復でも正しく2行とも
    // 保たれるか（片方が消えたり、同じ行へ上書きされてしまったりしないか）を確認する。
    [Fact]
    public async Task WriteThenReimport_RoundTrip_PreservesMultipleRegularLessonRowsForSameStudentAndSubject()
    {
        var master = new SqliteMasterDataRepository();
        var sourceStoreDirectory = Path.Combine(_directory, "roundtrip_regular_source");
        var sourceStore = new SharedRosterStore(new SharedRosterImportService(), master, sourceStoreDirectory);
        await sourceStore.EnsureWorkbookAsync();

        var student = await master.SaveStudentAsync(sourceStore.DatabasePath, new Student(0, "S-0001", "架空", "太郎", "中2"));
        var teacherA = await master.SaveTeacherAsync(sourceStore.DatabasePath, new Teacher(0, "T-0001", "架空", "花子"));
        var teacherB = await master.SaveTeacherAsync(sourceStore.DatabasePath, new Teacher(0, "T-0002", "架空", "次郎"));
        var subject = await master.SaveSubjectAsync(sourceStore.DatabasePath, new Subject(0, "JH_ENG", "中学校・英語", "英", "中学校", 1));
        await master.SaveRegularLessonAsync(sourceStore.DatabasePath, new RegularLessonProfile(0, student.Id, subject.Id, teacherA.Id, 5, false, "火曜担当"));
        await master.SaveRegularLessonAsync(sourceStore.DatabasePath, new RegularLessonProfile(0, student.Id, subject.Id, teacherB.Id, 4, false, "木曜担当"));

        await sourceStore.RefreshWorkbookAsync();

        var targetStoreDirectory = Path.Combine(_directory, "roundtrip_regular_target");
        var targetStore = new SharedRosterStore(new SharedRosterImportService(), master, targetStoreDirectory);
        var preview = await targetStore.PreviewImportAsync(sourceStore.WorkbookPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        var result = await targetStore.ApplyImportAsync(preview);

        var regularLessons = await master.GetRegularLessonsAsync(targetStore.DatabasePath);
        Assert.Equal(2, regularLessons.Count);
        Assert.Contains(regularLessons, r => r.RegularTeacherPriority == 5 && r.Note == "火曜担当");
        Assert.Contains(regularLessons, r => r.RegularTeacherPriority == 4 && r.Note == "木曜担当");
    }

    // 不具合再現テスト（ユーザー報告「基本情報xlsxを反映すると通常授業の中身が明らかに変わる／
    // 全く異なる」）。既存の反映先DBに「以前の反映」で入った通常授業行（生徒・科目・講師A）が
    // 残っている状態で、同じ生徒・科目だが通常担当講師を講師Bへ変更した新しいExcelを反映すると、
    // 修正前は(StudentId,SubjectId,RegularTeacherId)が一致しないため講師Aの行が削除されずに残り、
    // 講師Bの行が追加されるだけで2行になってしまっていた（担当講師の変更のたびに古い行が
    // 「幽霊」として蓄積する不具合）。修正後は、反映ファイルに書かれている内容へ正しく
    // 置き換わり、講師Aの行は消えて講師Bの行だけが残ることを確認する。
    [Fact]
    public async Task ApplyImportAsync_WhenRegularTeacherChangedSinceLastReflect_RemovesStaleRowInsteadOfAccumulating()
    {
        var master = new SqliteMasterDataRepository();
        var targetStoreDirectory = Path.Combine(_directory, "stale_regular_target");
        var targetStore = new SharedRosterStore(new SharedRosterImportService(), master, targetStoreDirectory);
        await targetStore.EnsureWorkbookAsync();

        var student = await master.SaveStudentAsync(targetStore.DatabasePath, new Student(0, "S-0001", "架空", "太郎", "中2"));
        var teacherA = await master.SaveTeacherAsync(targetStore.DatabasePath, new Teacher(0, "T-0001", "架空", "花子"));
        var teacherB = await master.SaveTeacherAsync(targetStore.DatabasePath, new Teacher(0, "T-0002", "架空", "次郎"));
        var subject = await master.SaveSubjectAsync(targetStore.DatabasePath, new Subject(0, "JH_ENG", "中学校・英語", "英", "中学校", 1));
        // 「以前の反映」で入ったはずの、担当講師Aの通常授業行をあらかじめ作っておく。
        await master.SaveRegularLessonAsync(targetStore.DatabasePath, new RegularLessonProfile(0, student.Id, subject.Id, teacherA.Id, 5, false, "旧担当"));

        var sourceStoreDirectory = Path.Combine(_directory, "stale_regular_source");
        var sourceStore = new SharedRosterStore(new SharedRosterImportService(), master, sourceStoreDirectory);
        await sourceStore.EnsureWorkbookAsync();
        await master.SaveStudentAsync(sourceStore.DatabasePath, new Student(0, "S-0001", "架空", "太郎", "中2"));
        await master.SaveTeacherAsync(sourceStore.DatabasePath, new Teacher(0, "T-0002", "架空", "次郎"));
        await master.SaveSubjectAsync(sourceStore.DatabasePath, new Subject(0, "JH_ENG", "中学校・英語", "英", "中学校", 1));
        var sourceTeacherB = (await master.GetTeachersAsync(sourceStore.DatabasePath)).Single(t => t.ExternalId == "T-0002");
        var sourceStudent = (await master.GetStudentsAsync(sourceStore.DatabasePath)).Single();
        var sourceSubject = (await master.GetSubjectsAsync(sourceStore.DatabasePath)).Single();
        // 新しいExcelでは、同じ生徒・科目の通常担当講師が講師Bへ変更されている（講師Aはもう登場しない）。
        await master.SaveRegularLessonAsync(sourceStore.DatabasePath, new RegularLessonProfile(0, sourceStudent.Id, sourceSubject.Id, sourceTeacherB.Id, 3, false, "新担当"));
        await sourceStore.RefreshWorkbookAsync();

        var preview = await targetStore.PreviewImportAsync(sourceStore.WorkbookPath);
        Assert.False(preview.HasErrors, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await targetStore.ApplyImportAsync(preview);

        var regularLessons = await master.GetRegularLessonsAsync(targetStore.DatabasePath);
        var single = Assert.Single(regularLessons);
        Assert.Equal(teacherB.Id, single.RegularTeacherId);
        Assert.Equal("新担当", single.Note);
        Assert.DoesNotContain(regularLessons, r => r.RegularTeacherId == teacherA.Id);
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
