using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

public sealed class PdfScheduleReportRenderer
{
    private static readonly object FontGate=new();
    public void Render(ScheduleReport report,string path)
    {
        EnsureFont();var document=new Document();document.Info.Title=report.Title;var normal=document.Styles[StyleNames.Normal]!;normal.Font.Name="SeminarSchedJapanese";normal.Font.Size=9;
        var section=document.AddSection();section.PageSetup.Orientation=Orientation.Landscape;var title=section.AddParagraph(report.Title);title.Format.Font.Size=16;title.Format.Font.Bold=true;title.Format.SpaceAfter=Unit.FromCentimeter(.4);

        var overviewLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));
        var overviewAssignments=report.Rows.Select(r=>new OverviewAssignment(DateOnly.Parse(r.Date),r.Teacher,r.TimeSlot,r.StudentGrade,r.SubjectShortName,overviewLabels[r.Student])).ToArray();
        var overviewUnavailabilities=report.TeacherUnavailabilities.Select(u=>new OverviewUnavailability(DateOnly.Parse(u.Date),u.Teacher,u.TimeSlot)).ToArray();
        var overviewGrid=OverviewGridLayout.Build(report.StartDate,report.EndDate,report.OpenDates.ToHashSet(),report.SlotLabels,overviewAssignments,overviewUnavailabilities);
        AddOverview(section,overviewGrid);

        if(report.Unassigned.Count>0){section.AddParagraph("未配置").Format.Font.Bold=true;foreach(var item in report.Unassigned)section.AddParagraph("・"+item);}
        if(report.RegularTeacherShortfalls.Count>0){section.AddParagraph("通常担当不足").Format.Font.Bold=true;foreach(var item in report.RegularTeacherShortfalls)section.AddParagraph("・"+item);}
        if(report.AbsentStudents.Count>0)
        {
            section.AddParagraph("講習欠席一覧").Format.Font.Bold=true;
            var absentTable=section.AddTable();absentTable.Borders.Width=.5;absentTable.AddColumn(Unit.FromCentimeter(2.5));absentTable.AddColumn(Unit.FromCentimeter(5));
            foreach(var student in report.AbsentStudents){var row=absentTable.AddRow();row.Cells[0].AddParagraph(student.Grade);row.Cells[1].AddParagraph(student.Name);}
        }

        foreach(var group in report.Rows.GroupBy(x=>new{x.Student,x.StudentGrade}).OrderBy(x=>x.Key.StudentGrade).ThenBy(x=>x.Key.Student))
        {
            var page=document.AddSection();page.PageSetup.Orientation=Orientation.Landscape;
            page.AddParagraph($"{group.Key.StudentGrade} {overviewLabels[group.Key.Student]} 配布時間割").Format.Font.Size=15;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {x.Teacher}t").ToArray());
            AddCalendar(page,report.StartDate,report.EndDate,linesByDate);
        }
        foreach(var group in report.Rows.GroupBy(x=>x.Teacher).OrderBy(x=>x.Key))
        {
            var page=document.AddSection();page.PageSetup.Orientation=Orientation.Landscape;
            page.AddParagraph($"{group.Key} 講師配布時間割").Format.Font.Size=15;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {overviewLabels[x.Student]}").ToArray());
            AddCalendar(page,report.StartDate,report.EndDate,linesByDate);
        }
        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();renderer.PdfDocument.Save(path);
    }

    /// <summary>
    /// PDF版の講師別個別ファイル。Excel版RenderTeacherPacketと同じ担当一覧（通常担当→講習担当の2区分）
    /// ＋週calendarの構成だが、独立したPDFファイルとして出力する。
    /// </summary>
    public void RenderTeacherPacket(ScheduleReport report,string teacherName,string path)
    {
        EnsureFont();
        var teacherRows=report.Rows.Where(x=>x.Teacher==teacherName).ToArray();
        var studentLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));
        var document=new Document();document.Info.Title=teacherName;var normal=document.Styles[StyleNames.Normal]!;normal.Font.Name="SeminarSchedJapanese";normal.Font.Size=9;

        var roster=document.AddSection();
        roster.AddParagraph($"{teacherName} 担当一覧").Format.Font.Size=16;
        var regular=teacherRows.Where(x=>x.IsRegularTeacher).Select(x=>$"{studentLabels[x.Student]} {x.SubjectShortName}").Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var others=teacherRows.Where(x=>!x.IsRegularTeacher).Select(x=>$"{studentLabels[x.Student]} {x.SubjectShortName}").Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        roster.AddParagraph("通常担当").Format.Font.Bold=true;
        foreach(var line in regular)roster.AddParagraph("・"+line);
        roster.AddParagraph("講習担当").Format.Font.Bold=true;
        foreach(var line in others)roster.AddParagraph("・"+line);

        var calendar=document.AddSection();calendar.PageSetup.Orientation=Orientation.Landscape;
        calendar.AddParagraph($"{teacherName} 時間割").Format.Font.Size=15;
        var linesByDate=teacherRows.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {studentLabels[x.Student]}").ToArray());
        AddCalendar(calendar,report.StartDate,report.EndDate,linesByDate);

        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();renderer.PdfDocument.Save(path);
    }

    private static void AddOverview(Section section,OverviewGrid grid)
    {
        foreach(var week in grid.Weeks)
        {
            if(week.Days.Count==0)continue;
            section.AddParagraph($"{week.SundayStart:yyyy/M/d}週").Format.Font.Bold=true;
            var totalColumns=1+week.Days.Sum(d=>Math.Max(1,d.Teachers.Count));
            var table=section.AddTable();table.Borders.Width=.5;
            for(var i=0;i<totalColumns;i++)table.AddColumn(Unit.FromCentimeter(i==0?2.6:3.0));

            var dayHeaderRow=table.AddRow();dayHeaderRow.Shading.Color=Colors.LightGray;
            var teacherHeaderRow=table.AddRow();teacherHeaderRow.Shading.Color=Colors.LightGray;
            teacherHeaderRow.Cells[0].AddParagraph("コマ").Format.Font.Bold=true;

            var col=1;var teacherColumns=new List<(OverviewTeacherColumn Teacher,int Column)>();
            foreach(var day in week.Days)
            {
                var teacherCount=Math.Max(1,day.Teachers.Count);var startCol=col;
                var dayCell=dayHeaderRow.Cells[startCol];
                dayCell.AddParagraph($"{day.Date.Month}/{day.Date.Day}({WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]})").Format.Font.Bold=true;
                if(teacherCount>1)dayCell.MergeRight=teacherCount-1;

                if(day.Teachers.Count==0){teacherHeaderRow.Cells[startCol].AddParagraph("(配置なし)");col++;continue;}
                foreach(var teacher in day.Teachers)
                {
                    teacherHeaderRow.Cells[col].AddParagraph(teacher.TeacherName).Format.Font.Bold=true;
                    teacherColumns.Add((teacher,col));
                    col++;
                }
            }

            for(var s=0;s<grid.SlotLabels.Count;s++)
            {
                var row=table.AddRow();row.Cells[0].AddParagraph(grid.SlotLabels[s]).Format.Font.Bold=true;
                foreach(var(teacher,teacherCol) in teacherColumns)
                {
                    var cell=teacher.Cells[s];
                    if(cell.Cards.Count>0)
                        foreach(var card in cell.Cards)
                            row.Cells[teacherCol].AddParagraph($"{card.Grade} {card.SubjectShortName} {card.Student}");
                    else if(cell.Unavailable)
                        row.Cells[teacherCol].Shading.Color=Colors.LightGray;
                }
            }
        }
    }

    private static void AddCalendar(Section section,DateOnly start,DateOnly end,IReadOnlyDictionary<DateOnly,IReadOnlyList<string>> linesByDate)
    {
        var weeks=WeeklyCalendarLayout.Build(start,end,linesByDate);
        var table=section.AddTable();table.Borders.Width=.5;for(var i=0;i<7;i++)table.AddColumn(Unit.FromCentimeter(3.6));
        var header=table.AddRow();header.Shading.Color=Colors.LightGray;
        for(var i=0;i<7;i++){header.Cells[i].AddParagraph(WeeklyCalendarLayout.WeekdayHeaders[i]);header.Cells[i].Format.Font.Bold=true;}
        foreach(var week in weeks)
        {
            var row=table.AddRow();
            for(var i=0;i<7;i++)
            {
                var day=week.Days[i];
                var paragraph=row.Cells[i].AddParagraph($"{day.Date.Month}/{day.Date.Day}");paragraph.Format.Font.Bold=true;
                foreach(var line in day.Lines)row.Cells[i].AddParagraph(line);
            }
        }
    }
    private static void EnsureFont(){lock(FontGate){if(GlobalFontSettings.FontResolver is null)GlobalFontSettings.FontResolver=new WindowsJapaneseFontResolver();}}
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
