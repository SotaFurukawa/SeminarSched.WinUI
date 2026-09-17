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
        var table=section.AddTable();table.Borders.Width=.5;foreach(var width in new[]{2.4,2.6,3.2,2.5,3.2,1.4})table.AddColumn(Unit.FromCentimeter(width));var header=table.AddRow();header.Shading.Color=Colors.LightGray;string[] names=["日付","コマ","生徒","科目","講師","区分"];for(var i=0;i<names.Length;i++){header.Cells[i].AddParagraph(names[i]);header.Cells[i].Format.Font.Bold=true;}
        foreach(var r in report.Rows){var row=table.AddRow();string[] values=[r.Date,r.TimeSlot,r.Student,r.Subject,r.Teacher,r.IsLocked?"固定":"自動"];for(var i=0;i<values.Length;i++)row.Cells[i].AddParagraph(values[i]);}
        if(report.Unassigned.Count>0){section.AddParagraph("未配置").Format.Font.Bold=true;foreach(var item in report.Unassigned)section.AddParagraph("・"+item);}
        if(report.AbsentStudents.Count>0)
        {
            section.AddParagraph("講習欠席一覧").Format.Font.Bold=true;
            var absentTable=section.AddTable();absentTable.Borders.Width=.5;absentTable.AddColumn(Unit.FromCentimeter(2.5));absentTable.AddColumn(Unit.FromCentimeter(5));
            foreach(var student in report.AbsentStudents){var row=absentTable.AddRow();row.Cells[0].AddParagraph(student.Grade);row.Cells[1].AddParagraph(student.Name);}
        }

        var studentLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));
        foreach(var group in report.Rows.GroupBy(x=>new{x.Student,x.StudentGrade}).OrderBy(x=>x.Key.StudentGrade).ThenBy(x=>x.Key.Student))
        {
            var page=document.AddSection();page.PageSetup.Orientation=Orientation.Landscape;
            page.AddParagraph($"{group.Key.StudentGrade} {studentLabels[group.Key.Student]} 配布時間割").Format.Font.Size=15;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {x.Teacher}t").ToArray());
            AddCalendar(page,report.StartDate,report.EndDate,linesByDate);
        }
        foreach(var group in report.Rows.GroupBy(x=>x.Teacher).OrderBy(x=>x.Key))
        {
            var page=document.AddSection();page.PageSetup.Orientation=Orientation.Landscape;
            page.AddParagraph($"{group.Key} 講師配布時間割").Format.Font.Size=15;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {studentLabels[x.Student]}").ToArray());
            AddCalendar(page,report.StartDate,report.EndDate,linesByDate);
        }
        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();renderer.PdfDocument.Save(path);
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
