using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
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
        foreach(var group in report.Rows.GroupBy(x=>new{x.Student,x.StudentGrade}).OrderBy(x=>x.Key.StudentGrade).ThenBy(x=>x.Key.Student)){var page=document.AddSection();page.AddParagraph($"{group.Key.StudentGrade} {group.Key.Student} 配布時間割").Format.Font.Size=15;AddDistribution(page,group.Select(x=>(x.Date,x.TimeSlot,x.Subject,x.Teacher)));}
        foreach(var group in report.Rows.GroupBy(x=>x.Teacher).OrderBy(x=>x.Key)){var page=document.AddSection();page.AddParagraph($"{group.Key} 講師配布時間割").Format.Font.Size=15;AddDistribution(page,group.Select(x=>(x.Date,x.TimeSlot,x.Student,x.Subject)));}
        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();renderer.PdfDocument.Save(path);
    }
    private static void AddDistribution(Section section,IEnumerable<(string Date,string Slot,string Item,string Detail)> rows){var table=section.AddTable();table.Borders.Width=.5;foreach(var width in new[]{3.0,3.8,4.0,4.0})table.AddColumn(Unit.FromCentimeter(width));var header=table.AddRow();string[] names=["日付","コマ","対象","詳細"];for(var i=0;i<4;i++){header.Cells[i].AddParagraph(names[i]);header.Cells[i].Format.Font.Bold=true;}foreach(var value in rows.OrderBy(x=>x.Date).ThenBy(x=>x.Slot)){var row=table.AddRow();string[] cells=[value.Date,value.Slot,value.Item,value.Detail];for(var i=0;i<4;i++)row.Cells[i].AddParagraph(cells[i]);}}
    private static void EnsureFont(){lock(FontGate){if(GlobalFontSettings.FontResolver is null)GlobalFontSettings.FontResolver=new WindowsJapaneseFontResolver();}}
    private sealed class WindowsJapaneseFontResolver:IFontResolver
    {
        private readonly byte[] _font;
        public WindowsJapaneseFontResolver(){var dir=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);var path=new[]{"HGRSMP.TTF","HGRSKP.TTF","YuGothM.ttc","meiryo.ttc"}.Select(x=>Path.Combine(dir,x)).FirstOrDefault(File.Exists)??throw new FileNotFoundException("日本語PDF用fontが見つかりません。");_font=File.ReadAllBytes(path);}
        public byte[]? GetFont(string faceName)=>faceName=="jp"?_font:null;
        public FontResolverInfo? ResolveTypeface(string familyName,bool isBold,bool isItalic)=>new("jp");
    }
}
