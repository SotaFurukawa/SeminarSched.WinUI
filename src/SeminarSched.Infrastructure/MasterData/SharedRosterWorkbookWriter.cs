using ClosedXML.Excel;
using SeminarSched.Domain.MasterData;

namespace SeminarSched.Infrastructure.MasterData;

/// <summary>
/// Python版shared_roster.pyのwrite_shared_roster相当。<see cref="SharedRosterImportService"/>が
/// 読み取れる「生徒・講師_基本情報.xlsx」形式（生徒/講師/科目/講師対応科目/通常授業の5シート）を
/// 書き出す。ID列は既存のExternalIdをそのまま値として書き込む（既存行は今まで通り）。
/// ユーザー指示（Python版同様の仕様）により、講師対応科目・通常授業シートの「…名から選択」列に
/// 生徒/講師/科目シートを参照するドロップダウン入力規則を設定し、対応する「…ID（自動・入力不要）」
/// 列には、まだ値の無い新規行に限り選んだ名前からIDを自動算出する数式を入れる。既存行のID値は
/// 従来通りの直接値のまま変更しない（既に動いているimportとの互換性・テストへの影響を避けるため）。
/// </summary>
internal static class SharedRosterWorkbookWriter
{
    // 在籍=FALSE（既卒等で使用しない生徒・講師）の行をグレー表示にし、一覧上で「この行は使わない」
    // ことが一目で分かるようにする。
    private static readonly XLColor InactiveRowFill = XLColor.FromHtml("#D9D9D9");

    private const int ReferenceValidationMaxRow = 1000;
    private const int ReferenceFormulaMaxRow = 200;

    public static void Write(
        string path,
        IReadOnlyList<Student> students,
        IReadOnlyList<Teacher> teachers,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<TeacherQualification> qualifications,
        IReadOnlyList<RegularLessonProfile> regularLessons)
    {
        var studentById = students.ToDictionary(x => x.Id);
        var teacherById = teachers.ToDictionary(x => x.Id);
        var subjectById = subjects.ToDictionary(x => x.Id);

        using var workbook = new XLWorkbook();

        var student = workbook.AddWorksheet("生徒");
        WriteHeaders(student, ["在籍", "生徒ID", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考"]);
        var studentRow = 2;
        foreach (var value in students)
        {
            var (surname, given) = SplitName(value.Name);
            SetRow(student, studentRow, value.Active ? "TRUE" : "FALSE", value.ExternalId, surname, given, value.Name, value.Grade, value.DefaultMaxConsecutiveSlots, value.AllowGap ? "あり" : "なし", value.Note);
            if (!value.Active) student.Row(studentRow).Style.Fill.BackgroundColor = InactiveRowFill;
            studentRow++;
        }

        var teacher = workbook.AddWorksheet("講師");
        WriteHeaders(teacher, ["在籍", "講師ID", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考"]);
        var teacherRow = 2;
        foreach (var value in teachers)
        {
            var (surname, given) = SplitName(value.Name);
            SetRow(teacher, teacherRow, value.Active ? "TRUE" : "FALSE", value.ExternalId, surname, given, value.Name, value.AllowGap ? "あり" : "なし", value.Note);
            if (!value.Active) teacher.Row(teacherRow).Style.Fill.BackgroundColor = InactiveRowFill;
            teacherRow++;
        }

        var subject = workbook.AddWorksheet("科目");
        WriteHeaders(subject, ["科目コード（必須）", "表示名（必須）", "学校段階（必須）", "並び順（必須）", "有効"]);
        var subjectRow = 2;
        foreach (var value in subjects)
            SetRow(subject, subjectRow++, value.Code, value.DisplayName, value.SchoolLevel, value.SortOrder, value.Active ? "はい" : "いいえ");

        var qualification = workbook.AddWorksheet("講師対応科目");
        WriteHeaders(qualification, ["講師名から選択", "講師ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "指導可能（デフォルトははい）", "備考"]);
        var qualificationRow = 2;
        foreach (var value in qualifications)
        {
            if (!teacherById.TryGetValue(value.TeacherId, out var qualificationTeacher) || !subjectById.TryGetValue(value.SubjectId, out var qualificationSubject)) continue;
            SetRow(qualification, qualificationRow++, qualificationTeacher.Name, qualificationTeacher.ExternalId, qualificationSubject.DisplayName, qualificationSubject.Code, value.CanTeach ? "はい" : "いいえ", value.Note);
        }
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 1, idColumn: 2, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: qualificationRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 3, idColumn: 4, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: qualificationRow - 1, idRequired: true);

        var regularLesson = workbook.AddWorksheet("通常授業");
        WriteHeaders(regularLesson, ["生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考"]);
        var regularLessonRow = 2;
        foreach (var value in regularLessons)
        {
            if (!studentById.TryGetValue(value.StudentId, out var regularStudent) || !subjectById.TryGetValue(value.SubjectId, out var regularSubject)) continue;
            var regularTeacher = value.RegularTeacherId is long teacherId && teacherById.TryGetValue(teacherId, out var found) ? found : null;
            SetRow(regularLesson, regularLessonRow++, regularStudent.Name, regularStudent.ExternalId, regularSubject.DisplayName, regularSubject.Code, regularTeacher?.Name ?? "", regularTeacher?.ExternalId ?? "", value.RegularTeacherPriority, value.OneToOneRequired ? "はい" : "いいえ", value.Note);
        }
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 1, idColumn: 2, sourceSheetName: "生徒", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 3, idColumn: 4, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 5, idColumn: 6, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: false);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new ArgumentException("The path has no parent directory.", nameof(path));
        var temporary = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(temporary);
        File.Move(temporary, path, overwrite: true);
    }

    // ドロップダウン選択＋新規行のID自動算出数式の仕組みを、生徒・講師シートに例示行が無くヘッダー
    // 直後（2行目）からデータが始まるこのワークブックのレイアウトに合わせて実装したもの。選択列（名前）には参照先シートの氏名/
    // 表示名列を参照するリスト入力規則を設定し、まだデータの無い行のID列にだけ、選んだ名前から
    // IDを自動算出する数式を入れる。既存行のID値は直接の文字列のまま変更しない
    // （SharedRosterImportServiceは選択列を読まずID/コード列だけを読むため、既存の取込み処理は無修正で動く）。
    private static void AddReferenceHelperColumn(XLWorkbook workbook, IXLWorksheet sheet, int selectColumn, int idColumn, string sourceSheetName, int sourceIdColumn, int sourceNameColumn, int lastDataRow, bool idRequired)
    {
        var sourceSheet = workbook.Worksheet(sourceSheetName);
        var idSourceRange = sourceSheet.Range(2, sourceIdColumn, ReferenceValidationMaxRow, sourceIdColumn);
        var nameSourceRange = sourceSheet.Range(2, sourceNameColumn, ReferenceValidationMaxRow, sourceNameColumn);
        var idColumnLetter = ColumnLetter(sourceIdColumn);
        var nameColumnLetter = ColumnLetter(sourceNameColumn);
        var idSourceRef = $"'{sourceSheetName}'!${idColumnLetter}$2:${idColumnLetter}${ReferenceValidationMaxRow}";
        var nameSourceRef = $"'{sourceSheetName}'!${nameColumnLetter}$2:${nameColumnLetter}${ReferenceValidationMaxRow}";
        var selectLetter = ColumnLetter(selectColumn);

        var selectValidation = sheet.Range(2, selectColumn, ReferenceValidationMaxRow, selectColumn).CreateDataValidation();
        selectValidation.List(nameSourceRange, true);
        selectValidation.IgnoreBlanks = !idRequired;
        selectValidation.ShowErrorMessage = true;
        selectValidation.ErrorTitle = "一覧にない名前です";
        selectValidation.ErrorMessage = $"{sourceSheetName}シートに登録済みの名前を選択してください。";

        var idValidation = sheet.Range(2, idColumn, ReferenceValidationMaxRow, idColumn).CreateDataValidation();
        idValidation.List(idSourceRange, true);
        idValidation.IgnoreBlanks = !idRequired;
        idValidation.ShowErrorMessage = true;
        idValidation.ErrorTitle = "一覧にない値です";
        idValidation.ErrorMessage = $"{sourceSheetName}シートに登録済みのIDまたはコードを入力するか、左の「名前から選択」列で選んでください。";

        var formulaEnd = Math.Max(lastDataRow, ReferenceFormulaMaxRow);
        for (var row = lastDataRow + 1; row <= formulaEnd; row++)
            sheet.Cell(row, idColumn).FormulaA1 = $"IF({selectLetter}{row}=\"\",\"\",IF(COUNTIF({nameSourceRef},{selectLetter}{row})=1,INDEX({idSourceRef},MATCH({selectLetter}{row},{nameSourceRef},0)),\"\"))";
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

    // Student/Teacher.Nameは"姓 名"の単一空白区切りで保存されている（FullName()の逆操作）。
    private static (string Surname, string Given) SplitName(string name)
    {
        var index = name.IndexOf(' ');
        return index < 0 ? (name, "") : (name[..index], name[(index + 1)..]);
    }

    private static void WriteHeaders(IXLWorksheet sheet, string[] headers)
    {
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
    }

    private static void SetRow(IXLWorksheet sheet, int row, params object[] values)
    {
        for (var index = 0; index < values.Length; index++) sheet.Cell(row, index + 1).Value = XLCellValue.FromObject(values[index]);
    }
}
