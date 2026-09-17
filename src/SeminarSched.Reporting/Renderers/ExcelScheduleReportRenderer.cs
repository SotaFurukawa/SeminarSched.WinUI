using ClosedXML.Excel;
using SeminarSched.Reporting.Layout;
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

        var studentLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));
        var studentIndex=0;
        foreach(var group in report.Rows.GroupBy(x=>new{x.Student,x.StudentGrade}).OrderBy(x=>x.Key.StudentGrade).ThenBy(x=>x.Key.Student))
        {
            var ws=workbook.AddWorksheet($"生徒{++studentIndex:000}");
            ws.Cell(1,1).Value=$"{group.Key.StudentGrade} {studentLabels[group.Key.Student]}";ws.Cell(1,1).Style.Font.Bold=true;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {x.Teacher}t").ToArray());
            WriteCalendar(ws,report.StartDate,report.EndDate,linesByDate);
        }

        var teacherIndex=0;
        foreach(var group in report.Rows.GroupBy(x=>x.Teacher).OrderBy(x=>x.Key))
        {
            var ws=workbook.AddWorksheet($"講師{++teacherIndex:000}");
            ws.Cell(1,1).Value=group.Key;ws.Cell(1,1).Style.Font.Bold=true;
            var linesByDate=group.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {studentLabels[x.Student]}").ToArray());
            WriteCalendar(ws,report.StartDate,report.EndDate,linesByDate);
        }

        var issues=workbook.AddWorksheet("未配置・警告");issues.Cell(1,1).Value="未配置";issues.Cell(1,1).Style.Font.Bold=true;for(var i=0;i<report.Unassigned.Count;i++)issues.Cell(i+2,1).Value=report.Unassigned[i];
        var absentStart=report.Unassigned.Count+3;issues.Cell(absentStart,1).Value="講習欠席一覧";issues.Cell(absentStart,1).Style.Font.Bold=true;
        for(var i=0;i<report.AbsentStudents.Count;i++){issues.Cell(absentStart+1+i,1).Value=report.AbsentStudents[i].Grade;issues.Cell(absentStart+1+i,2).Value=report.AbsentStudents[i].Name;}
        issues.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static void WriteCalendar(IXLWorksheet sheet,DateOnly start,DateOnly end,IReadOnlyDictionary<DateOnly,IReadOnlyList<string>> linesByDate)
    {
        var weeks=WeeklyCalendarLayout.Build(start,end,linesByDate);
        var row=3;
        for(var i=0;i<7;i++){sheet.Cell(row,i+1).Value=WeeklyCalendarLayout.WeekdayHeaders[i];sheet.Cell(row,i+1).Style.Font.Bold=true;sheet.Cell(row,i+1).Style.Fill.BackgroundColor=XLColor.LightGray;}
        row++;
        foreach(var week in weeks)
        {
            for(var i=0;i<7;i++)
            {
                var day=week.Days[i];var cell=sheet.Cell(row,i+1);
                cell.Value=$"{day.Date:M/d}\n{string.Join("\n",day.Lines)}";
                cell.Style.Alignment.WrapText=true;cell.Style.Alignment.Vertical=XLAlignmentVerticalValues.Top;
            }
            sheet.Row(row).Height=Math.Max(30,15*(1+week.Days.Max(d=>d.Lines.Count)));
            row++;
        }
        sheet.Columns(1,7).Width=16;sheet.SheetView.FreezeRows(3);
    }
}
