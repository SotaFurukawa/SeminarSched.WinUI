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
        var issues=workbook.AddWorksheet("未配置・警告");issues.Cell(1,1).Value="未配置";issues.Cell(1,1).Style.Font.Bold=true;for(var i=0;i<report.Unassigned.Count;i++)issues.Cell(i+2,1).Value=report.Unassigned[i];issues.Column(1).AdjustToContents();
        workbook.SaveAs(path);
    }
}
