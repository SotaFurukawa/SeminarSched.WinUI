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
/// ユーザー指示（「以前のデザイン」「カラムなどの色なども」）により、見出し・必須列・退籍行の配色、
/// 枠線、列幅、ウィンドウ枠の固定、オートフィルターはPython版shared_roster.pyの配色・レイアウトへ
/// 合わせている。
/// </summary>
internal static class SharedRosterWorkbookWriter
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor HelperHeaderFill = XLColor.FromHtml("#5B9BD5");
    private static readonly XLColor RequiredFill = XLColor.FromHtml("#FFF2CC");
    // 在籍=FALSE（既卒等で使用しない生徒・講師）の行をグレー表示にし、一覧上で「この行は使わない」
    // ことが一目で分かるようにする。Python版と同じく、値を直接塗るのではなく条件付き書式にすることで、
    // Excel上で在籍チェックを後から変更しても自動的に表示が切り替わる。
    private static readonly XLColor InactiveRowFill = XLColor.FromHtml("#E7E6E6");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#D9DEE7");

    private const int ReferenceValidationMaxRow = 1000;
    private const int ReferenceFormulaMaxRow = 200;
    private const int ConditionalFormatMaxRow = 10000;

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

        var studentHeaders = new[] { "在籍", "生徒ID", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考" };
        var student = workbook.AddWorksheet("生徒");
        WriteHeaders(student, studentHeaders, [20, 18, 15, 15, 24, 12, 25, 24, 32]);
        var studentRow = 2;
        foreach (var value in students)
            SetRow(student, studentRow++, value.Active ? "TRUE" : "FALSE", value.ExternalId, value.FamilyName, value.GivenName, value.FullName, value.Grade, value.DefaultMaxConsecutiveSlots, value.AllowGap ? "あり" : "なし", value.Note);
        StyleDataRows(student, studentHeaders.Length, studentRow - 1, [3, 6], blankRowBuffer: ReferenceFormulaMaxRow);
        MarkInactiveRows(student, studentHeaders.Length, activeColumn: 1, keyColumn: 3);
        FinishSheet(student, studentRow - 1, studentHeaders.Length);

        var teacherHeaders = new[] { "在籍", "講師ID", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考" };
        var teacher = workbook.AddWorksheet("講師");
        WriteHeaders(teacher, teacherHeaders, [20, 18, 15, 15, 24, 24, 32]);
        var teacherRow = 2;
        foreach (var value in teachers)
            SetRow(teacher, teacherRow++, value.Active ? "TRUE" : "FALSE", value.ExternalId, value.FamilyName, value.GivenName, value.FullName, value.AllowGap ? "あり" : "なし", value.Note);
        StyleDataRows(teacher, teacherHeaders.Length, teacherRow - 1, [3], blankRowBuffer: ReferenceFormulaMaxRow);
        MarkInactiveRows(teacher, teacherHeaders.Length, activeColumn: 1, keyColumn: 3);
        FinishSheet(teacher, teacherRow - 1, teacherHeaders.Length);

        var subjectHeaders = new[] { "科目コード（必須）", "表示名（必須）", "学校段階（必須）", "並び順（必須）", "有効" };
        var subject = workbook.AddWorksheet("科目");
        WriteHeaders(subject, subjectHeaders, [20, 28, 15, 13, 12]);
        var subjectRow = 2;
        foreach (var value in subjects)
            SetRow(subject, subjectRow++, value.Code, value.DisplayName, value.SchoolLevel, value.SortOrder, value.Active ? "はい" : "いいえ");
        StyleDataRows(subject, subjectHeaders.Length, subjectRow - 1, []);
        FinishSheet(subject, subjectRow - 1, subjectHeaders.Length);

        var qualificationHeaders = new[] { "講師名から選択", "講師ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "指導可能（デフォルトははい）", "備考" };
        var qualification = workbook.AddWorksheet("講師対応科目");
        WriteHeaders(qualification, qualificationHeaders, [28, 22, 32, 24, 24, 32], helperColumns: [2, 4]);
        var qualificationRow = 2;
        foreach (var value in qualifications)
        {
            if (!teacherById.TryGetValue(value.TeacherId, out var qualificationTeacher) || !subjectById.TryGetValue(value.SubjectId, out var qualificationSubject)) continue;
            SetRow(qualification, qualificationRow++, qualificationTeacher.FullName, qualificationTeacher.ExternalId, qualificationSubject.DisplayName, qualificationSubject.Code, value.CanTeach ? "はい" : "いいえ", value.Note);
        }
        StyleDataRows(qualification, qualificationHeaders.Length, qualificationRow - 1, []);
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 1, idColumn: 2, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: qualificationRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 3, idColumn: 4, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: qualificationRow - 1, idRequired: true);
        FinishSheet(qualification, qualificationRow - 1, qualificationHeaders.Length);

        var regularHeaders = new[] { "生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考" };
        var regularLesson = workbook.AddWorksheet("通常授業");
        WriteHeaders(regularLesson, regularHeaders, [28, 22, 32, 24, 28, 24, 24, 26, 32], helperColumns: [2, 4, 6]);
        var regularLessonRow = 2;
        foreach (var value in regularLessons)
        {
            if (!studentById.TryGetValue(value.StudentId, out var regularStudent) || !subjectById.TryGetValue(value.SubjectId, out var regularSubject)) continue;
            var regularTeacher = value.RegularTeacherId is long teacherId && teacherById.TryGetValue(teacherId, out var found) ? found : null;
            SetRow(regularLesson, regularLessonRow++, regularStudent.FullName, regularStudent.ExternalId, regularSubject.DisplayName, regularSubject.Code, regularTeacher?.FullName ?? "", regularTeacher?.ExternalId ?? "", value.RegularTeacherPriority, value.OneToOneRequired ? "はい" : "いいえ", value.Note);
        }
        StyleDataRows(regularLesson, regularHeaders.Length, regularLessonRow - 1, []);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 1, idColumn: 2, sourceSheetName: "生徒", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 3, idColumn: 4, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 5, idColumn: 6, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: false);
        FinishSheet(regularLesson, regularLessonRow - 1, regularHeaders.Length);

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
        {
            var idCell = sheet.Cell(row, idColumn);
            idCell.FormulaA1 = $"IF({selectLetter}{row}=\"\",\"\",IF(COUNTIF({nameSourceRef},{selectLetter}{row})=1,INDEX({idSourceRef},MATCH({selectLetter}{row},{nameSourceRef},0)),\"\"))";
            ApplyNormalCellStyle(idCell);
            var selectCell = sheet.Cell(row, selectColumn);
            ApplyNormalCellStyle(selectCell);
            if (idRequired) selectCell.Style.Fill.BackgroundColor = RequiredFill;
        }
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

    private static void WriteHeaders(IXLWorksheet sheet, string[] headers, int[] widths, int[]? helperColumns = null)
    {
        var helpers = helperColumns is null ? new HashSet<int>() : new HashSet<int>(helperColumns);
        sheet.ShowGridLines = false;
        sheet.SheetView.FreezeRows(1);
        sheet.Row(1).Height = 34;
        for (var index = 0; index < headers.Length; index++)
        {
            var column = index + 1;
            var cell = sheet.Cell(1, column);
            cell.Value = headers[index];
            cell.Style.Fill.BackgroundColor = helpers.Contains(column) ? HelperHeaderFill : HeaderFill;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            sheet.Column(column).Width = widths[index];
        }
    }

    // blankRowBufferを指定すると、実データが無い行にも「ここに入力してください」という黄色の
    // 必須列ハイライトを、まだ姓・学年等の値が無いまま先に表示する（Python版の生徒・講師シートが
    // 1000行分の入力済みテンプレート行を用意していたのと同じ見た目を、数式無しの配色のみで再現）。
    private static void StyleDataRows(IXLWorksheet sheet, int columnCount, int lastDataRow, int[] requiredColumns, int blankRowBuffer = 0)
    {
        var lastStyledRow = Math.Max(lastDataRow, blankRowBuffer);
        if (lastStyledRow < 2) return;
        var range = sheet.Range(2, 1, lastStyledRow, columnCount);
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = BorderColor;
        foreach (var column in requiredColumns)
            sheet.Range(2, column, lastStyledRow, column).Style.Fill.BackgroundColor = RequiredFill;
    }

    private static void ApplyNormalCellStyle(IXLCell cell)
    {
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        cell.Style.Alignment.WrapText = true;
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorderColor = BorderColor;
    }

    // 在籍チェック（文字列"FALSE"）かつ姓が入力済みの行をPython版と同じ配色でグレー表示する。
    // 条件付き書式にすることで、Excel上で後から在籍チェックを変更しても表示が追従する。
    private static void MarkInactiveRows(IXLWorksheet sheet, int columnCount, int activeColumn, int keyColumn)
    {
        var activeLetter = ColumnLetter(activeColumn);
        var keyLetter = ColumnLetter(keyColumn);
        var range = sheet.Range(2, 1, ConditionalFormatMaxRow, columnCount);
        range.AddConditionalFormat().WhenIsTrue($"AND(${keyLetter}2<>\"\",${activeLetter}2=\"FALSE\")").Fill.BackgroundColor = InactiveRowFill;
    }

    private static void FinishSheet(IXLWorksheet sheet, int lastDataRow, int columnCount)
    {
        var rangeLastRow = Math.Max(lastDataRow, 2);
        sheet.Range(1, 1, rangeLastRow, columnCount).SetAutoFilter();
    }

    private static void SetRow(IXLWorksheet sheet, int row, params object[] values)
    {
        for (var index = 0; index < values.Length; index++) sheet.Cell(row, index + 1).Value = XLCellValue.FromObject(values[index]);
    }
}
