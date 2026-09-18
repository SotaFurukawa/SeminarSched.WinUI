using ClosedXML.Excel;
using SeminarSched.Domain.MasterData;

namespace SeminarSched.Infrastructure.MasterData;

/// <summary>
/// Python版shared_roster.pyのwrite_shared_roster相当。<see cref="SharedRosterImportService"/>が
/// 読み取れる「生徒・講師_基本情報.xlsx」形式（生徒/講師/科目/講師対応科目/通常授業の5シート）を
/// 書き出す。Python版と異なり、ID列は数式・入力補助シートではなく既存のExternalIdをそのまま
/// 値として書き込む（新規行はユーザーがIDを直接入力する）。
/// </summary>
internal static class SharedRosterWorkbookWriter
{
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
            SetRow(student, studentRow++, value.Active ? "TRUE" : "FALSE", value.ExternalId, surname, given, value.Name, value.Grade, value.DefaultMaxConsecutiveSlots, value.AllowGap ? "あり" : "なし", value.Note);
        }

        var teacher = workbook.AddWorksheet("講師");
        WriteHeaders(teacher, ["在籍", "講師ID", "姓（必須）", "名", "氏名（確認）", "空きコマ許可（デフォルトはなし）", "備考"]);
        var teacherRow = 2;
        foreach (var value in teachers)
        {
            var (surname, given) = SplitName(value.Name);
            SetRow(teacher, teacherRow++, value.Active ? "TRUE" : "FALSE", value.ExternalId, surname, given, value.Name, value.AllowGap ? "あり" : "なし", value.Note);
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

        var regularLesson = workbook.AddWorksheet("通常授業");
        WriteHeaders(regularLesson, ["生徒名から選択", "生徒ID（自動・入力不要）", "科目名から選択", "科目コード（自動・入力不要）", "通常担当講師名から選択", "通常担当講師ID（自動・入力不要）", "担当講師優先度（デフォルトは3）", "1対1必須（デフォルトはいいえ）", "備考"]);
        var regularLessonRow = 2;
        foreach (var value in regularLessons)
        {
            if (!studentById.TryGetValue(value.StudentId, out var regularStudent) || !subjectById.TryGetValue(value.SubjectId, out var regularSubject)) continue;
            var regularTeacher = value.RegularTeacherId is long teacherId && teacherById.TryGetValue(teacherId, out var found) ? found : null;
            SetRow(regularLesson, regularLessonRow++, regularStudent.Name, regularStudent.ExternalId, regularSubject.DisplayName, regularSubject.Code, regularTeacher?.Name ?? "", regularTeacher?.ExternalId ?? "", value.RegularTeacherPriority, value.OneToOneRequired ? "はい" : "いいえ", value.Note);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new ArgumentException("The path has no parent directory.", nameof(path));
        var temporary = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(temporary);
        File.Move(temporary, path, overwrite: true);
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
