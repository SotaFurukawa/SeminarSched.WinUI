using ClosedXML.Excel;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

public sealed class ExcelScheduleReportRenderer
{
    public void Render(ScheduleReport report,string path)
    {
        using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("時間割");
        string[] headers=["日付","コマ","生徒","科目","講師","固定"];for(var i=0;i<headers.Length;i++)sheet.Cell(1,i+1).Value=headers[i];
        for(var i=0;i<report.Rows.Count;i++){var r=report.Rows[i];sheet.Cell(i+2,1).Value=r.Date;sheet.Cell(i+2,2).Value=r.TimeSlot;sheet.Cell(i+2,3).Value=r.Student;sheet.Cell(i+2,4).Value=r.Subject;sheet.Cell(i+2,5).Value=r.Teacher;sheet.Cell(i+2,6).Value=r.IsLocked?"固定":"自動";}
        sheet.Row(1).Style.Font.Bold=true;sheet.SheetView.FreezeRows(1);sheet.Columns().AdjustToContents();
        var studentIndex=0;foreach(var group in report.Rows.GroupBy(x=>new{x.Student,x.StudentGrade}).OrderBy(x=>x.Key.StudentGrade).ThenBy(x=>x.Key.Student)){var ws=workbook.AddWorksheet($"生徒{++studentIndex:000}");ws.Cell(1,1).Value=$"{group.Key.StudentGrade} {group.Key.Student}";WriteDistribution(ws,group.Select(x=>(x.Date,x.TimeSlot,x.Subject,x.Teacher)));}
        var teacherIndex=0;foreach(var group in report.Rows.GroupBy(x=>x.Teacher).OrderBy(x=>x.Key)){var ws=workbook.AddWorksheet($"講師{++teacherIndex:000}");ws.Cell(1,1).Value=group.Key;WriteDistribution(ws,group.Select(x=>(x.Date,x.TimeSlot,x.Student,x.Subject)));}
        var issues=workbook.AddWorksheet("未配置・警告");issues.Cell(1,1).Value="未配置";issues.Cell(1,1).Style.Font.Bold=true;for(var i=0;i<report.Unassigned.Count;i++)issues.Cell(i+2,1).Value=report.Unassigned[i];issues.Column(1).AdjustToContents();
        workbook.SaveAs(path);
    }

    private static void WriteDistribution(IXLWorksheet sheet,IEnumerable<(string Date,string Slot,string Item,string Detail)> rows){string[] headers=["日付","コマ","対象","詳細"];for(var i=0;i<4;i++){sheet.Cell(3,i+1).Value=headers[i];sheet.Cell(3,i+1).Style.Font.Bold=true;}var index=4;foreach(var row in rows.OrderBy(x=>x.Date).ThenBy(x=>x.Slot)){sheet.Cell(index,1).Value=row.Date;sheet.Cell(index,2).Value=row.Slot;sheet.Cell(index,3).Value=row.Item;sheet.Cell(index,4).Value=row.Detail;index++;}sheet.Columns().AdjustToContents();sheet.SheetView.FreezeRows(3);}
}
