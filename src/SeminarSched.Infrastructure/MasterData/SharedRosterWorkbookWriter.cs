using System.Globalization;
using System.Text.RegularExpressions;
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
/// ユーザー指示（「以前のデザイン」「カラムなどの色なども」、続けて「在籍チェックボックス・ID自動入力・
/// 氏名確認・デフォルト値の自動入力が消えている」「必須列も空白を許可してほしい」「通常授業に生徒の
/// 学年を表示する列を追加してほしい」）により、以下をPython版・実運用ファイル（生徒・講師_基本情報
/// (夏期).xlsx）と同じ設計へ合わせている。
/// - 在籍列はネイティブの真偽値セル＋TRUE/FALSEのドロップダウンにする（ClosedXMLはxlsxwriterの
///   フォームコントロール式チェックボックス自体は作成できないため、見た目はチェックボックスの
///   アイコンにはならないが、値の型・ドロップダウン選択・条件付き書式との整合性は同じにしている）。
/// - 生徒ID・講師ID列は、まだ姓が未入力の新規行に対し、非表示の"_入力補助"シートから次に使える
///   連番IDを自動算出する数式を入れる（既存行は直接値のまま）。
/// - 氏名（確認）列は、既存行・新規行を問わず常に姓・名から組み立てる数式にする（Python版・実ファイル
///   と同じく、保存時点の値を直接書き込まない）。
/// - 標準最大連続コマ数・空きコマ許可・講師対応科目の指導可能・通常授業の担当講師優先度/1対1必須は、
///   まだ値の無い新規行に限り既定値を自動算入する数式にする。
/// - 必須列（学年・最大連続コマ数・空きコマ許可等）の入力規則はallowBlank（空白を許可）にし、誤入力後に
///   空白へ戻せない状態にならないようにする。
/// - 通常授業シートに「学年」列を生徒ID列の隣へ追加する（Python版には無い、本WinUI版独自の追加）。
/// </summary>
internal static class SharedRosterWorkbookWriter
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor HelperHeaderFill = XLColor.FromHtml("#5B9BD5");
    private static readonly XLColor RequiredFill = XLColor.FromHtml("#FFF2CC");
    // 在籍=falseの行をグレー表示にし、一覧上で「この行は使わない」ことが一目で分かるようにする。
    // Python版と同じく、値を書き込む代わりに条件付き書式にすることで、Excel上で在籍チェックを
    // 後から変更しても表示が追従する。
    private static readonly XLColor InactiveRowFill = XLColor.FromHtml("#E7E6E6");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#D9DEE7");

    private const string IdHelperSheetName = "_入力補助";
    private const string GradeListLiteral = "S1,S2,S3,S4,S5,S6,J1,J2,J3,H1,H2,H3";
    private const string NotRegisteredMarker = "⚠ 未登録";

    private const int ReferenceValidationMaxRow = 1000; // 他シート参照のドロップダウン・INDEX/MATCH元範囲
    private const int ReferenceFormulaMaxRow = 1000; // 空白テンプレート行へあらかじめ数式を入れる範囲（Python版・実運用ファイルと同じ1000行）
    private const int DataValidationMaxRow = 10000; // 在籍・学年等、自シート内の入力規則の対象範囲
    private const int ConditionalFormatMaxRow = 10000; // 退籍行グレー表示の対象範囲
    private const int IdHelperCandidateCount = ReferenceFormulaMaxRow - 1;

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

        var studentHeaders = new[] { "在籍", "生徒ID（自動・入力不要）", "姓（必須）", "名", "氏名（確認）", "学年（必須）", "標準最大連続コマ数（デフォルトは2）", "空きコマ許可（デフォルトはなし）", "備考" };
        var student = workbook.AddWorksheet("生徒");
        WriteHeaders(student, studentHeaders, [20, 18, 15, 15, 24, 12, 25, 24, 32]);
        var studentRow = 2;
        foreach (var value in students)
            SetRow(student, studentRow++, value.Active, value.ExternalId, value.FamilyName, value.GivenName, "", value.Grade, value.DefaultMaxConsecutiveSlots, value.AllowGap ? "あり" : "なし", value.Note);
        var studentFormulaEnd = Math.Max(studentRow - 1, ReferenceFormulaMaxRow);
        StyleDataRows(student, studentHeaders.Length, studentRow - 1, [3, 6], blankRowBuffer: ReferenceFormulaMaxRow);
        AddFullNameFormulas(student, nameColumn: 5, familyColumn: 3, givenColumn: 4, lastRow: studentFormulaEnd);
        AddSequentialIdFormulas(student, idColumn: 2, surnameColumn: 3, helperColumnLetter: "A", firstBlankRow: studentRow, formulaEnd: studentFormulaEnd);
        AddDefaultValueFormulas(student, column: 7, conditionColumns: [3], literal: "2", firstBlankRow: studentRow, formulaEnd: studentFormulaEnd);
        AddDefaultValueFormulas(student, column: 8, conditionColumns: [3], literal: "\"なし\"", firstBlankRow: studentRow, formulaEnd: studentFormulaEnd);
        FillBlankActiveFlags(student, activeColumn: 1, firstBlankRow: studentRow, formulaEnd: studentFormulaEnd);
        AddListValidation(student, column: 1, "TRUE,FALSE");
        AddListValidation(student, column: 6, GradeListLiteral);
        AddWholeNumberValidation(student, column: 7, 1, 3);
        AddListValidation(student, column: 8, "あり,なし");
        MarkInactiveRows(student, studentHeaders.Length, activeColumn: 1, keyColumn: 3);
        FinishSheet(student, studentRow - 1, studentHeaders.Length);

        var teacherHeaders = new[] { "在籍", "講師ID（自動・入力不要）", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考" };
        var teacher = workbook.AddWorksheet("講師");
        WriteHeaders(teacher, teacherHeaders, [20, 18, 15, 15, 24, 24, 32]);
        var teacherRow = 2;
        foreach (var value in teachers)
            SetRow(teacher, teacherRow++, value.Active, value.ExternalId, value.FamilyName, value.GivenName, "", value.AllowGap ? "あり" : "なし", value.Note);
        var teacherFormulaEnd = Math.Max(teacherRow - 1, ReferenceFormulaMaxRow);
        StyleDataRows(teacher, teacherHeaders.Length, teacherRow - 1, [3], blankRowBuffer: ReferenceFormulaMaxRow);
        AddFullNameFormulas(teacher, nameColumn: 5, familyColumn: 3, givenColumn: 4, lastRow: teacherFormulaEnd);
        AddSequentialIdFormulas(teacher, idColumn: 2, surnameColumn: 3, helperColumnLetter: "B", firstBlankRow: teacherRow, formulaEnd: teacherFormulaEnd);
        AddDefaultValueFormulas(teacher, column: 6, conditionColumns: [3], literal: "\"なし\"", firstBlankRow: teacherRow, formulaEnd: teacherFormulaEnd);
        FillBlankActiveFlags(teacher, activeColumn: 1, firstBlankRow: teacherRow, formulaEnd: teacherFormulaEnd);
        AddListValidation(teacher, column: 1, "TRUE,FALSE");
        AddListValidation(teacher, column: 6, "あり,なし");
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
        var qualificationFormulaEnd = Math.Max(qualificationRow - 1, ReferenceFormulaMaxRow);
        StyleDataRows(qualification, qualificationHeaders.Length, qualificationRow - 1, []);
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 1, idColumn: 2, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: qualificationRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, qualification, selectColumn: 3, idColumn: 4, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: qualificationRow - 1, idRequired: true);
        AddDefaultValueFormulas(qualification, column: 5, conditionColumns: [1, 3], literal: "\"はい\"", firstBlankRow: qualificationRow, formulaEnd: qualificationFormulaEnd);
        FinishSheet(qualification, qualificationRow - 1, qualificationHeaders.Length);

        var regularHeaders = new[] { "生徒名から選択", "生徒ID（自動・入力不要）", "学年", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考" };
        var regularLesson = workbook.AddWorksheet("通常授業");
        WriteHeaders(regularLesson, regularHeaders, [28, 22, 10, 32, 24, 28, 24, 24, 26, 32], helperColumns: [2, 5, 7]);
        var regularLessonRow = 2;
        foreach (var value in regularLessons)
        {
            if (!studentById.TryGetValue(value.StudentId, out var regularStudent) || !subjectById.TryGetValue(value.SubjectId, out var regularSubject)) continue;
            var regularTeacher = value.RegularTeacherId is long teacherId && teacherById.TryGetValue(teacherId, out var found) ? found : null;
            SetRow(regularLesson, regularLessonRow, 1, regularStudent.FullName, regularStudent.ExternalId);
            SetRow(regularLesson, regularLessonRow, 4, regularSubject.DisplayName, regularSubject.Code, regularTeacher?.FullName ?? "", regularTeacher?.ExternalId ?? "", value.RegularTeacherPriority, value.OneToOneRequired ? "はい" : "いいえ", value.Note);
            regularLessonRow++;
        }
        var regularFormulaEnd = Math.Max(regularLessonRow - 1, ReferenceFormulaMaxRow);
        StyleDataRows(regularLesson, regularHeaders.Length, regularLessonRow - 1, []);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 1, idColumn: 2, sourceSheetName: "生徒", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 4, idColumn: 5, sourceSheetName: "科目", sourceIdColumn: 1, sourceNameColumn: 2, lastDataRow: regularLessonRow - 1, idRequired: true);
        AddReferenceHelperColumn(workbook, regularLesson, selectColumn: 6, idColumn: 7, sourceSheetName: "講師", sourceIdColumn: 2, sourceNameColumn: 5, lastDataRow: regularLessonRow - 1, idRequired: false);
        AddGradeLookupFormulas(regularLesson, gradeColumn: 3, studentIdColumn: 2, lastRow: regularFormulaEnd);
        AddDefaultValueFormulas(regularLesson, column: 8, conditionColumns: [1, 4], literal: "3", firstBlankRow: regularLessonRow, formulaEnd: regularFormulaEnd);
        AddDefaultValueFormulas(regularLesson, column: 9, conditionColumns: [1, 4], literal: "\"いいえ\"", firstBlankRow: regularLessonRow, formulaEnd: regularFormulaEnd);
        FinishSheet(regularLesson, regularLessonRow - 1, regularHeaders.Length);

        CreateIdHelperSheet(workbook, students, teachers);

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
        // ユーザー指示: 必須列でも、誤って入力した値を一度空白へ戻せるようにする（入力規則は常に
        // 空白を許可し、「必須」は黄色のハイライトという視覚的な注意喚起のみで表現する）。
        selectValidation.IgnoreBlanks = true;
        selectValidation.ShowErrorMessage = true;
        selectValidation.ErrorTitle = "一覧にない名前です";
        selectValidation.ErrorMessage = $"{sourceSheetName}シートに登録済みの名前を選択してください。";

        var idValidation = sheet.Range(2, idColumn, ReferenceValidationMaxRow, idColumn).CreateDataValidation();
        idValidation.List(idSourceRange, true);
        idValidation.IgnoreBlanks = true;
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

    // 通常授業シートの生徒ID列の隣に、その生徒の現在の学年を表示する読み取り専用の列を追加する
    // （Python版には無い、本WinUI版独自の追加）。生徒シートの学年が変わっても、再エクスポートの
    // たびに最新の値へ追従する。
    private static void AddGradeLookupFormulas(IXLWorksheet sheet, int gradeColumn, int studentIdColumn, int lastRow)
    {
        var idLetter = ColumnLetter(studentIdColumn);
        for (var row = 2; row <= lastRow; row++)
        {
            var cell = sheet.Cell(row, gradeColumn);
            cell.FormulaA1 = $"IF({idLetter}{row}=\"\",\"\",IFERROR(INDEX(生徒!$F:$F,MATCH({idLetter}{row},生徒!$B:$B,0)),\"{NotRegisteredMarker}\"))";
            ApplyNormalCellStyle(cell);
        }
    }

    // 氏名（確認）列は、既存行・新規行を問わず常に姓・名から組み立てる数式にする（Python版・実運用
    // ファイルと同じ。保存時点の値を直接書き込むと、Excel上で姓・名だけを後から直接編集した場合に
    // 追従しなくなるため）。
    private static void AddFullNameFormulas(IXLWorksheet sheet, int nameColumn, int familyColumn, int givenColumn, int lastRow)
    {
        var familyLetter = ColumnLetter(familyColumn);
        var givenLetter = ColumnLetter(givenColumn);
        for (var row = 2; row <= lastRow; row++)
        {
            var cell = sheet.Cell(row, nameColumn);
            cell.FormulaA1 = $"IF(COUNTA({familyLetter}{row}:{givenLetter}{row})=0,\"\",TRIM({familyLetter}{row}&\" \"&{givenLetter}{row}))";
            ApplyNormalCellStyle(cell);
        }
    }

    // 生徒ID・講師ID列について、まだ姓が未入力の新規行にだけ、非表示の"_入力補助"シートから
    // 次に使える連番IDを自動算出する数式を入れる（既存行は直接値のまま変更しない）。
    private static void AddSequentialIdFormulas(IXLWorksheet sheet, int idColumn, int surnameColumn, string helperColumnLetter, int firstBlankRow, int formulaEnd)
    {
        var surnameLetter = ColumnLetter(surnameColumn);
        for (var row = firstBlankRow; row <= formulaEnd; row++)
        {
            var cell = sheet.Cell(row, idColumn);
            cell.FormulaA1 = $"IF({surnameLetter}{row}=\"\",\"\",INDEX('{IdHelperSheetName}'!${helperColumnLetter}$1:${helperColumnLetter}${IdHelperCandidateCount},COUNTIF(${surnameLetter}${firstBlankRow}:{surnameLetter}{row},\"?*\")))";
            ApplyNormalCellStyle(cell);
        }
    }

    // 標準最大連続コマ数・空きコマ許可・指導可能・担当講師優先度・1対1必須など、既定値のある列は、
    // まだ値の無い新規行に限り既定値を自動算入する数式にする（conditionColumnsのいずれかが埋まる
    // までは空欄のままにし、既存データ行の値は上書きしない）。
    private static void AddDefaultValueFormulas(IXLWorksheet sheet, int column, int[] conditionColumns, string literal, int firstBlankRow, int formulaEnd)
    {
        for (var row = firstBlankRow; row <= formulaEnd; row++)
        {
            var condition = conditionColumns.Length == 1
                ? $"{ColumnLetter(conditionColumns[0])}{row}=\"\""
                : $"OR({string.Join(",", conditionColumns.Select(column => $"{ColumnLetter(column)}{row}=\"\""))})";
            var cell = sheet.Cell(row, column);
            cell.FormulaA1 = $"IF({condition},\"\",{literal})";
            ApplyNormalCellStyle(cell);
        }
    }

    // 在籍チェックは新規行でも既定で「在籍」扱いにする（Python版の空行チェックボックスが既定で
    // オンになっているのと同じ。空行がそのまま見逃されて除外設定になることを防ぐ）。
    private static void FillBlankActiveFlags(IXLWorksheet sheet, int activeColumn, int firstBlankRow, int formulaEnd)
    {
        for (var row = firstBlankRow; row <= formulaEnd; row++)
            sheet.Cell(row, activeColumn).Value = true;
    }

    // 在籍・学年・空きコマ許可等、自シート内で完結する一覧からの選択列の入力規則を追加する。
    // ユーザー指示により、誤入力時に空白へ戻せなくなることを避けるため、必ずallowBlankにする。
    private static void AddListValidation(IXLWorksheet sheet, int column, string commaSeparatedValues)
    {
        var validation = sheet.Range(2, column, DataValidationMaxRow, column).CreateDataValidation();
        validation.List($"\"{commaSeparatedValues}\"");
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorTitle = "入力値を確認してください";
        validation.ErrorMessage = "一覧から選択してください。";
    }

    private static void AddWholeNumberValidation(IXLWorksheet sheet, int column, int min, int max)
    {
        var validation = sheet.Range(2, column, DataValidationMaxRow, column).CreateDataValidation();
        validation.WholeNumber.Between(min, max);
        validation.IgnoreBlanks = true;
        validation.ShowErrorMessage = true;
        validation.ErrorTitle = "入力値を確認してください";
        validation.ErrorMessage = $"{min}～{max}の整数を入力してください。";
    }

    // 非表示の"_入力補助"シートに、生徒ID・講師IDとして次に使える連番候補をあらかじめ計算して
    // 並べておく（Python版のnext_person_external_id相当）。数式側は単純な連番INDEX参照だけで済む
    // ようにするための補助データであり、利用者が直接編集することは想定していないためveryHiddenにする。
    private static void CreateIdHelperSheet(XLWorkbook workbook, IReadOnlyList<Student> students, IReadOnlyList<Teacher> teachers)
    {
        var sheet = workbook.AddWorksheet(IdHelperSheetName);
        var studentIds = GenerateCandidateIds(students.Select(s => s.ExternalId), "S", IdHelperCandidateCount);
        var teacherIds = GenerateCandidateIds(teachers.Select(t => t.ExternalId), "T", IdHelperCandidateCount);
        for (var i = 0; i < IdHelperCandidateCount; i++)
        {
            sheet.Cell(i + 1, 1).Value = studentIds[i];
            sheet.Cell(i + 1, 2).Value = teacherIds[i];
        }
        sheet.Visibility = XLWorksheetVisibility.VeryHidden;
    }

    private static IReadOnlyList<string> GenerateCandidateIds(IEnumerable<string> existingExternalIds, string prefix, int count)
    {
        var pattern = new Regex($"^{Regex.Escape(prefix)}-?(\\d+)$", RegexOptions.IgnoreCase);
        var used = new HashSet<int>();
        foreach (var id in existingExternalIds)
        {
            var match = pattern.Match(id.Trim());
            if (match.Success) used.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        var result = new List<string>(count);
        var number = 1;
        while (result.Count < count)
        {
            if (used.Add(number)) result.Add($"{prefix}-{number.ToString("D4", CultureInfo.InvariantCulture)}");
            number++;
        }
        return result;
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

    // 在籍（文字列ではなく真偽値FALSE）かつ姓が入力済みの行をPython版と同じ配色でグレー表示する。
    // 条件付き書式にすることで、Excel上で後から在籍チェックを変更しても表示が追従する。
    private static void MarkInactiveRows(IXLWorksheet sheet, int columnCount, int activeColumn, int keyColumn)
    {
        var activeLetter = ColumnLetter(activeColumn);
        var keyLetter = ColumnLetter(keyColumn);
        var range = sheet.Range(2, 1, ConditionalFormatMaxRow, columnCount);
        range.AddConditionalFormat().WhenIsTrue($"AND(${keyLetter}2<>\"\",${activeLetter}2=FALSE)").Fill.BackgroundColor = InactiveRowFill;
    }

    private static void FinishSheet(IXLWorksheet sheet, int lastDataRow, int columnCount)
    {
        var rangeLastRow = Math.Max(lastDataRow, 2);
        sheet.Range(1, 1, rangeLastRow, columnCount).SetAutoFilter();
    }

    private static void SetRow(IXLWorksheet sheet, int row, params object[] values) => SetRow(sheet, row, 1, values);

    private static void SetRow(IXLWorksheet sheet, int row, int startColumn, params object[] values)
    {
        for (var index = 0; index < values.Length; index++) sheet.Cell(row, startColumn + index).Value = XLCellValue.FromObject(values[index]);
    }
}
