using ClosedXML.Excel;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

/// <summary>
/// Python版reporting/*_builder.pyが生成する5種の帳票（全体時間割・生徒配布時間割・講師配布時間割
/// （学年順）・講師配布時間割（講師別、講師ごとの個別ファイル）・未配置警告一覧）を、それぞれ独立した
/// xlsxとして出力する。Python版と異なりcampus（校舎）概念は本移植版に存在しないため、出力情報シートの
/// 「校舎・講習」欄はProjectTitleのみを表示する。また、Python版が行う「休校日・範囲外セルの連続結合」
/// 「補足の集団授業シート」（本移植版に集団授業の概念自体が無いため対象外）は簡略化・対象外としている。
/// </summary>
public sealed class ExcelScheduleReportRenderer
{
    public void RenderOverall(ScheduleReport report, string path)
    {
        using var workbook = new XLWorkbook();
        WriteOverviewMetadataSheet(workbook.AddWorksheet("出力情報"), report);

        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        var studentLabels = WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x => x.Student));
        var overviewAssignments = report.Rows.Select(r => new OverviewAssignment(DateOnly.Parse(r.Date), teacherLabels[r.Teacher], r.TimeSlot, r.StudentGrade, r.SubjectShortName, studentLabels[r.Student])).ToArray();
        var overviewUnavailabilities = report.TeacherUnavailabilities.Select(u => new OverviewUnavailability(DateOnly.Parse(u.Date), teacherLabels[u.Teacher], u.TimeSlot)).ToArray();
        var grid = OverviewGridLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, overviewAssignments, overviewUnavailabilities);

        var usedNames = new HashSet<string>(StringComparer.Ordinal) { "出力情報" };
        foreach (var week in grid.Weeks)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"週_{week.SundayStart:yyyyMMdd}"));
            WriteOverviewWeekSheet(sheet, week, grid.SlotLabels);
        }
        workbook.SaveAs(path);
    }

    public void RenderStudentHandouts(ScheduleReport report, string path) => RenderHandoutWorkbook(report, path, includeTeacher: false, ParticipatingStudents(report));

    public void RenderTeacherHandouts(ScheduleReport report, string path) => RenderHandoutWorkbook(report, path, includeTeacher: true, ParticipatingStudents(report));

    /// <summary>
    /// Python版6節「講師配布時間割（講師別）」相当。講師ごとに独立したファイルとして、その講師が担当する
    /// 生徒（通常担当の生徒を先に、季節講習のみ担当する生徒を後に列挙）の個人時間割を1生徒1シートで生成する。
    /// </summary>
    public void RenderTeacherPacket(ScheduleReport report, string teacherName, string path)
    {
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);

        var assigned = report.Rows.Where(x => x.Teacher == teacherName).Select(x => new { x.Student, x.StudentGrade }).Distinct().ToArray();
        var regular = assigned.Where(s => report.Rows.Any(r => r.Teacher == teacherName && r.Student == s.Student && r.IsRegularTeacher)).OrderBy(s => GradeOrdering.SortKey(s.StudentGrade)).ThenBy(s => s.Student, StringComparer.Ordinal).ToArray();
        var others = assigned.Except(regular).OrderBy(s => GradeOrdering.SortKey(s.StudentGrade)).ThenBy(s => s.Student, StringComparer.Ordinal).ToArray();

        foreach (var s in regular.Concat(others))
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{s.StudentGrade}_{s.Student}_講師別"));
            WriteStudentHandoutPage(sheet, report, s.Student, s.StudentGrade, includeTeacher: true, teacherLabels);
        }
        if (regular.Length == 0 && others.Length == 0 && report.AbsentStudents.Count == 0) workbook.AddWorksheet("出力対象がありません");
        workbook.SaveAs(path);
    }

    public void RenderIssues(ScheduleReport report, string path)
    {
        using var workbook = new XLWorkbook();

        var unassignedSheet = workbook.AddWorksheet("未配置一覧");
        string[] unassignedHeaders = ["生徒", "科目", "必要", "配置済", "不足", "主な理由", "解決候補", "優先度", "通常担当", "1対1", "備考"];
        for (var i = 0; i < unassignedHeaders.Length; i++) unassignedSheet.Cell(1, i + 1).Value = unassignedHeaders[i];
        unassignedSheet.Row(1).Style.Font.Bold = true; unassignedSheet.SheetView.FreezeRows(1);
        for (var i = 0; i < report.UnassignedRequests.Count; i++)
        {
            var r = report.UnassignedRequests[i]; var row = i + 2;
            unassignedSheet.Cell(row, 1).Value = r.Student; unassignedSheet.Cell(row, 2).Value = r.Subject;
            unassignedSheet.Cell(row, 3).Value = r.Required; unassignedSheet.Cell(row, 4).Value = r.Placed; unassignedSheet.Cell(row, 5).Value = r.Missing;
            unassignedSheet.Cell(row, 6).Value = r.MainReason;
            unassignedSheet.Cell(row, 7).Value = r.ResolutionCandidates.Count > 0 ? string.Join("／", r.ResolutionCandidates) : "候補なし";
            unassignedSheet.Cell(row, 8).Value = r.Priority;
            unassignedSheet.Cell(row, 9).Value = r.RegularTeacher ?? "未設定";
            unassignedSheet.Cell(row, 10).Value = r.OneToOneRequired ? "必須" : "通常";
            unassignedSheet.Cell(row, 11).Value = r.Note;
        }
        double[] unassignedWidths = [15, 12, 7, 7, 7, 25, 28, 8, 15, 9, 20];
        for (var i = 0; i < unassignedWidths.Length; i++) unassignedSheet.Column(i + 1).Width = unassignedWidths[i];

        var warningSheet = workbook.AddWorksheet("警告一覧");
        string[] warningHeaders = ["severity", "issue type", "日付", "コマ", "生徒", "講師", "内容", "対応状況"];
        for (var i = 0; i < warningHeaders.Length; i++) warningSheet.Cell(1, i + 1).Value = warningHeaders[i];
        warningSheet.Row(1).Style.Font.Bold = true; warningSheet.SheetView.FreezeRows(1);
        for (var i = 0; i < report.Warnings.Count; i++)
        {
            var w = report.Warnings[i]; var row = i + 2;
            warningSheet.Cell(row, 1).Value = w.Severity; warningSheet.Cell(row, 2).Value = w.IssueType;
            warningSheet.Cell(row, 3).Value = w.Date ?? "—"; warningSheet.Cell(row, 4).Value = w.Slot ?? "—";
            warningSheet.Cell(row, 5).Value = w.Student; warningSheet.Cell(row, 6).Value = w.Teacher;
            warningSheet.Cell(row, 7).Value = w.Content; warningSheet.Cell(row, 8).Value = w.Status;
        }
        double[] warningWidths = [10, 17, 14, 8, 15, 15, 36, 12];
        for (var i = 0; i < warningWidths.Length; i++) warningSheet.Column(i + 1).Width = warningWidths[i];

        workbook.SaveAs(path);
    }

    private static void RenderHandoutWorkbook(ScheduleReport report, string path, bool includeTeacher, IReadOnlyList<(string Student, string Grade)> students)
    {
        using var workbook = new XLWorkbook();
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);
        foreach (var (student, grade) in students)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{grade}_{student}"));
            WriteStudentHandoutPage(sheet, report, student, grade, includeTeacher, teacherLabels);
        }
        workbook.SaveAs(path);
    }

    private static IReadOnlyList<(string Student, string Grade)> ParticipatingStudents(ScheduleReport report) =>
        report.Rows.GroupBy(x => new { x.Student, x.StudentGrade }).OrderBy(x => GradeOrdering.SortKey(x.Key.StudentGrade)).ThenBy(x => x.Key.Student, StringComparer.Ordinal)
            .Select(g => (g.Key.Student, g.Key.StudentGrade)).ToArray();

    /// <summary>
    /// Python版distribution_builder.pyの生徒個人calendarページ（9列A:I）相当。student_handouts・
    /// teacher_handouts・teacher_packetsの3レポートすべてがこの1メソッドを共有する（includeTeacherの
    /// 有無だけが異なる）。
    /// </summary>
    private static void WriteStudentHandoutPage(IXLWorksheet sheet, ScheduleReport report, string student, string grade, bool includeTeacher, IReadOnlyDictionary<string, string> teacherLabels)
    {
        sheet.Column(1).Width = 9.0; sheet.Column(2).Width = 12.125; for (var c = 3; c <= 9; c++) sheet.Column(c).Width = 9.875;

        sheet.Range(1, 1, 1, 9).Merge(); var title = sheet.Cell(1, 1);
        title.Value = $"{report.AcademicYear}　{report.SeasonName}　個別指導　受講日のご案内";
        title.Style.Font.Bold = true; title.Style.Font.FontSize = 16; title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var (schoolLevel, gradeNumber) = ParseGrade(grade);
        sheet.Cell(4, 2).Value = schoolLevel; sheet.Cell(4, 3).Value = gradeNumber; sheet.Cell(4, 4).Value = "年生";
        sheet.Range(4, 6, 4, 7).Merge(); sheet.Cell(4, 6).Value = student; sheet.Cell(4, 8).Value = "様";

        var rows = report.Rows.Where(x => x.Student == student).ToArray();
        if (includeTeacher)
        {
            var summary = rows.Where(x => x.IsRegularTeacher).Select(x => $"{x.SubjectShortName} {teacherLabels[x.Teacher]}t").Distinct().ToArray();
            sheet.Range(5, 1, 5, 9).Merge();
            sheet.Cell(5, 1).Value = summary.Length > 0 ? string.Join("　", summary) : "通常担当：―";
        }

        var lessonTextByDateSlot = rows.ToDictionary(x => (DateOnly.Parse(x.Date), x.TimeSlot), string (x) => includeTeacher ? $"{x.SubjectShortName}　{teacherLabels[x.Teacher]}" : x.SubjectShortName);
        var weeks = HandoutPageLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, lessonTextByDateSlot);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);

        var row = 7;
        foreach (var week in weeks)
        {
            if (week.FullyClosed)
            {
                sheet.Range(row, 1, row, 9).Merge();
                sheet.Cell(row, 1).Value = $"{week.Days[0].Date.Month}/{week.Days[0].Date.Day} ~ {week.Days[6].Date.Month}/{week.Days[6].Date.Day}　休校日";
                row++; continue;
            }
            var monthRow = row; var weekdayRow = row + 1; var dayRow = row + 2;
            var col = 3;
            foreach (var monthGroup in week.Days.GroupBy(d => d.Date.Month))
            {
                var span = monthGroup.Count();
                var monthCell = sheet.Cell(monthRow, col);
                monthCell.Value = $"{monthGroup.Key}月"; monthCell.Style.Font.Bold = true; monthCell.Style.Font.FontColor = XLColor.White; monthCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
                if (span > 1) sheet.Range(monthRow, col, monthRow, col + span - 1).Merge();
                col += span;
            }
            for (var i = 0; i < 7; i++) { sheet.Cell(weekdayRow, 3 + i).Value = WeeklyCalendarLayout.WeekdayHeaders[i]; sheet.Cell(dayRow, 3 + i).Value = week.Days[i].Date.Day.ToString(); }
            row = dayRow + 1;
            foreach (var slotRow in week.SlotRows)
            {
                var slotDefinition = slotDefinitionsByLabel[slotRow.SlotLabel];
                sheet.Cell(row, 1).Value = $"{slotDefinition.Code}タイム"; sheet.Cell(row, 2).Value = slotDefinition.TimeRangeText;
                for (var i = 0; i < 7; i++)
                {
                    var day = week.Days[i];
                    sheet.Cell(row, 3 + i).Value = day.Kind switch { HandoutDayKind.OutOfRange => "指定範囲外", HandoutDayKind.ClosedDay => "休校日", _ => slotRow.LessonTextByDay[i] ?? "" };
                }
                row++;
            }
        }

        row++;
        sheet.Range(row, 1, row, 4).Merge(); sheet.Cell(row, 1).Value = $"学力テスト　　{grade}　　日時：";
        sheet.Range(row, 5, row, 9).Merge(); sheet.Cell(row, 5).Value = "受験する・受験しない";
    }

    private static void WriteAbsenceSheet(IXLWorksheet sheet, ScheduleReport report)
    {
        sheet.Column(1).Width = 9.0; sheet.Column(2).Width = 12.125; for (var c = 3; c <= 9; c++) sheet.Column(c).Width = 9.875;
        sheet.Range(1, 1, 1, 9).Merge(); var title = sheet.Cell(1, 1);
        title.Value = "講習欠席一覧"; title.Style.Font.Bold = true; title.Style.Font.FontSize = 16; title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(3, 1, 3, 3).Merge(); sheet.Cell(3, 1).Value = "学年"; sheet.Cell(3, 1).Style.Font.Bold = true;
        sheet.Range(3, 4, 3, 9).Merge(); sheet.Cell(3, 4).Value = "生徒名"; sheet.Cell(3, 4).Style.Font.Bold = true;
        var row = 4;
        if (report.AbsentStudents.Count == 0) { sheet.Range(row, 1, row, 9).Merge(); sheet.Cell(row, 1).Value = "該当者はいません"; }
        else foreach (var student in report.AbsentStudents.OrderBy(s => GradeOrdering.SortKey(s.Grade)).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            sheet.Range(row, 1, row, 3).Merge(); sheet.Cell(row, 1).Value = student.Grade;
            sheet.Range(row, 4, row, 9).Merge(); sheet.Cell(row, 4).Value = student.Name;
            row++;
        }
    }

    private static void WriteOverviewMetadataSheet(IXLWorksheet sheet, ScheduleReport report)
    {
        sheet.Column(1).Width = 24; sheet.Column(2).Width = 54; sheet.Column(1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Value = "帳票名"; sheet.Cell(1, 2).Value = "季節講習時間割";
        sheet.Cell(2, 1).Value = "校舎・講習"; sheet.Cell(2, 2).Value = report.ProjectTitle;
        sheet.Cell(3, 1).Value = "更新日時"; sheet.Cell(3, 2).Value = report.GeneratedAtText;
    }

    /// <summary>
    /// Python版timetable_builder.pyの週単位grid相当。パネル共通の「コマ」ラベル列を先頭に1本だけ置き
    /// （Python版はパネルごとにラベル列を持つが、視認性を優先しここでは1本へ共通化）、各日は
    /// 出勤講師ごとに2列（最大同時2名までの並び表示）×コマごと3行（学年／科目略称／生徒名縦書き）で
    /// 表示する。
    /// </summary>
    private static void WriteOverviewWeekSheet(IXLWorksheet sheet, OverviewWeek week, IReadOnlyList<string> slotLabels)
    {
        const int dayHeaderRow = 1, teacherHeaderRow = 2, slotStartRow = 3;
        sheet.Cell(teacherHeaderRow, 1).Value = "コマ"; sheet.Cell(teacherHeaderRow, 1).Style.Font.Bold = true;

        var col = 2;
        foreach (var day in week.Days)
        {
            var teacherCount = Math.Max(1, day.Teachers.Count);
            var dayStartCol = col; var totalCols = teacherCount * 2;
            var dayCell = sheet.Cell(dayHeaderRow, dayStartCol);
            dayCell.Value = $"{day.Date:M/d}({WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]})";
            dayCell.Style.Font.Bold = true; dayCell.Style.Fill.BackgroundColor = XLColor.LightGray; dayCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (totalCols > 1) sheet.Range(dayHeaderRow, dayStartCol, dayHeaderRow, dayStartCol + totalCols - 1).Merge();

            if (day.Teachers.Count == 0)
            {
                var noneCell = sheet.Cell(teacherHeaderRow, dayStartCol); noneCell.Value = "出勤予定なし"; noneCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                sheet.Range(teacherHeaderRow, dayStartCol, teacherHeaderRow, dayStartCol + 1).Merge();
                if (slotLabels.Count > 0) sheet.Range(slotStartRow, dayStartCol, slotStartRow + slotLabels.Count * 3 - 1, dayStartCol + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
                col += 2; continue;
            }

            foreach (var teacher in day.Teachers)
            {
                var teacherCell = sheet.Cell(teacherHeaderRow, col); teacherCell.Value = teacher.TeacherName; teacherCell.Style.Font.Bold = true;
                sheet.Range(teacherHeaderRow, col, teacherHeaderRow, col + 1).Merge();

                for (var s = 0; s < slotLabels.Count; s++)
                {
                    var rowBase = slotStartRow + s * 3; var cell = teacher.Cells[s];
                    for (var sub = 0; sub < 2; sub++)
                    {
                        var cardCol = col + sub;
                        if (sub < cell.Cards.Count)
                        {
                            var card = cell.Cards[sub];
                            sheet.Cell(rowBase, cardCol).Value = card.Grade;
                            sheet.Cell(rowBase + 1, cardCol).Value = card.SubjectShortName;
                            var nameCell = sheet.Cell(rowBase + 2, cardCol); nameCell.Value = card.Student;
                            nameCell.Style.Alignment.TextRotation = 255; nameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        else if (cell.Cards.Count == 0 && cell.Unavailable)
                        {
                            for (var r = 0; r < 3; r++) sheet.Cell(rowBase + r, cardCol).Style.Fill.BackgroundColor = XLColor.LightGray;
                        }
                    }
                    if (cell.Cards.Count > 2)
                    {
                        var overflow = string.Join("\n", cell.Cards.Skip(2).Select(c => $"{c.Grade} {c.SubjectShortName} {c.Student}"));
                        var overflowCell = sheet.Cell(rowBase + 2, col + 1);
                        overflowCell.Value = overflowCell.GetString().Length > 0 ? overflowCell.GetString() + "\n" + overflow : overflow;
                        overflowCell.Style.Alignment.WrapText = true; overflowCell.Style.Alignment.TextRotation = 0;
                    }
                }
                col += 2;
            }
        }

        for (var s = 0; s < slotLabels.Count; s++)
        {
            var rowBase = slotStartRow + s * 3;
            var labelCell = sheet.Cell(rowBase, 1); labelCell.Value = slotLabels[s]; labelCell.Style.Font.Bold = true; labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; labelCell.Style.Alignment.WrapText = true;
            sheet.Range(rowBase, 1, rowBase + 2, 1).Merge();
        }

        var lastCol = Math.Max(2, col - 1);
        var legendRow = slotStartRow + slotLabels.Count * 3;
        var legendCell = sheet.Cell(legendRow, 1); legendCell.Value = "凡例　灰色: 勤務不可コマ"; legendCell.Style.Font.Italic = true;
        sheet.Range(legendRow, 1, legendRow, lastCol).Merge();

        sheet.Column(1).Width = 10; sheet.Columns(2, lastCol).Width = 4.5;
        sheet.SheetView.FreezeRows(2);
    }

    private static (string Level, string Number) ParseGrade(string grade)
    {
        if (grade.Length == 0) return ("", "");
        var level = grade[0] switch { '小' => "小学", '中' => "中学", '高' => "高校", _ => "" };
        var number = new string(grade.Where(char.IsDigit).ToArray());
        return (level, number);
    }

    private static string SanitizeSheetName(string name)
    {
        char[] invalid = ['[', ']', ':', '*', '?', '/', '\\'];
        var sanitized = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (sanitized.Length == 0) sanitized = "Sheet";
        return sanitized.Length > 31 ? sanitized[..31] : sanitized;
    }

    private static string UniqueSheetName(HashSet<string> used, string desired)
    {
        var sanitized = SanitizeSheetName(desired);
        var candidate = sanitized; var suffix = 2;
        while (!used.Add(candidate))
        {
            var suffixText = $"_{suffix++}";
            candidate = (sanitized.Length + suffixText.Length > 31 ? sanitized[..(31 - suffixText.Length)] : sanitized) + suffixText;
        }
        return candidate;
    }
}
