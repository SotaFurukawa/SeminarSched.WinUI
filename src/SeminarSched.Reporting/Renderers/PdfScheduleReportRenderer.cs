using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

/// <summary>
/// ユーザー指示により、PDF版は「とりあえずxlsxを変換したもの」の位置付けのまま据え置く（列見出しや
/// テーブル構造はExcelScheduleReportRendererに合わせるが、9列handoutページの罫線・縦書き等の細かい
/// 書式まではPython版と一致させていない。はみ出し等のPDF固有の表示崩れは別途対応予定）。
/// </summary>
public sealed class PdfScheduleReportRenderer
{
    private static readonly object FontGate = new();

    public void RenderOverall(ScheduleReport report, string path)
    {
        EnsureFont(); var document = NewDocument("季節講習時間割");
        var section = document.AddSection(); section.PageSetup.Orientation = Orientation.Landscape;
        var title = section.AddParagraph("季節講習時間割"); title.Format.Font.Size = 16; title.Format.Font.Bold = true; title.Format.SpaceAfter = Unit.FromCentimeter(.2);
        var meta = section.AddParagraph($"{report.ProjectTitle}／{report.GeneratedAtText}"); meta.Format.Font.Size = 9; meta.Format.SpaceAfter = Unit.FromCentimeter(.4);

        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        var studentLabels = WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x => x.Student));
        var overviewAssignments = report.Rows.Select(r => new OverviewAssignment(DateOnly.Parse(r.Date), teacherLabels[r.Teacher], r.TimeSlot, r.StudentGrade, r.SubjectShortName, studentLabels[r.Student])).ToArray();
        var overviewUnavailabilities = report.TeacherUnavailabilities.Select(u => new OverviewUnavailability(DateOnly.Parse(u.Date), teacherLabels[u.Teacher], u.TimeSlot)).ToArray();
        var grid = OverviewGridLayout.Build(report.StartDate, report.EndDate, report.OpenDates.ToHashSet(), report.SlotLabels, overviewAssignments, overviewUnavailabilities);
        AddOverview(section, grid);

        Save(document, path);
    }

    public void RenderStudentHandouts(ScheduleReport report, string path) => RenderHandouts(report, path, includeTeacher: false, "生徒配布用生徒別時間割");

    public void RenderTeacherHandouts(ScheduleReport report, string path) => RenderHandouts(report, path, includeTeacher: true, "講師配布用学年別時間割");

    public void RenderTeacherPacket(ScheduleReport report, string teacherName, string path)
    {
        EnsureFont(); var document = NewDocument(teacherName);
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        AddAbsenceSection(document, report);
        var allStudents = report.Rows.GroupBy(x => new { x.Student, x.StudentGrade }).OrderBy(x => GradeOrdering.SortKey(x.Key.StudentGrade)).ThenBy(x => x.Key.Student, StringComparer.Ordinal).Select(g => (g.Key.Student, Grade: g.Key.StudentGrade)).ToArray();
        var regularIds = report.Rows.Where(r => r.Teacher == teacherName && r.IsRegularTeacher).Select(r => r.Student).ToHashSet();
        var seasonalIds = report.Rows.Where(r => r.Teacher == teacherName).Select(r => r.Student).ToHashSet();
        var ordered = allStudents.Where(s => regularIds.Contains(s.Student))
            .Concat(allStudents.Where(s => seasonalIds.Contains(s.Student) && !regularIds.Contains(s.Student)))
            .Concat(allStudents.Where(s => !regularIds.Contains(s.Student) && !seasonalIds.Contains(s.Student)));
        foreach (var s in ordered) AddStudentCalendarSection(document, report, s.Student, s.Grade, includeTeacher: true, teacherLabels);
        Save(document, path);
    }

    public void RenderIssues(ScheduleReport report, string path)
    {
        EnsureFont(); var document = NewDocument("未配置・警告一覧");

        var section = document.AddSection(); section.PageSetup.Orientation = Orientation.Landscape;
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

        var section2 = document.AddSection(); section2.PageSetup.Orientation = Orientation.Landscape;
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

    private static void RenderHandouts(ScheduleReport report, string path, bool includeTeacher, string title)
    {
        EnsureFont(); var document = NewDocument(title);
        var teacherLabels = WeeklyCalendarLayout.BuildTeacherLabels(report.Rows.Select(x => x.Teacher));
        AddAbsenceSection(document, report);
        foreach (var group in report.Rows.GroupBy(x => new { x.Student, x.StudentGrade }).OrderBy(x => GradeOrdering.SortKey(x.Key.StudentGrade)).ThenBy(x => x.Key.Student, StringComparer.Ordinal))
            AddStudentCalendarSection(document, report, group.Key.Student, group.Key.StudentGrade, includeTeacher, teacherLabels);
        Save(document, path);
    }

    private static void AddAbsenceSection(Document document, ScheduleReport report)
    {
        if (report.AbsentStudents.Count == 0) return;
        var section = document.AddSection();
        section.AddParagraph("講習欠席一覧").Format.Font.Bold = true;
        var table = section.AddTable(); table.Borders.Width = .5; table.AddColumn(Unit.FromCentimeter(2.5)); table.AddColumn(Unit.FromCentimeter(5));
        foreach (var student in report.AbsentStudents.OrderBy(s => GradeOrdering.SortKey(s.Grade)).ThenBy(s => s.Name, StringComparer.Ordinal)) { var row = table.AddRow(); row.Cells[0].AddParagraph(student.Grade); row.Cells[1].AddParagraph(student.Name); }
    }

    private static void AddStudentCalendarSection(Document document, ScheduleReport report, string student, string grade, bool includeTeacher, IReadOnlyDictionary<string, string> teacherLabels)
    {
        var page = document.AddSection(); page.PageSetup.Orientation = Orientation.Landscape;
        page.AddParagraph($"{grade} {student} 個別時間割").Format.Font.Size = 15;
        var rows = report.Rows.Where(x => x.Student == student).ToArray();
        var linesByDate = rows.GroupBy(x => DateOnly.Parse(x.Date)).ToDictionary(g => g.Key, IReadOnlyList<string> (g) => g.OrderBy(x => x.TimeSlot).Select(x => includeTeacher ? $"{x.SubjectShortName} {teacherLabels[x.Teacher]}" : x.SubjectShortName).ToArray());
        AddCalendar(page, report.StartDate, report.EndDate, linesByDate);
    }

    private static void AddOverview(Section section, OverviewGrid grid)
    {
        foreach (var week in grid.Weeks)
        {
            if (week.Days.Count == 0) continue;
            section.AddParagraph($"{week.SundayStart:yyyy/M/d}週").Format.Font.Bold = true;
            var totalColumns = 1 + week.Days.Sum(d => Math.Max(1, d.Teachers.Count));
            var table = section.AddTable(); table.Borders.Width = .5;
            for (var i = 0; i < totalColumns; i++) table.AddColumn(Unit.FromCentimeter(i == 0 ? 2.6 : 3.0));

            var dayHeaderRow = table.AddRow(); dayHeaderRow.Shading.Color = Colors.LightGray;
            var teacherHeaderRow = table.AddRow(); teacherHeaderRow.Shading.Color = Colors.LightGray;
            teacherHeaderRow.Cells[0].AddParagraph("コマ").Format.Font.Bold = true;

            var col = 1; var teacherColumns = new List<(OverviewTeacherColumn Teacher, int Column)>();
            foreach (var day in week.Days)
            {
                var teacherCount = Math.Max(1, day.Teachers.Count); var startCol = col;
                var dayCell = dayHeaderRow.Cells[startCol];
                dayCell.AddParagraph($"{day.Date.Month}/{day.Date.Day}({WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]})").Format.Font.Bold = true;
                if (teacherCount > 1) dayCell.MergeRight = teacherCount - 1;

                if (day.Teachers.Count == 0) { teacherHeaderRow.Cells[startCol].AddParagraph("(配置なし)"); col++; continue; }
                foreach (var teacher in day.Teachers)
                {
                    teacherHeaderRow.Cells[col].AddParagraph(teacher.TeacherName).Format.Font.Bold = true;
                    teacherColumns.Add((teacher, col));
                    col++;
                }
            }

            for (var s = 0; s < grid.SlotLabels.Count; s++)
            {
                var row = table.AddRow(); row.Cells[0].AddParagraph(grid.SlotLabels[s]).Format.Font.Bold = true;
                foreach (var (teacher, teacherCol) in teacherColumns)
                {
                    var cell = teacher.Cells[s];
                    if (cell.Cards.Count > 0)
                        foreach (var card in cell.Cards)
                            row.Cells[teacherCol].AddParagraph($"{card.Grade} {card.SubjectShortName} {card.Student}");
                    else if (cell.Unavailable)
                        row.Cells[teacherCol].Shading.Color = Colors.LightGray;
                }
            }
        }
    }

    private static void AddCalendar(Section section, DateOnly start, DateOnly end, IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> linesByDate)
    {
        var weeks = WeeklyCalendarLayout.Build(start, end, linesByDate);
        // 7 columns must fit within A4 landscape's usable width (29.7cm page - 2.5cm left/right margins =
        // 24.7cm); 3.6cm/column (25.2cm total) overflowed into the right margin, so this uses 3.5cm (24.5cm).
        var table = section.AddTable(); table.Borders.Width = .5; for (var i = 0; i < 7; i++) table.AddColumn(Unit.FromCentimeter(3.5));
        var header = table.AddRow(); header.Shading.Color = Colors.LightGray;
        for (var i = 0; i < 7; i++) { header.Cells[i].AddParagraph(WeeklyCalendarLayout.WeekdayHeaders[i]); header.Cells[i].Format.Font.Bold = true; }
        foreach (var week in weeks)
        {
            var row = table.AddRow();
            for (var i = 0; i < 7; i++)
            {
                var day = week.Days[i];
                var paragraph = row.Cells[i].AddParagraph($"{day.Date.Month}/{day.Date.Day}"); paragraph.Format.Font.Bold = true;
                foreach (var line in day.Lines) row.Cells[i].AddParagraph(line);
            }
        }
    }

    private static Document NewDocument(string title)
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

            // PDFsharp Core cannot reliably consume every Windows TTC collection. Prefer a
            // Japanese TrueType face and use a guaranteed TTF fallback on minimal CI images.
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
