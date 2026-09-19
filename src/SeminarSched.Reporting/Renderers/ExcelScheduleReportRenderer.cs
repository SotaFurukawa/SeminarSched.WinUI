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

        // report.Rowsだけから作ると、配置が1件も無い（が出勤不可情報だけ提出済みの）講師がTeacherUnavailabilities
        // 側にしか登場せずKeyNotFoundExceptionになるため、両方の集合の講師名を渡す。
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher).Concat(report.TeacherUnavailabilities.Select(u => u.Teacher)));
        var studentLabels = WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x => x.Student));
        var overviewAssignments = report.Rows.Select(r => new OverviewAssignment(DateOnly.Parse(r.Date), teacherLabels[r.Teacher], r.TimeSlot, r.StudentGrade, r.SubjectShortName, studentLabels[r.Student])).ToArray();
        var overviewUnavailabilities = report.TeacherUnavailabilities.Select(u => new OverviewUnavailability(DateOnly.Parse(u.Date), teacherLabels[u.Teacher], u.TimeSlot)).ToArray();
        var grid = OverviewGridLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, overviewAssignments, overviewUnavailabilities);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);

        var usedNames = new HashSet<string>(StringComparer.Ordinal) { "出力情報" };
        foreach (var week in grid.Weeks)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"週_{week.SundayStart:yyyyMMdd}"));
            WriteOverviewWeekSheet(sheet, week, grid.SlotLabels, slotDefinitionsByLabel);
        }
        workbook.SaveAs(path);
    }

    public void RenderStudentHandouts(ScheduleReport report, string path) => RenderHandoutWorkbook(report, path, includeTeacher: false, ParticipatingStudents(report));

    public void RenderTeacherHandouts(ScheduleReport report, string path) => RenderHandoutWorkbook(report, path, includeTeacher: true, ParticipatingStudents(report));

    /// <summary>
    /// Python版6節「講師配布時間割（講師別）」相当。講師ごとに独立したファイルとして、参加する全生徒の
    /// 個人時間割を1生徒1シートで生成する（フィルタはしない）。並び順だけがこの講師の通常担当の生徒→
    /// この講師が今期担当する生徒→残り全員、という3段階（各段の中では元の学年順を維持）になる
    /// （`build_teacher_packet_document`の`ordered_students`と同じロジック。他の全講師のファイルも
    /// 中身の生徒集合は同じで、並び順だけがそれぞれの講師視点で変わる）。
    /// </summary>
    public void RenderTeacherPacket(ScheduleReport report, string teacherName, string path)
    {
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);

        var allStudents = ParticipatingStudents(report);
        var regularIds = report.Rows.Where(r => r.Teacher == teacherName && r.IsRegularTeacher).Select(r => r.Student).ToHashSet();
        var seasonalIds = report.Rows.Where(r => r.Teacher == teacherName).Select(r => r.Student).ToHashSet();
        var ordered = allStudents.Where(s => regularIds.Contains(s.Student))
            .Concat(allStudents.Where(s => seasonalIds.Contains(s.Student) && !regularIds.Contains(s.Student)))
            .Concat(allStudents.Where(s => !regularIds.Contains(s.Student) && !seasonalIds.Contains(s.Student)))
            .ToArray();

        foreach (var s in ordered)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{s.Grade}_{s.Student}_講師別"));
            WriteStudentHandoutPage(sheet, report, s.Student, s.Grade, includeTeacher: true, teacherLabels);
        }
        if (ordered.Length == 0 && report.AbsentStudents.Count == 0) workbook.AddWorksheet("出力対象がありません");
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
    private static readonly XLColor HandoutWeekdayFill = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor HandoutMonthFill = XLColor.FromHtml("#0B3041");
    private static readonly XLColor HandoutClosedFill = XLColor.FromHtml("#E8E8E8");
    private static readonly XLColor HandoutOutOfRangeFill = XLColor.FromHtml("#0E2841");

    private static void WriteStudentHandoutPage(IXLWorksheet sheet, ScheduleReport report, string student, string grade, bool includeTeacher, IReadOnlyDictionary<string, string> teacherLabels)
    {
        sheet.Column(1).Width = 8.3; sheet.Column(2).Width = 11.4; for (var c = 3; c <= 9; c++) sheet.Column(c).Width = 9.2;

        sheet.Range(1, 1, 1, 9).Merge(); var title = sheet.Cell(1, 1);
        title.Value = $"{report.AcademicYear}　{report.SeasonName}　個別指導　受講日のご案内";
        title.Style.Font.Bold = true; title.Style.Font.FontSize = 16; title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var (schoolLevel, gradeNumber) = ParseGrade(grade);
        sheet.Cell(4, 2).Value = schoolLevel; sheet.Cell(4, 3).Value = gradeNumber; sheet.Cell(4, 4).Value = "年生";
        sheet.Range(4, 6, 4, 7).Merge(); sheet.Cell(4, 6).Value = student; sheet.Cell(4, 8).Value = "様";

        var rows = report.Rows.Where(x => x.Student == student).ToArray();
        var lessonTextByDateSlot = rows.ToDictionary(x => (DateOnly.Parse(x.Date), x.TimeSlot), string (x) => includeTeacher ? $"{x.SubjectShortName}　{teacherLabels[x.Teacher]}" : x.SubjectShortName);
        var weeks = HandoutPageLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, lessonTextByDateSlot);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);

        var row = 7;
        foreach (var week in weeks)
        {
            if (week.FullyClosed)
            {
                sheet.Range(row, 1, row, 9).Merge();
                var closedCell = sheet.Cell(row, 1);
                closedCell.Value = $"{week.Days[0].Date.Month}/{week.Days[0].Date.Day} ~ {week.Days[6].Date.Month}/{week.Days[6].Date.Day}　休校日";
                closedCell.Style.Fill.BackgroundColor = HandoutClosedFill;
                row++; continue;
            }
            var monthRow = row; var weekdayRow = row + 1; var dayRow = row + 2;
            sheet.Cell(monthRow, 1).Style.Fill.BackgroundColor = HandoutWeekdayFill; sheet.Cell(monthRow, 2).Style.Fill.BackgroundColor = HandoutWeekdayFill;
            var col = 3;
            foreach (var monthGroup in week.Days.GroupBy(d => d.Date.Month))
            {
                var span = monthGroup.Count();
                var monthCell = sheet.Cell(monthRow, col);
                monthCell.Value = $"{monthGroup.Key}月"; monthCell.Style.Font.Bold = true; monthCell.Style.Font.FontColor = XLColor.White; monthCell.Style.Fill.BackgroundColor = HandoutMonthFill;
                if (span > 1) sheet.Range(monthRow, col, monthRow, col + span - 1).Merge();
                col += span;
            }
            for (var i = 0; i < 7; i++)
            {
                var weekdayCell = sheet.Cell(weekdayRow, 3 + i); weekdayCell.Value = WeeklyCalendarLayout.WeekdayHeaders[i]; weekdayCell.Style.Fill.BackgroundColor = HandoutWeekdayFill;
                var dayCell = sheet.Cell(dayRow, 3 + i); dayCell.Value = week.Days[i].Date.Day.ToString(); dayCell.Style.Fill.BackgroundColor = HandoutWeekdayFill;
            }
            row = dayRow + 1;
            var slotBlockStartRow = row;
            foreach (var slotRow in week.SlotRows)
            {
                var slotDefinition = slotDefinitionsByLabel[slotRow.SlotLabel];
                sheet.Cell(row, 1).Value = $"{slotDefinition.Code}タイム"; sheet.Cell(row, 2).Value = slotDefinition.TimeRangeText;
                for (var i = 0; i < 7; i++)
                {
                    if (week.Days[i].Kind != HandoutDayKind.Open) continue;
                    sheet.Cell(row, 3 + i).Value = slotRow.LessonTextByDay[i] ?? "";
                }
                row++;
            }
            // Python版は休校日・範囲外セルを日付列単位でコマ数ぶん縦結合し、1つの値だけを表示する。
            var slotBlockRowCount = week.SlotRows.Count;
            for (var i = 0; i < 7; i++)
            {
                var day = week.Days[i];
                if (day.Kind == HandoutDayKind.Open) continue;
                var cell = sheet.Cell(slotBlockStartRow, 3 + i);
                cell.Value = day.Kind == HandoutDayKind.OutOfRange ? "指定範囲外" : "休校日";
                cell.Style.Fill.BackgroundColor = day.Kind == HandoutDayKind.OutOfRange ? HandoutOutOfRangeFill : HandoutClosedFill;
                if (slotBlockRowCount > 1) sheet.Range(slotBlockStartRow, 3 + i, slotBlockStartRow + slotBlockRowCount - 1, 3 + i).Merge();
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

    private static readonly XLColor OverviewTitleFill = XLColor.FromHtml("#D9EAF7");
    private static readonly XLColor OverviewSubtitleFill = XLColor.FromHtml("#EAF0F6");
    private static readonly XLColor OverviewHeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor OverviewUnavailableFill = XLColor.FromHtml("#D9D9D9");
    private static readonly XLColor OverviewFootnoteFill = XLColor.FromHtml("#F0F2F5");
    private const string OverviewLegendText = "凡例　灰色: 勤務不可コマ　[1対1] 1対1　[集団] 集団授業　[固定] ロック　[警告] 警告　[手] 手動変更";
    private const string OverviewFootnoteText = "日曜始まり・土曜終わりの週単位です。出勤予定の講師のみ表示します。";

    /// <summary>
    /// Python版timetable_builder.pyの週単位grid相当。日付panelごとに専用の「コマ」ラベル列を持ち
    /// （ラベル列＋出勤講師ごとに2列＝同時最大2名までの並び表示）、コマごと3行（学年／科目略称／
    /// 生徒名縦書き）で表示する。該当日・出勤予定講師が1件も無い週もsheet自体は生成し、
    /// 「対象となる開校日・出勤予定講師がありません」のplaceholderを表示する。
    /// </summary>
    private static void WriteOverviewWeekSheet(IXLWorksheet sheet, OverviewWeek week, IReadOnlyList<string> slotLabels, IReadOnlyDictionary<string, SlotDefinition> slotDefinitionsByLabel)
    {
        const int dateHeaderRow = 3, comaHeaderRow = 4, slotStartRow = 5;
        sheet.Cell(1, 1).Value = "季節講習時間割"; sheet.Cell(1, 1).Style.Fill.BackgroundColor = OverviewTitleFill;
        var sundayEnd = week.SundayStart.AddDays(6);
        sheet.Cell(2, 1).Value = $"{week.SundayStart:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[0]}） ～ {sundayEnd:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[6]}）";
        sheet.Cell(2, 1).Style.Fill.BackgroundColor = OverviewSubtitleFill;

        int lastCol; int legendRow;
        if (week.Days.Count == 0)
        {
            sheet.Cell(dateHeaderRow, 1).Value = "対象となる開校日・出勤予定講師がありません"; sheet.Cell(dateHeaderRow, 1).Style.Fill.BackgroundColor = OverviewSubtitleFill;
            lastCol = 1; legendRow = dateHeaderRow + 1;
        }
        else
        {
            var col = 1;
            foreach (var day in week.Days)
            {
                var labelCol = col; col++;
                var comaCell = sheet.Cell(comaHeaderRow, labelCol); comaCell.Value = "コマ"; comaCell.Style.Font.Bold = true; comaCell.Style.Fill.BackgroundColor = OverviewSubtitleFill;

                if (day.Teachers.Count == 0)
                {
                    var noneCell = sheet.Cell(comaHeaderRow, labelCol + 1); noneCell.Value = "出勤予定なし"; noneCell.Style.Fill.BackgroundColor = OverviewUnavailableFill;
                    sheet.Range(comaHeaderRow, labelCol + 1, comaHeaderRow, labelCol + 2).Merge();
                    if (slotLabels.Count > 0) sheet.Range(slotStartRow, labelCol + 1, slotStartRow + slotLabels.Count * 3 - 1, labelCol + 2).Style.Fill.BackgroundColor = OverviewUnavailableFill;
                    col += 2;
                }
                else
                {
                    var teacherCol = labelCol + 1;
                    foreach (var teacher in day.Teachers)
                    {
                        var teacherCell = sheet.Cell(comaHeaderRow, teacherCol); teacherCell.Value = teacher.TeacherName; teacherCell.Style.Font.Bold = true; teacherCell.Style.Font.FontColor = XLColor.White; teacherCell.Style.Fill.BackgroundColor = OverviewHeaderFill;
                        sheet.Range(comaHeaderRow, teacherCol, comaHeaderRow, teacherCol + 1).Merge();

                        for (var s = 0; s < slotLabels.Count; s++)
                        {
                            var rowBase = slotStartRow + s * 3; var cell = teacher.Cells[s];
                            for (var sub = 0; sub < 2; sub++)
                            {
                                var cardCol = teacherCol + sub;
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
                                    for (var r = 0; r < 3; r++) sheet.Cell(rowBase + r, cardCol).Style.Fill.BackgroundColor = OverviewUnavailableFill;
                                }
                            }
                            if (cell.Cards.Count > 2)
                            {
                                var overflow = string.Join("\n", cell.Cards.Skip(2).Select(c => $"{c.Grade} {c.SubjectShortName} {c.Student}"));
                                var overflowCell = sheet.Cell(rowBase + 2, teacherCol + 1);
                                overflowCell.Value = overflowCell.GetString().Length > 0 ? overflowCell.GetString() + "\n" + overflow : overflow;
                                overflowCell.Style.Alignment.WrapText = true; overflowCell.Style.Alignment.TextRotation = 0;
                            }
                        }
                        teacherCol += 2;
                    }
                    col = teacherCol;
                }

                if (col - labelCol > 1) sheet.Range(dateHeaderRow, labelCol, dateHeaderRow, col - 1).Merge();
                var dateCell = sheet.Cell(dateHeaderRow, labelCol);
                dateCell.Value = $"{day.Date:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]}）";
                dateCell.Style.Font.Bold = true; dateCell.Style.Font.FontColor = XLColor.White; dateCell.Style.Fill.BackgroundColor = OverviewHeaderFill; dateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (var s = 0; s < slotLabels.Count; s++)
                {
                    var rowBase = slotStartRow + s * 3;
                    var labelCell = sheet.Cell(rowBase, labelCol);
                    labelCell.Value = slotDefinitionsByLabel[slotLabels[s]].OverviewLabelText;
                    labelCell.Style.Font.Bold = true; labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; labelCell.Style.Alignment.WrapText = true;
                    labelCell.Style.Fill.BackgroundColor = OverviewSubtitleFill;
                    if (slotLabels.Count > 0) sheet.Range(rowBase, labelCol, rowBase + 2, labelCol).Merge();
                }
            }
            lastCol = Math.Max(1, col - 1);
            legendRow = slotStartRow + slotLabels.Count * 3;
        }

        var legendCell = sheet.Cell(legendRow, 1); legendCell.Value = OverviewLegendText; legendCell.Style.Fill.BackgroundColor = OverviewFootnoteFill;
        var footnoteCell = sheet.Cell(legendRow + 1, 1); footnoteCell.Value = OverviewFootnoteText; footnoteCell.Style.Fill.BackgroundColor = OverviewFootnoteFill;
        if (lastCol > 1)
        {
            sheet.Range(1, 1, 1, lastCol).Merge(); sheet.Range(2, 1, 2, lastCol).Merge();
            sheet.Range(legendRow, 1, legendRow, lastCol).Merge(); sheet.Range(legendRow + 1, 1, legendRow + 1, lastCol).Merge();
            sheet.Columns(1, lastCol).Width = 2.9;
        }
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
