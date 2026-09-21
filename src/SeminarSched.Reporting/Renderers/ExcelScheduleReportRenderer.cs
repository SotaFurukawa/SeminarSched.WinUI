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
        var overviewAssignments = report.Rows.Select(r => new OverviewAssignment(DateOnly.Parse(r.Date), teacherLabels[r.Teacher], r.TimeSlot, r.StudentGrade, r.SubjectShortName, studentLabels[r.Student], r.OneToOneRequired, r.IsLocked, r.IsManual)).ToArray();
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

    public void RenderStudentHandouts(ScheduleReport report, string path, HandoutStyleSettings? styleSettings = null) =>
        RenderHandoutWorkbook(report, path, includeTeacher: false, ParticipatingStudents(report), styleSettings ?? HandoutStyleSettings.Default);

    public void RenderTeacherHandouts(ScheduleReport report, string path, HandoutStyleSettings? styleSettings = null) =>
        RenderHandoutWorkbook(report, path, includeTeacher: true, ParticipatingStudents(report), styleSettings ?? HandoutStyleSettings.Default);

    /// <summary>
    /// Python版6節「講師配布時間割（講師別）」相当。講師ごとに独立したファイルとして、参加する全生徒の
    /// 個人時間割を1生徒1シートで生成する（フィルタはしない）。並び順だけがこの講師の通常担当の生徒→
    /// この講師が今期担当する生徒→残り全員、という3段階（各段の中では元の学年順を維持）になる
    /// （`build_teacher_packet_document`の`ordered_students`と同じロジック。他の全講師のファイルも
    /// 中身の生徒集合は同じで、並び順だけがそれぞれの講師視点で変わる）。
    /// </summary>
    public void RenderTeacherPacket(ScheduleReport report, string teacherName, string path, HandoutStyleSettings? styleSettings = null)
    {
        var settings = styleSettings ?? HandoutStyleSettings.Default;
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);
        WriteHandoutStyleTemplateSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, HandoutStyleSheetName)), settings);

        var ordered = OrderStudentsForTeacher(report, teacherName);
        foreach (var s in ordered)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{s.Grade}_{s.Student}_講師別"));
            WriteStudentHandoutPage(sheet, report, s.Student, s.Grade, includeTeacher: true, teacherLabels, settings);
        }
        if (ordered.Count == 0 && report.AbsentStudents.Count == 0) workbook.AddWorksheet("出力対象がありません");
        workbook.SaveAs(path);
    }

    /// <summary>
    /// ユーザー指示による追加機能。従来の「講師配布用講師別時間割」（講師ごとに独立したファイル）とは
    /// 別に、全講師分を1ファイルへまとめた「(一括)」版を作る。印刷時に2シートずつまとめる運用を
    /// 想定しているため、担当生徒数が奇数の講師の後ろには空白シートを1枚挟み、次の講師が必ず
    /// 奇数番目のシートから始まるようにする（そうしないと講師の境界がページの中間へずれてしまう）。
    /// 各生徒シートの左上（A2、通常空欄の行）に「{講師名}t用」と書き、どの講師の束かを明示する。
    /// </summary>
    public void RenderTeacherPacketsCombined(ScheduleReport report, IReadOnlyList<string> teacherNames, string path, HandoutStyleSettings? styleSettings = null)
    {
        var settings = styleSettings ?? HandoutStyleSettings.Default;
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);
        WriteHandoutStyleTemplateSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, HandoutStyleSheetName)), settings);

        var blankIndex = 1;
        foreach (var teacherName in teacherNames.OrderBy(x => x, StringComparer.Ordinal))
        {
            var teacherLabel = teacherLabels[teacherName];
            var ordered = OrderStudentsForTeacher(report, teacherName);
            foreach (var s in ordered)
            {
                var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{s.Grade}_{s.Student}_{teacherLabel}"));
                WriteStudentHandoutPage(sheet, report, s.Student, s.Grade, includeTeacher: true, teacherLabels, settings);
                var label = sheet.Cell(2, 1); label.Value = $"{teacherLabel}t用"; label.Style.Font.Bold = true; label.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }
            if (ordered.Count % 2 != 0) workbook.AddWorksheet(UniqueSheetName(usedNames, $"空白_{blankIndex++}"));
        }
        if (workbook.Worksheets.Count == 0) workbook.AddWorksheet("出力対象がありません");
        workbook.SaveAs(path);
    }

    private static IReadOnlyList<(string Student, string Grade)> OrderStudentsForTeacher(ScheduleReport report, string teacherName)
    {
        var allStudents = ParticipatingStudents(report);
        var regularIds = report.Rows.Where(r => r.Teacher == teacherName && r.IsRegularTeacher).Select(r => r.Student).ToHashSet();
        var seasonalIds = report.Rows.Where(r => r.Teacher == teacherName).Select(r => r.Student).ToHashSet();
        return allStudents.Where(s => regularIds.Contains(s.Student))
            .Concat(allStudents.Where(s => seasonalIds.Contains(s.Student) && !regularIds.Contains(s.Student)))
            .Concat(allStudents.Where(s => !regularIds.Contains(s.Student) && !seasonalIds.Contains(s.Student)))
            .ToArray();
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

    private static void RenderHandoutWorkbook(ScheduleReport report, string path, bool includeTeacher, IReadOnlyList<(string Student, string Grade)> students, HandoutStyleSettings settings)
    {
        using var workbook = new XLWorkbook();
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        if (report.AbsentStudents.Count > 0) WriteAbsenceSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, "講習欠席一覧")), report);
        WriteHandoutStyleTemplateSheet(workbook.AddWorksheet(UniqueSheetName(usedNames, HandoutStyleSheetName)), settings);
        foreach (var (student, grade) in students)
        {
            var sheet = workbook.AddWorksheet(UniqueSheetName(usedNames, $"{grade}_{student}"));
            WriteStudentHandoutPage(sheet, report, student, grade, includeTeacher, teacherLabels, settings);
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
    private static readonly XLColor HandoutClosedFill = XLColor.FromHtml("#E8E8E8");
    private static readonly XLColor HandoutOutOfRangeFill = XLColor.FromHtml("#0E2841");
    private const string HandoutStyleSheetName = "デザイン設定";

    private static void WriteStudentHandoutPage(IXLWorksheet sheet, ScheduleReport report, string student, string grade, bool includeTeacher, IReadOnlyDictionary<string, string> teacherLabels, HandoutStyleSettings settings)
    {
        var titleFill = ParseColorOrDefault(settings.TitleFillHex, XLColor.Black);
        var titleFontColor = ParseColorOrDefault(settings.TitleFontColorHex, XLColor.White);
        var monthFill = ParseColorOrDefault(settings.MonthFillHex, XLColor.FromHtml("#0F243E"));
        var dayFill = ParseColorOrDefault(settings.DayFillHex, XLColor.FromHtml("#90CAFE"));
        var headerBlankFill = ParseColorOrDefault(settings.HeaderBlankFillHex, XLColor.FromHtml("#BFBFBF"));
        var academicTestFill = ParseColorOrDefault(settings.AcademicTestFillHex, XLColor.FromHtml("#95B3D7"));

        // ユーザー指定の列幅（ピクセル）。A=54px, B=96px, C~I=64px。
        sheet.Column(1).Width = PixelsToColumnWidth(54); sheet.Column(2).Width = PixelsToColumnWidth(96);
        for (var c = 3; c <= 9; c++) sheet.Column(c).Width = PixelsToColumnWidth(64);

        // シート全体の既定値: 全マス中央ぞろえ（水平・垂直）、4行目以降のフォントを設定値に統一する
        // （1行目の「ご案内」だけは後で個別に上書きする）。
        sheet.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Style.Font.FontName = settings.BodyFontName;

        sheet.Range(1, 1, 1, 9).Merge(); var title = sheet.Cell(1, 1);
        title.Value = $"{report.AcademicYear}　{report.SeasonName}　個別指導　受講日のご案内";
        title.Style.Font.Bold = true; title.Style.Font.FontSize = settings.TitleFontSize; title.Style.Font.FontName = settings.TitleFontName;
        title.Style.Font.FontColor = titleFontColor; title.Style.Fill.BackgroundColor = titleFill;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var (schoolLevel, gradeNumber) = ParseGrade(grade);
        var schoolLevelCell = sheet.Cell(4, 2); schoolLevelCell.Value = schoolLevel; schoolLevelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        sheet.Cell(4, 3).Value = gradeNumber;
        var gradeSuffixCell = sheet.Cell(4, 4); gradeSuffixCell.Value = "年生"; gradeSuffixCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Range(4, 6, 4, 7).Merge(); var nameCell = sheet.Cell(4, 6); nameCell.Value = student; nameCell.Style.Font.FontSize = settings.NameFontSize;
        sheet.Cell(4, 8).Value = "様";
        sheet.Range(4, 2, 4, 8).Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        var rows = report.Rows.Where(x => x.Student == student).ToArray();
        var lessonTextByDateSlot = rows.ToDictionary(x => (DateOnly.Parse(x.Date), x.TimeSlot), string (x) => includeTeacher ? $"{x.SubjectShortName}　{teacherLabels[x.Teacher]}" : x.SubjectShortName);
        var weeks = HandoutPageLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, lessonTextByDateSlot);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);
        // 集団授業を受講する生徒については、その時間帯を個別指導ページ上でも黒塗り「集団」表示にする
        // （集団授業の開始・終了はコマに縛られない自由入力のため、コマの時間帯と重なるかで判定する）。
        var groupLessonsByDate = report.GroupLessonAttendances.Where(x => x.Student == student)
            .GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.Select(x => (x.StartTime, x.EndTime)).ToArray());

        var row = 7;
        foreach (var week in weeks)
        {
            if (week.FullyClosed)
            {
                sheet.Range(row, 1, row, 9).Merge();
                var closedCell = sheet.Cell(row, 1);
                closedCell.Value = $"{week.Days[0].Date.Month}/{week.Days[0].Date.Day} ~ {week.Days[6].Date.Month}/{week.Days[6].Date.Day}　休校日";
                closedCell.Style.Fill.BackgroundColor = HandoutClosedFill;
                sheet.Range(row, 1, row, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(row, 1, row, 9).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                row++; continue;
            }
            var monthRow = row; var weekdayRow = row + 1; var dayRow = row + 2;
            // 月・曜日・日の見出し3行のうちA~B列は内容が無い空白になるため、指定通りグレー(#BFBFBF)で
            // 塗りつぶした上で結合する（週ごとに繰り返す。checkpointの指示にある「7~9行目」はこの
            // 週単位の見出しブロックの一例であり、他の週の同じ位置にも同じ処理を適用する）。
            sheet.Range(monthRow, 1, dayRow, 2).Merge();
            sheet.Range(monthRow, 1, dayRow, 2).Style.Fill.BackgroundColor = headerBlankFill;
            var col = 3;
            foreach (var monthGroup in week.Days.GroupBy(d => d.Date.Month))
            {
                var span = monthGroup.Count();
                var monthCell = sheet.Cell(monthRow, col);
                monthCell.Value = $"{monthGroup.Key}月"; monthCell.Style.Font.Bold = true; monthCell.Style.Font.FontColor = XLColor.White; monthCell.Style.Font.FontSize = settings.MonthFontSize; monthCell.Style.Fill.BackgroundColor = monthFill;
                if (span > 1) sheet.Range(monthRow, col, monthRow, col + span - 1).Merge();
                col += span;
            }
            for (var i = 0; i < 7; i++)
            {
                var weekdayCell = sheet.Cell(weekdayRow, 3 + i); weekdayCell.Value = WeeklyCalendarLayout.WeekdayHeaders[i]; weekdayCell.Style.Font.FontSize = settings.HeaderFontSize; weekdayCell.Style.Fill.BackgroundColor = HandoutWeekdayFill;
                var dayCell = sheet.Cell(dayRow, 3 + i); dayCell.Value = week.Days[i].Date.Day.ToString(); dayCell.Style.Font.FontSize = settings.HeaderFontSize; dayCell.Style.Fill.BackgroundColor = dayFill;
            }
            row = dayRow + 1;
            var slotBlockStartRow = row;
            foreach (var slotRow in week.SlotRows)
            {
                var slotDefinition = slotDefinitionsByLabel[slotRow.SlotLabel];
                var slotStart = TimeOnly.ParseExact(slotDefinition.StartTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var slotEnd = TimeOnly.ParseExact(slotDefinition.EndTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                sheet.Cell(row, 1).Value = $"{slotDefinition.Code}タイム"; sheet.Cell(row, 2).Value = slotDefinition.TimeRangeText;
                for (var i = 0; i < 7; i++)
                {
                    if (week.Days[i].Kind != HandoutDayKind.Open) continue;
                    var lessonCell = sheet.Cell(row, 3 + i);
                    var hasGroupLesson = groupLessonsByDate.TryGetValue(week.Days[i].Date, out var sessions) && sessions.Any(s => s.StartTime < slotEnd && slotStart < s.EndTime);
                    if (hasGroupLesson)
                    {
                        lessonCell.Value = "集団";
                        lessonCell.Style.Fill.BackgroundColor = XLColor.Black;
                        lessonCell.Style.Font.FontColor = XLColor.White;
                    }
                    else lessonCell.Value = slotRow.LessonTextByDay[i] ?? "";
                    lessonCell.Style.Font.FontSize = settings.HeaderFontSize;
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
            // カレンダー部分は隙間なく格子（全セル罫線）にする。
            var weekBlock = sheet.Range(monthRow, 1, row - 1, 9);
            weekBlock.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            weekBlock.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        row++;
        sheet.Range(row, 1, row, 4).Merge(); sheet.Cell(row, 1).Value = $"学力テスト　　{grade}　　日時：";
        sheet.Range(row, 5, row, 9).Merge(); sheet.Cell(row, 5).Value = "受験する・受験しない";
        var academicTestRange = sheet.Range(row, 1, row, 9);
        academicTestRange.Style.Fill.BackgroundColor = academicTestFill;
        academicTestRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        academicTestRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
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

    /// <summary>
    /// 生徒配布・講師配布xlsxの見た目をテキストで確認・編集できる「デザイン設定」シートを
    /// 書き出す。ここに列挙したラベル文字列は<see cref="TryReadHandoutStyleSettings"/>が読み戻す際の
    /// キーと完全一致させる必要があるため、<see cref="StyleFields"/>を単一の定義元として両者が共有する。
    /// 校舎側がこのシートのB列を編集して保存すると、次回以降の出力（同じ出力先フォルダの直前の
    /// 生徒配布用生徒別時間割.xlsx）から読み込まれ反映される。値が壊れている・シート自体が無い等の
    /// 場合は既定値にフォールバックし、出力自体は止めない。
    /// </summary>
    private static readonly (string Label, Func<HandoutStyleSettings, string> Get, Func<HandoutStyleSettings, string, HandoutStyleSettings> With, string Hint, bool IsColor)[] StyleFields =
    [
        ("タイトル文字のフォント名", s => s.TitleFontName, (s, v) => string.IsNullOrWhiteSpace(v) ? s : s with { TitleFontName = v.Trim() }, "例: BIZ UDPMincho Medium", false),
        ("タイトル文字のサイズ", s => FormatNumber(s.TitleFontSize), (s, v) => s with { TitleFontSize = ParseSizeOrDefault(v, s.TitleFontSize) }, "pt", false),
        ("タイトル背景色", s => s.TitleFillHex, (s, v) => IsValidHexColor(v) ? s with { TitleFillHex = v.Trim() } : s, "#RRGGBB形式", true),
        ("タイトル文字色", s => s.TitleFontColorHex, (s, v) => IsValidHexColor(v) ? s with { TitleFontColorHex = v.Trim() } : s, "#RRGGBB形式", true),
        ("本文フォント名", s => s.BodyFontName, (s, v) => string.IsNullOrWhiteSpace(v) ? s : s with { BodyFontName = v.Trim() }, "例: HG丸ゴシックM-PRO", false),
        ("氏名の文字サイズ", s => FormatNumber(s.NameFontSize), (s, v) => s with { NameFontSize = ParseSizeOrDefault(v, s.NameFontSize) }, "pt", false),
        ("曜日・日付・教科名等の文字サイズ", s => FormatNumber(s.HeaderFontSize), (s, v) => s with { HeaderFontSize = ParseSizeOrDefault(v, s.HeaderFontSize) }, "pt", false),
        ("月表示の文字サイズ", s => FormatNumber(s.MonthFontSize), (s, v) => s with { MonthFontSize = ParseSizeOrDefault(v, s.MonthFontSize) }, "pt", false),
        ("月の背景色", s => s.MonthFillHex, (s, v) => IsValidHexColor(v) ? s with { MonthFillHex = v.Trim() } : s, "#RRGGBB形式", true),
        ("日の背景色", s => s.DayFillHex, (s, v) => IsValidHexColor(v) ? s with { DayFillHex = v.Trim() } : s, "#RRGGBB形式", true),
        ("見出し余白の背景色", s => s.HeaderBlankFillHex, (s, v) => IsValidHexColor(v) ? s with { HeaderBlankFillHex = v.Trim() } : s, "#RRGGBB形式", true),
        ("学力テスト行の背景色", s => s.AcademicTestFillHex, (s, v) => IsValidHexColor(v) ? s with { AcademicTestFillHex = v.Trim() } : s, "#RRGGBB形式", true),
    ];

    private static void WriteHandoutStyleTemplateSheet(IXLWorksheet sheet, HandoutStyleSettings settings)
    {
        sheet.Column(1).Width = 32; sheet.Column(2).Width = 28; sheet.Column(3).Width = 40;
        sheet.Range(1, 1, 1, 3).Merge();
        sheet.Cell(1, 1).Value = "デザイン設定";
        sheet.Cell(1, 1).Style.Font.Bold = true; sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Range(2, 1, 2, 3).Merge();
        sheet.Cell(2, 1).Value = "B列の値を書き換えて保存すると、この出力フォルダより後に生成する生徒配布・講師配布xlsxへ反映されます（色は#RRGGBB形式で入力してください）。";
        sheet.Cell(2, 1).Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 30;

        var row = 4;
        sheet.Cell(row, 1).Value = "項目"; sheet.Cell(row, 2).Value = "値"; sheet.Cell(row, 3).Value = "備考";
        sheet.Row(row).Style.Font.Bold = true; row++;
        foreach (var field in StyleFields)
        {
            sheet.Cell(row, 1).Value = field.Label;
            var valueCell = sheet.Cell(row, 2); valueCell.Value = field.Get(settings);
            if (field.IsColor) valueCell.Style.Fill.BackgroundColor = ParseColorOrDefault(field.Get(settings), XLColor.White);
            sheet.Cell(row, 3).Value = field.Hint;
            row++;
        }
        sheet.Columns(1, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Columns(1, 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>
    /// 直前の出力フォルダ内のxlsxから「デザイン設定」シートを読み戻す。シートが無い・壊れている等の
    /// 場合はnullを返し、呼び出し側は既定値（<see cref="HandoutStyleSettings.Default"/>）を使う。
    /// </summary>
    public static HandoutStyleSettings? TryReadHandoutStyleSettings(string xlsxPath)
    {
        try
        {
            if (!File.Exists(xlsxPath)) return null;
            using var workbook = new XLWorkbook(xlsxPath);
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == HandoutStyleSheetName);
            if (sheet is null) return null;

            var byLabel = new Dictionary<string, string>(StringComparer.Ordinal);
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var r = 1; r <= lastRow; r++)
            {
                var label = sheet.Cell(r, 1).GetString().Trim();
                if (label.Length == 0) continue;
                byLabel[label] = sheet.Cell(r, 2).GetString();
            }

            var settings = HandoutStyleSettings.Default;
            foreach (var field in StyleFields)
                if (byLabel.TryGetValue(field.Label, out var value))
                    settings = field.With(settings, value);
            return settings;
        }
        catch
        {
            // 校舎側の編集内容が壊れていても出力自体は止めない。既定値へフォールバックする。
            return null;
        }
    }

    private static string FormatNumber(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static double ParseSizeOrDefault(string text, double fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value is > 0 and <= 200 ? value : fallback;

    private static bool IsValidHexColor(string text) =>
        !string.IsNullOrWhiteSpace(text) && System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^#[0-9A-Fa-f]{6}$");

    private static XLColor ParseColorOrDefault(string hex, XLColor fallback)
    {
        try { return IsValidHexColor(hex) ? XLColor.FromHtml(hex.Trim()) : fallback; }
        catch { return fallback; }
    }

    private static void WriteOverviewMetadataSheet(IXLWorksheet sheet, ScheduleReport report)
    {
        sheet.Column(1).Width = 24; sheet.Column(2).Width = 54; sheet.Column(1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Value = "帳票名"; sheet.Cell(1, 2).Value = "全体時間割";
        sheet.Cell(2, 1).Value = "校舎・講習"; sheet.Cell(2, 2).Value = report.ProjectTitle;
        sheet.Cell(3, 1).Value = "更新日時"; sheet.Cell(3, 2).Value = report.GeneratedAtText;
    }

    private static readonly XLColor OverviewSubtitleFill = XLColor.FromHtml("#EAF0F6");
    private static readonly XLColor OverviewHeaderFill = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor OverviewUnavailableFill = XLColor.FromHtml("#D9D9D9");
    private static readonly XLColor OverviewFootnoteFill = XLColor.FromHtml("#F0F2F5");
    private static readonly XLColor OverviewOneToOneFill = XLColor.FromHtml("#FFF1CC");
    private static readonly XLColor OverviewLockedFill = XLColor.FromHtml("#DCEBFF");
    private static readonly XLColor OverviewManualFill = XLColor.FromHtml("#EADFFF");
    private const string OverviewLegendText = "凡例　灰色: 勤務不可コマ　[1対1] 1対1　[固定] ロック　[手] 手動変更";
    private const string OverviewFootnoteText = "日曜始まり・土曜終わりの週単位です。出勤予定の講師のみ表示します。";

    /// <summary>Python版のstyle_rules優先順位（warning > closed > group > one_to_one > locked > manual）
    /// のうち、本移植版が実際に持つ属性（1対1／ロック／手動）だけを同じ優先順で適用する。</summary>
    private static XLColor? OverviewCardFill(OverviewCard card) =>
        card.OneToOneRequired ? OverviewOneToOneFill : card.IsLocked ? OverviewLockedFill : card.IsManual ? OverviewManualFill : null;

    /// <summary>
    /// Python版timetable_builder.pyは日付panelごとに専用の「コマ」ラベル列を持つ構成だったが、
    /// ユーザー指示により「コマ・時刻のラベルは週の先頭（A列）だけに1回だけ置く」という簡略化された
    /// 独自レイアウトへ変更した（Python parityより指示を優先。意図的な差分）。出勤講師は2列（同時
    /// 最大2名）で日付ごとに横へ並び、コマごと3行（学年／科目略称／生徒名縦書き）で表示する点は従来通り。
    /// 該当日・出勤予定講師が1件も無い週もsheet自体は生成し、プレースホルダーを表示する。
    /// </summary>
    private static void WriteOverviewWeekSheet(IXLWorksheet sheet, OverviewWeek week, IReadOnlyList<string> slotLabels, IReadOnlyDictionary<string, SlotDefinition> slotDefinitionsByLabel)
    {
        const int dateHeaderRow = 2, comaHeaderRow = 3, slotStartRow = 4;
        var sundayEnd = week.SundayStart.AddDays(6);
        sheet.Cell(1, 1).Value = $"{week.SundayStart:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[0]}） ～ {sundayEnd:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[6]}）";
        sheet.Cell(1, 1).Style.Fill.BackgroundColor = OverviewSubtitleFill;

        // A列＝週で共有する「コマ」ラベル列。中央ぞろえ（水平・垂直とも）で1回だけ書く。
        var comaCell = sheet.Cell(comaHeaderRow, 1); comaCell.Value = "コマ"; comaCell.Style.Font.Bold = true; comaCell.Style.Fill.BackgroundColor = OverviewSubtitleFill;
        comaCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; comaCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        for (var s = 0; s < slotLabels.Count; s++)
        {
            var rowBase = slotStartRow + s * 3;
            var labelCell = sheet.Cell(rowBase, 1);
            labelCell.Value = slotDefinitionsByLabel[slotLabels[s]].OverviewLabelText;
            labelCell.Style.Font.Bold = true; labelCell.Style.Alignment.WrapText = true;
            labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            labelCell.Style.Fill.BackgroundColor = OverviewSubtitleFill;
            sheet.Range(rowBase, 1, rowBase + 2, 1).Merge();
        }

        int lastCol; int legendRow;
        if (week.Days.Count == 0)
        {
            sheet.Cell(dateHeaderRow, 2).Value = "対象となる開校日・出勤予定講師がありません"; sheet.Cell(dateHeaderRow, 2).Style.Fill.BackgroundColor = OverviewSubtitleFill;
            lastCol = 2; legendRow = dateHeaderRow + 1;
        }
        else
        {
            var col = 2;
            foreach (var day in week.Days)
            {
                var dayStartCol = col;

                if (day.Teachers.Count == 0)
                {
                    var noneCell = sheet.Cell(comaHeaderRow, dayStartCol); noneCell.Value = "出勤予定なし"; noneCell.Style.Fill.BackgroundColor = OverviewUnavailableFill;
                    sheet.Range(comaHeaderRow, dayStartCol, comaHeaderRow, dayStartCol + 1).Merge();
                    if (slotLabels.Count > 0) sheet.Range(slotStartRow, dayStartCol, slotStartRow + slotLabels.Count * 3 - 1, dayStartCol + 1).Style.Fill.BackgroundColor = OverviewUnavailableFill;
                    col += 2;
                }
                else
                {
                    var teacherCol = dayStartCol;
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
                                    if (OverviewCardFill(card) is { } cardFill) sheet.Range(rowBase, cardCol, rowBase + 2, cardCol).Style.Fill.BackgroundColor = cardFill;
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

                if (col - dayStartCol > 1) sheet.Range(dateHeaderRow, dayStartCol, dateHeaderRow, col - 1).Merge();
                var dateCell = sheet.Cell(dateHeaderRow, dayStartCol);
                dateCell.Value = $"{day.Date:yyyy/M/d}（{WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]}）";
                dateCell.Style.Font.Bold = true; dateCell.Style.Font.FontColor = XLColor.White; dateCell.Style.Fill.BackgroundColor = OverviewHeaderFill; dateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            lastCol = Math.Max(2, col - 1);
            legendRow = slotStartRow + slotLabels.Count * 3;
        }

        var legendCell = sheet.Cell(legendRow, 1); legendCell.Value = OverviewLegendText; legendCell.Style.Fill.BackgroundColor = OverviewFootnoteFill;
        var footnoteCell = sheet.Cell(legendRow + 1, 1); footnoteCell.Value = OverviewFootnoteText; footnoteCell.Style.Fill.BackgroundColor = OverviewFootnoteFill;
        sheet.Range(1, 1, 1, lastCol).Merge();
        sheet.Range(legendRow, 1, legendRow, lastCol).Merge(); sheet.Range(legendRow + 1, 1, legendRow + 1, lastCol).Merge();
        // ユーザー指示: A列（コマ・時刻ラベル）=45px、B列以降（出勤講師の列）は一律30px。
        sheet.Column(1).Width = PixelsToColumnWidth(45);
        if (lastCol > 1) sheet.Columns(2, lastCol).Width = PixelsToColumnWidth(30);
        sheet.SheetView.FreezeRows(2);
    }

    // ExcelのColumn.Width単位（既定Calibri 11pt基準のcharacter幅）へ、指定ピクセル数を変換する。
    // OOXMLの標準変換式: width = (pixels - 5) / MaximumDigitWidth（既定フォントは7px）。
    private static double PixelsToColumnWidth(double pixels) => Math.Round((pixels - 5) / 7.0, 2);

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
