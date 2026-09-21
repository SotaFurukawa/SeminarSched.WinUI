using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SeminarSched.Domain.Output;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

/// <summary>
/// ユーザー指示により、PDF版はExcelScheduleReportRendererと同じ見た目（フォント・色・構成）を目指す。
/// タイトルフォントのみExcel側は「BIZ UDPMincho Medium」を使うが、そのフォントファイルはWindows上で
/// TrueType Collection(.ttc)として提供されておりPDFsharp 6.2.4が直接読み込めない（実機で検証済み。
/// 生のバイト列を渡すとOpenTypeFontFace.CetOrCreateFromでNullReferenceExceptionになる）ため、PDFでは
/// 本文と同じ「HG丸ゴシックM-PRO」を太字・大きめサイズで代用する。列幅・文字サイズは用紙内に収まる
/// 範囲で、7曜日列の幅を揃えたうえでできるだけ大きくする方針で決め打ちしている。
/// </summary>
public sealed class PdfScheduleReportRenderer
{
    private static readonly object FontGate = new();

    private static readonly Color HandoutTitleFillDefault = ParseColor("#000000");
    private static readonly Color HandoutMonthFill = ParseColor("#0F243E");
    private static readonly Color HandoutDayFill = ParseColor("#90CAFE");
    private static readonly Color HandoutWeekdayFill = ParseColor("#F2F2F2");
    private static readonly Color HandoutHeaderBlankFill = ParseColor("#BFBFBF");
    private static readonly Color HandoutAcademicTestFill = ParseColor("#95B3D7");
    private static readonly Color HandoutOutOfRangeFill = ParseColor("#0E2841");
    private static readonly Color OverviewSubtitleFill = ParseColor("#EAF0F6");
    private static readonly Color OverviewHeaderFill = ParseColor("#1F4E78");
    private static readonly Color OverviewFootnoteFill = ParseColor("#F0F2F5");
    private static readonly Color OverviewOneToOneFill = ParseColor("#FFF1CC");
    private static readonly Color OverviewLockedFill = ParseColor("#DCEBFF");
    private static readonly Color OverviewManualFill = ParseColor("#EADFFF");

    /// <summary>Excel版OverviewCardFillと同じ優先順位（1対1＞ロック＞手動）。</summary>
    private static Color? OverviewCardFill(OverviewCard card) =>
        card.OneToOneRequired ? OverviewOneToOneFill : card.IsLocked ? OverviewLockedFill : card.IsManual ? OverviewManualFill : null;

    public void RenderOverall(ScheduleReport report, string path, OutputSettings? outputSettings = null)
    {
        var settings = outputSettings ?? OutputSettings.Default;
        EnsureFont(); var document = NewDocument("全体時間割", settings);
        var section = document.AddSection(); ApplyPageSetup(section, settings);
        AddFullWidthBar(section, settings, $"{report.ProjectTitle}／{report.GeneratedAtText}", OverviewSubtitleFill, Colors.Black, 9, bold: false);

        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher).Concat(report.TeacherUnavailabilities.Select(u => u.Teacher)));
        var studentLabels = WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x => x.Student));
        var overviewAssignments = report.Rows.Select(r => new OverviewAssignment(DateOnly.Parse(r.Date), teacherLabels[r.Teacher], r.TimeSlot, r.StudentGrade, r.SubjectShortName, studentLabels[r.Student], r.OneToOneRequired, r.IsLocked, r.IsManual)).ToArray();
        var overviewUnavailabilities = report.TeacherUnavailabilities.Select(u => new OverviewUnavailability(DateOnly.Parse(u.Date), teacherLabels[u.Teacher], u.TimeSlot)).ToArray();
        var grid = OverviewGridLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, overviewAssignments, overviewUnavailabilities);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);
        AddOverview(document, grid, slotDefinitionsByLabel, settings);

        Save(document, path);
    }

    public void RenderStudentHandouts(ScheduleReport report, string path, OutputSettings? outputSettings = null) => RenderHandouts(report, path, includeTeacher: false, "生徒配布用生徒別時間割", outputSettings);

    public void RenderTeacherHandouts(ScheduleReport report, string path, OutputSettings? outputSettings = null) => RenderHandouts(report, path, includeTeacher: true, "講師配布用学年別時間割", outputSettings);

    public void RenderTeacherPacket(ScheduleReport report, string teacherName, string path, OutputSettings? outputSettings = null)
    {
        var settings = outputSettings ?? OutputSettings.Default;
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        EnsureFont(); var document = NewDocument(teacherLabels[teacherName], settings);
        AddAbsenceSection(document, report, settings);
        var ordered = OrderStudentsForTeacher(report, teacherName);
        foreach (var s in ordered) AddStudentCalendarSection(document, report, s.Student, s.Grade, includeTeacher: true, teacherLabels, settings);
        Save(document, path);
    }

    /// <summary>ExcelScheduleReportRenderer.RenderTeacherPacketsCombinedのPDF版。1講師=1セクション
    /// （=1ページ）という前提で、担当生徒数が奇数の講師の後ろに空白ページを1枚挟む（印刷時に2ページずつ
    /// まとめる運用のため、次の講師が必ず奇数ページ目から始まるようにする）。各ページ冒頭に
    /// 「{講師名}t用」を付ける。</summary>
    public void RenderTeacherPacketsCombined(ScheduleReport report, IReadOnlyList<string> teacherNames, string path, OutputSettings? outputSettings = null)
    {
        var settings = outputSettings ?? OutputSettings.Default;
        EnsureFont(); var document = NewDocument("講師配布用講師別時間割(一括)", settings);
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        AddAbsenceSection(document, report, settings);
        foreach (var teacherName in teacherNames.OrderBy(x => x, StringComparer.Ordinal))
        {
            var teacherLabel = teacherLabels[teacherName];
            var ordered = OrderStudentsForTeacher(report, teacherName);
            foreach (var s in ordered) AddStudentCalendarSection(document, report, s.Student, s.Grade, includeTeacher: true, teacherLabels, settings, teacherLabelPrefix: $"{teacherLabel}t用");
            if (ordered.Count % 2 != 0) { var blank = document.AddSection(); ApplyPageSetup(blank, settings); }
        }
        Save(document, path);
    }

    private static IReadOnlyList<(string Student, string Grade)> OrderStudentsForTeacher(ScheduleReport report, string teacherName)
    {
        var allStudents = report.Rows.GroupBy(x => new { x.Student, x.StudentGrade }).OrderBy(x => GradeOrdering.SortKey(x.Key.StudentGrade)).ThenBy(x => x.Key.Student, StringComparer.Ordinal).Select(g => (g.Key.Student, Grade: g.Key.StudentGrade)).ToArray();
        var regularIds = report.Rows.Where(r => r.Teacher == teacherName && r.IsRegularTeacher).Select(r => r.Student).ToHashSet();
        var seasonalIds = report.Rows.Where(r => r.Teacher == teacherName).Select(r => r.Student).ToHashSet();
        return allStudents.Where(s => regularIds.Contains(s.Student))
            .Concat(allStudents.Where(s => seasonalIds.Contains(s.Student) && !regularIds.Contains(s.Student)))
            .Concat(allStudents.Where(s => !regularIds.Contains(s.Student) && !seasonalIds.Contains(s.Student)))
            .ToArray();
    }

    public void RenderIssues(ScheduleReport report, string path, OutputSettings? outputSettings = null)
    {
        var settings = outputSettings ?? OutputSettings.Default;
        EnsureFont(); var document = NewDocument("未配置・警告一覧", settings);

        var section = document.AddSection(); ApplyPageSetup(section, settings);
        section.AddParagraph("未配置一覧").Format.Font.Size = 15;
        string[] unassignedHeaders = ["生徒", "科目", "必要", "配置済", "不足", "主な理由", "解決候補", "優先度", "通常担当", "1対1", "備考"];
        double[] unassignedWidths = [2.4, 1.8, 1.0, 1.0, 1.0, 3.2, 3.6, 1.1, 2.2, 1.2, 2.6];
        var unassignedTable = section.AddTable(); unassignedTable.Borders.Width = .5; foreach (var w in unassignedWidths) unassignedTable.AddColumn(Unit.FromCentimeter(w));
        var unassignedHeaderRow = unassignedTable.AddRow(); unassignedHeaderRow.Shading.Color = Colors.LightGray;
        for (var i = 0; i < unassignedHeaders.Length; i++) unassignedHeaderRow.Cells[i].AddParagraph(unassignedHeaders[i]).Format.Font.Bold = true;
        foreach (var r in report.UnassignedRequests)
        {
            var row = unassignedTable.AddRow();
            row.Cells[0].AddParagraph(r.Student); row.Cells[1].AddParagraph(r.Subject);
            row.Cells[2].AddParagraph(r.Required.ToString()); row.Cells[3].AddParagraph(r.Placed.ToString()); row.Cells[4].AddParagraph(r.Missing.ToString());
            row.Cells[5].AddParagraph(r.MainReason); row.Cells[6].AddParagraph(r.ResolutionCandidates.Count > 0 ? string.Join("／", r.ResolutionCandidates) : "候補なし");
            row.Cells[7].AddParagraph(r.Priority.ToString()); row.Cells[8].AddParagraph(r.RegularTeacher ?? "未設定");
            row.Cells[9].AddParagraph(r.OneToOneRequired ? "必須" : "通常"); row.Cells[10].AddParagraph(r.Note);
        }

        var section2 = document.AddSection(); ApplyPageSetup(section2, settings);
        section2.AddParagraph("警告一覧").Format.Font.Size = 15;
        string[] warningHeaders = ["severity", "issue type", "日付", "コマ", "生徒", "講師", "内容", "対応状況"];
        double[] warningWidths = [1.6, 2.6, 2.2, 1.3, 2.3, 2.3, 6.0, 1.8];
        var warningTable = section2.AddTable(); warningTable.Borders.Width = .5; foreach (var w in warningWidths) warningTable.AddColumn(Unit.FromCentimeter(w));
        var warningHeaderRow = warningTable.AddRow(); warningHeaderRow.Shading.Color = Colors.LightGray;
        for (var i = 0; i < warningHeaders.Length; i++) warningHeaderRow.Cells[i].AddParagraph(warningHeaders[i]).Format.Font.Bold = true;
        foreach (var w in report.Warnings)
        {
            var row = warningTable.AddRow();
            row.Cells[0].AddParagraph(w.Severity); row.Cells[1].AddParagraph(w.IssueType);
            row.Cells[2].AddParagraph(w.Date ?? "—"); row.Cells[3].AddParagraph(w.Slot ?? "—");
            row.Cells[4].AddParagraph(w.Student); row.Cells[5].AddParagraph(w.Teacher);
            row.Cells[6].AddParagraph(w.Content); row.Cells[7].AddParagraph(w.Status);
        }

        Save(document, path);
    }

    private static void RenderHandouts(ScheduleReport report, string path, bool includeTeacher, string title, OutputSettings? outputSettings)
    {
        var settings = outputSettings ?? OutputSettings.Default;
        EnsureFont(); var document = NewDocument(title, settings);
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        AddAbsenceSection(document, report, settings);
        foreach (var group in report.Rows.GroupBy(x => new { x.Student, x.StudentGrade }).OrderBy(x => GradeOrdering.SortKey(x.Key.StudentGrade)).ThenBy(x => x.Key.Student, StringComparer.Ordinal))
            AddStudentCalendarSection(document, report, group.Key.Student, group.Key.StudentGrade, includeTeacher, teacherLabels, settings);
        Save(document, path);
    }

    private static void AddAbsenceSection(Document document, ScheduleReport report, OutputSettings settings)
    {
        if (report.AbsentStudents.Count == 0) return;
        var section = document.AddSection(); ApplyPageSetup(section, settings);
        section.AddParagraph("講習欠席一覧").Format.Font.Bold = true;
        var table = section.AddTable(); table.Borders.Width = .5; table.AddColumn(Unit.FromCentimeter(2.5)); table.AddColumn(Unit.FromCentimeter(5));
        foreach (var student in report.AbsentStudents.OrderBy(s => GradeOrdering.SortKey(s.Grade)).ThenBy(s => s.Name, StringComparer.Ordinal)) { var row = table.AddRow(); row.Cells[0].AddParagraph(student.Grade); row.Cells[1].AddParagraph(student.Name); }
    }

    /// <summary>
    /// ExcelScheduleReportRenderer.WriteStudentHandoutPageのPDF版。同じHandoutPageLayoutを使って
    /// 月・曜日・日付・コマの週blockを積み上げ、集団授業と重なる時間帯は黒塗り白文字「集団」にする
    /// （checkpoint81のExcel版と同じ判定ロジック）。
    /// </summary>
    private static void AddStudentCalendarSection(Document document, ScheduleReport report, string student, string grade, bool includeTeacher, IReadOnlyDictionary<string, string> teacherLabels, OutputSettings settings, string? teacherLabelPrefix = null)
    {
        var page = document.AddSection(); ApplyPageSetup(page, settings);
        var usableWidth = GetUsableWidth(settings);

        if (teacherLabelPrefix is not null) { var labelPara = page.AddParagraph(teacherLabelPrefix); labelPara.Format.Font.Bold = true; labelPara.Format.Font.Size = 9; }

        var titleTable = page.AddTable(); titleTable.AddColumn(usableWidth);
        var titleRow = titleTable.AddRow(); titleRow.Height = Unit.FromCentimeter(1.4); titleRow.VerticalAlignment = VerticalAlignment.Center; titleRow.Shading.Color = HandoutTitleFillDefault;
        var titlePara = titleRow.Cells[0].AddParagraph($"{report.AcademicYear}　{report.SeasonName}　個別指導　受講日のご案内");
        titlePara.Format.Font.Bold = true; titlePara.Format.Font.Size = 24; titlePara.Format.Font.Color = Colors.White; titlePara.Format.Alignment = ParagraphAlignment.Center;

        var (schoolLevel, gradeNumber) = ParseGrade(grade);
        var namePara = page.AddParagraph();
        namePara.Format.SpaceBefore = Unit.FromCentimeter(0.25); namePara.Format.SpaceAfter = Unit.FromCentimeter(0.15);
        namePara.Format.Borders.Bottom.Width = 1;
        var levelRun = namePara.AddFormattedText($"{schoolLevel}{gradeNumber}年生　　"); levelRun.Font.Size = 13;
        var nameRun = namePara.AddFormattedText(student); nameRun.Font.Size = 20; nameRun.Font.Bold = true;
        var suffixRun = namePara.AddFormattedText("　様"); suffixRun.Font.Size = 13;

        var rows = report.Rows.Where(x => x.Student == student).ToArray();
        var lessonTextByDateSlot = rows.ToDictionary(x => (DateOnly.Parse(x.Date), x.TimeSlot), string (x) => includeTeacher ? $"{x.SubjectShortName}　{teacherLabels[x.Teacher]}" : x.SubjectShortName);
        var weeks = HandoutPageLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, lessonTextByDateSlot);
        var slotDefinitionsByLabel = report.SlotDefinitions.ToDictionary(s => s.Label);
        var groupLessonsByDate = report.GroupLessonAttendances.Where(x => x.Student == student)
            .GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.Select(x => (x.StartTime, x.EndTime)).ToArray());

        var closedFill = ParseColorOrDefault(settings.ClosedFillHex, ParseColor("#E8E8E8"));
        var groupFill = ParseColorOrDefault(settings.GroupFillHex, HandoutTitleFillDefault);

        // Excel版のA=54px,B=96px,C~I(7日)=64pxと同じ比率を、実際の用紙幅へ配分する（7曜日列の幅は必ず揃える）。
        const double comaWeight = 54, timeWeight = 96, dayWeight = 64; const double totalWeight = comaWeight + timeWeight + dayWeight * 7;
        var comaColWidth = usableWidth * (comaWeight / totalWeight); var timeColWidth = usableWidth * (timeWeight / totalWeight); var dayColWidth = usableWidth * (dayWeight / totalWeight);

        foreach (var week in weeks)
        {
            var table = page.AddTable(); table.Borders.Width = .5;
            table.AddColumn(comaColWidth); table.AddColumn(timeColWidth);
            for (var i = 0; i < 7; i++) table.AddColumn(dayColWidth);

            if (week.FullyClosed)
            {
                var row = table.AddRow(); row.Cells[0].MergeRight = 8; row.Shading.Color = closedFill;
                var text = row.Cells[0].AddParagraph($"{week.Days[0].Date.Month}/{week.Days[0].Date.Day} ~ {week.Days[6].Date.Month}/{week.Days[6].Date.Day}　休校日");
                text.Format.Font.Size = 11; text.Format.Alignment = ParagraphAlignment.Center;
                continue;
            }

            var monthRow = table.AddRow(); var weekdayRow = table.AddRow(); var dayRow = table.AddRow();
            monthRow.Cells[0].MergeDown = 2; monthRow.Cells[0].MergeRight = 1; monthRow.Cells[0].Shading.Color = HandoutHeaderBlankFill;

            var col = 2;
            foreach (var monthGroup in week.Days.GroupBy(d => d.Date.Month))
            {
                var span = monthGroup.Count();
                var cell = monthRow.Cells[col]; if (span > 1) cell.MergeRight = span - 1;
                var monthPara = cell.AddParagraph($"{monthGroup.Key}月"); monthPara.Format.Font.Bold = true; monthPara.Format.Font.Size = 12; monthPara.Format.Font.Color = Colors.White; monthPara.Format.Alignment = ParagraphAlignment.Center;
                cell.Shading.Color = HandoutMonthFill; cell.VerticalAlignment = VerticalAlignment.Center;
                col += span;
            }
            for (var i = 0; i < 7; i++)
            {
                var weekdayCell = weekdayRow.Cells[2 + i]; var weekdayPara = weekdayCell.AddParagraph(WeeklyCalendarLayout.WeekdayHeaders[i]);
                weekdayPara.Format.Font.Size = 10; weekdayPara.Format.Alignment = ParagraphAlignment.Center; weekdayCell.Shading.Color = HandoutWeekdayFill;
                var dayCell = dayRow.Cells[2 + i]; var dayPara = dayCell.AddParagraph(week.Days[i].Date.Day.ToString());
                dayPara.Format.Font.Size = 10; dayPara.Format.Alignment = ParagraphAlignment.Center; dayCell.Shading.Color = HandoutDayFill;
            }

            foreach (var slotRow in week.SlotRows)
            {
                var slotDefinition = slotDefinitionsByLabel[slotRow.SlotLabel];
                var slotStart = TimeOnly.ParseExact(slotDefinition.StartTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var slotEnd = TimeOnly.ParseExact(slotDefinition.EndTimeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var row = table.AddRow(); row.VerticalAlignment = VerticalAlignment.Center;
                var codeCell = row.Cells[0]; codeCell.AddParagraph($"{slotDefinition.Code}タイム").Format.Font.Size = 10; codeCell.Format.Alignment = ParagraphAlignment.Center;
                var timeCell = row.Cells[1]; timeCell.AddParagraph(slotDefinition.TimeRangeText).Format.Font.Size = 10; timeCell.Format.Alignment = ParagraphAlignment.Center;
                for (var i = 0; i < 7; i++)
                {
                    var cell = row.Cells[2 + i];
                    if (week.Days[i].Kind != HandoutDayKind.Open)
                    {
                        cell.Shading.Color = week.Days[i].Kind == HandoutDayKind.OutOfRange ? HandoutOutOfRangeFill : closedFill;
                        continue;
                    }
                    var hasGroupLesson = groupLessonsByDate.TryGetValue(week.Days[i].Date, out var sessions) && sessions.Any(s => s.StartTime < slotEnd && slotStart < s.EndTime);
                    var textPara = cell.AddParagraph(hasGroupLesson ? "集団" : slotRow.LessonTextByDay[i] ?? "");
                    textPara.Format.Font.Size = 10; textPara.Format.Alignment = ParagraphAlignment.Center;
                    if (hasGroupLesson) { cell.Shading.Color = groupFill; textPara.Format.Font.Color = Colors.White; }
                }
            }
        }

        var academicTestTable = page.AddTable(); academicTestTable.Borders.Width = .5;
        academicTestTable.AddColumn(usableWidth * 4 / 9); academicTestTable.AddColumn(usableWidth * 5 / 9);
        var academicTestRow = academicTestTable.AddRow(); academicTestRow.Shading.Color = HandoutAcademicTestFill;
        academicTestRow.Cells[0].AddParagraph($"学力テスト　　{grade}　　日時：").Format.Font.Size = 10;
        academicTestRow.Cells[1].AddParagraph("受験する・受験しない").Format.Font.Size = 10;
    }

    /// <summary>
    /// ExcelScheduleReportRenderer.WriteOverviewWeekSheetのPDF版。各コマの時刻ラベル（{コード}
    /// {開始}～{終了}）は、そのコマの3行ブロック（学年／科目略称／生徒名）の先頭列へ縦結合して1回だけ
    /// 表示する（Excel版と同じ意匠）。
    /// </summary>
    private static void AddOverview(Document document, OverviewGrid grid, IReadOnlyDictionary<string, SlotDefinition> slotDefinitionsByLabel, OutputSettings settings)
    {
        var unavailableFill = ParseColorOrDefault(settings.UnavailableFillHex, ParseColor("#D9D9D9"));
        var weeksWithDays = grid.Weeks.Where(w => w.Days.Count > 0).ToArray();
        for (var weekIndex = 0; weekIndex < weeksWithDays.Length; weekIndex++)
        {
            var week = weeksWithDays[weekIndex];
            var section = weekIndex == 0 ? document.LastSection : document.AddSection();
            ApplyPageSetup(section, settings);
            var usableWidth = GetUsableWidth(settings);

            section.AddParagraph($"{week.SundayStart:yyyy/M/d}週").Format.Font.Bold = true;
            var totalColumns = 1 + week.Days.Sum(d => Math.Max(1, d.Teachers.Count) * 2);
            var comaColWidth = usableWidth * 0.07; var otherColWidth = (usableWidth - comaColWidth) / Math.Max(1, totalColumns - 1);

            var table = section.AddTable(); table.Borders.Width = .5;
            table.AddColumn(comaColWidth); for (var i = 1; i < totalColumns; i++) table.AddColumn(otherColWidth);

            var dayHeaderRow = table.AddRow(); dayHeaderRow.Shading.Color = OverviewHeaderFill;
            var teacherHeaderRow = table.AddRow(); teacherHeaderRow.Shading.Color = OverviewSubtitleFill;
            var comaCell = teacherHeaderRow.Cells[0]; var comaPara = comaCell.AddParagraph("コマ"); comaPara.Format.Font.Bold = true; comaPara.Format.Alignment = ParagraphAlignment.Center; comaCell.VerticalAlignment = VerticalAlignment.Center;

            var col = 1; var teacherColumns = new List<(OverviewTeacherColumn Teacher, int Column)>();
            foreach (var day in week.Days)
            {
                var teacherCount = Math.Max(1, day.Teachers.Count); var startCol = col;
                var dayCell = dayHeaderRow.Cells[startCol];
                var dayPara = dayCell.AddParagraph($"{day.Date.Month}/{day.Date.Day}({WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]})");
                dayPara.Format.Font.Bold = true; dayPara.Format.Font.Color = Colors.White; dayPara.Format.Alignment = ParagraphAlignment.Center;
                if (teacherCount * 2 > 1) dayCell.MergeRight = teacherCount * 2 - 1;

                if (day.Teachers.Count == 0) { var noneCell = teacherHeaderRow.Cells[startCol]; noneCell.AddParagraph("出勤予定なし").Format.Font.Size = 9; noneCell.MergeRight = 1; noneCell.Shading.Color = unavailableFill; col += 2; continue; }
                foreach (var teacher in day.Teachers)
                {
                    var teacherCell = teacherHeaderRow.Cells[col]; teacherCell.MergeRight = 1;
                    var teacherPara = teacherCell.AddParagraph(teacher.TeacherName); teacherPara.Format.Font.Bold = true; teacherPara.Format.Font.Color = Colors.White; teacherPara.Format.Alignment = ParagraphAlignment.Center;
                    teacherCell.Shading.Color = OverviewHeaderFill;
                    teacherColumns.Add((teacher, col));
                    col += 2;
                }
            }

            for (var s = 0; s < grid.SlotLabels.Count; s++)
            {
                var gradeRow = table.AddRow(); var subjectRow = table.AddRow(); var studentRow = table.AddRow();
                gradeRow.Format.Font.Size = 8; subjectRow.Format.Font.Size = 8; studentRow.Format.Font.Size = 8;

                var labelCell = gradeRow.Cells[0]; labelCell.MergeDown = 2; labelCell.VerticalAlignment = VerticalAlignment.Center; labelCell.Shading.Color = OverviewSubtitleFill;
                var labelPara = labelCell.AddParagraph(slotDefinitionsByLabel[grid.SlotLabels[s]].OverviewLabelText.Replace("\n", " "));
                labelPara.Format.Font.Bold = true; labelPara.Format.Alignment = ParagraphAlignment.Center;

                foreach (var (teacher, teacherCol) in teacherColumns)
                {
                    var cell = teacher.Cells[s];
                    if (cell.Cards.Count > 0)
                    {
                        var card = cell.Cards[0];
                        var gradeCell = gradeRow.Cells[teacherCol]; gradeCell.MergeRight = 1; gradeCell.AddParagraph(card.Grade).Format.Alignment = ParagraphAlignment.Center;
                        var subjectCell = subjectRow.Cells[teacherCol]; subjectCell.MergeRight = 1; subjectCell.AddParagraph(card.SubjectShortName).Format.Alignment = ParagraphAlignment.Center;
                        var studentCell = studentRow.Cells[teacherCol]; studentCell.MergeRight = 1;
                        var studentPara = studentCell.AddParagraph(card.Student); studentPara.Format.Alignment = ParagraphAlignment.Center;
                        if (cell.Cards.Count > 1)
                        {
                            studentPara.AddLineBreak();
                            studentPara.AddText(string.Join("、", cell.Cards.Skip(1).Select(c => $"{c.Grade}{c.SubjectShortName}{c.Student}")));
                        }
                        if (OverviewCardFill(card) is { } cardFill) { gradeCell.Shading.Color = cardFill; subjectCell.Shading.Color = cardFill; studentCell.Shading.Color = cardFill; }
                    }
                    else if (cell.Unavailable)
                    {
                        gradeRow.Cells[teacherCol].MergeRight = 1; gradeRow.Cells[teacherCol].Shading.Color = unavailableFill;
                        subjectRow.Cells[teacherCol].MergeRight = 1; subjectRow.Cells[teacherCol].Shading.Color = unavailableFill;
                        studentRow.Cells[teacherCol].MergeRight = 1; studentRow.Cells[teacherCol].Shading.Color = unavailableFill;
                    }
                }
            }

            var legendPara = section.AddParagraph("凡例　灰色: 勤務不可コマ　[1対1] 1対1　[固定] ロック　[手] 手動変更"); legendPara.Format.Font.Size = 8; legendPara.Format.SpaceBefore = Unit.FromCentimeter(0.1);
            var footnotePara = section.AddParagraph("日曜始まり・土曜終わりの週単位です。出勤予定の講師のみ表示します。"); footnotePara.Format.Font.Size = 8;
        }
    }

    private static Table AddFullWidthBar(Section section, OutputSettings settings, string text, Color fill, Color textColor, double fontSize, bool bold)
    {
        var table = section.AddTable(); table.AddColumn(GetUsableWidth(settings));
        var row = table.AddRow(); row.Shading.Color = fill;
        var para = row.Cells[0].AddParagraph(text); para.Format.Font.Size = fontSize; para.Format.Font.Bold = bold; para.Format.Font.Color = textColor;
        return table;
    }

    private static (string Level, string Number) ParseGrade(string grade)
    {
        if (grade.Length == 0) return ("", "");
        var level = grade[0] switch { '小' => "小学", '中' => "中学", '高' => "高校", _ => "" };
        var number = new string(grade.Where(char.IsDigit).ToArray());
        return (level, number);
    }

    private static void ApplyPageSetup(Section section, OutputSettings settings)
    {
        section.PageSetup.Orientation = settings.Orientation == OutputSettings.OrientationPortrait ? Orientation.Portrait : Orientation.Landscape;
        section.PageSetup.PageFormat = settings.PaperSize == OutputSettings.PaperSizeA3 ? PageFormat.A3 : PageFormat.A4;
        var margin = Unit.FromMillimeter(settings.MarginMm);
        section.PageSetup.LeftMargin = margin; section.PageSetup.RightMargin = margin; section.PageSetup.TopMargin = margin; section.PageSetup.BottomMargin = margin;
    }

    // MigraDocの PageSetup.PageWidth/PageHeight は、PageFormatから寸法へ解決されるのが実際の
    // レンダリング処理（PdfDocumentRenderer.RenderDocument）の中でだけであり、Section生成直後に
    // 読み取ると常に0を返す（実機で確認済み）。そのためレイアウト計算用の使用可能幅は、レンダリング前に
    // 自前でA3/A4×縦横の既知の寸法から計算する。
    private static Unit GetUsableWidth(OutputSettings settings)
    {
        var widthMm = (settings.PaperSize, settings.Orientation) switch
        {
            (OutputSettings.PaperSizeA3, OutputSettings.OrientationPortrait) => 297.0,
            (OutputSettings.PaperSizeA3, _) => 420.0,
            (_, OutputSettings.OrientationPortrait) => 210.0,
            _ => 297.0,
        };
        return Unit.FromMillimeter(widthMm - settings.MarginMm * 2);
    }

    private static Color ParseColor(string hex)
    {
        var r = Convert.ToByte(hex.Substring(1, 2), 16); var g = Convert.ToByte(hex.Substring(3, 2), 16); var b = Convert.ToByte(hex.Substring(5, 2), 16);
        return Color.FromRgb(r, g, b);
    }

    private static Color ParseColorOrDefault(string hex, Color fallback)
    {
        try { return System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$") ? ParseColor(hex) : fallback; }
        catch { return fallback; }
    }

    private static Document NewDocument(string title, OutputSettings settings)
    {
        var document = new Document(); document.Info.Title = title; var normal = document.Styles[StyleNames.Normal]!; normal.Font.Name = "SeminarSchedJapanese"; normal.Font.Size = 9;
        return document;
    }

    private static void Save(Document document, string path)
    {
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument(); renderer.PdfDocument.Save(path);
    }

    private static void EnsureFont() { lock (FontGate) { if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new WindowsJapaneseFontResolver(); } }

    private sealed class WindowsJapaneseFontResolver : IFontResolver
    {
        private readonly IReadOnlyDictionary<string, byte[]> _fonts;

        public WindowsJapaneseFontResolver()
        {
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var latinPath = FindFont(directory, "arial.ttf", "segoeui.ttf")
                ?? throw new FileNotFoundException("PDF用のTrueType fontが見つかりません。");
            var courierPath = FindFont(directory, "cour.ttf", "consola.ttf") ?? latinPath;

            // PDFsharp Coreは全てのWindows TTC collectionを確実には読み込めない（BIZ-UDMinchoM.ttcで
            // 実機検証済み: OpenTypeFontFace.CetOrCreateFromでNullReferenceException）。
            // Japanese TrueType単体ファイルを優先し、最小構成のCI環境向けにLatinへfallbackする。
            var japanesePath = FindFont(directory, "HGRSMP.TTF", "HGRSKP.TTF") ?? latinPath;
            _fonts = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["jp"] = File.ReadAllBytes(japanesePath),
                ["sans"] = File.ReadAllBytes(latinPath),
                ["mono"] = File.ReadAllBytes(courierPath),
            };
        }

        public byte[]? GetFont(string faceName) =>
            _fonts.TryGetValue(faceName, out var font) ? font : null;

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (string.Equals(familyName, "SeminarSchedJapanese", StringComparison.OrdinalIgnoreCase))
            {
                return new FontResolverInfo("jp", isBold, isItalic);
            }

            if (familyName.Contains("Courier", StringComparison.OrdinalIgnoreCase)
                || familyName.Contains("Mono", StringComparison.OrdinalIgnoreCase))
            {
                return new FontResolverInfo("mono", isBold, isItalic);
            }

            return new FontResolverInfo("sans", isBold, isItalic);
        }

        private static string? FindFont(string directory, params string[] names) =>
            names.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    }
}
