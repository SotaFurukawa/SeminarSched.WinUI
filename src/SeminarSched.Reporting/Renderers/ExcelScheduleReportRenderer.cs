using ClosedXML.Excel;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;

namespace SeminarSched.Reporting.Renderers;

public sealed class ExcelScheduleReportRenderer
{
    public void Render(ScheduleReport report,string path)
    {
        using var workbook=new XLWorkbook();
        var studentLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));

        var overview=workbook.AddWorksheet("全体時間割");
        var overviewAssignments=report.Rows.Select(r=>new OverviewAssignment(DateOnly.Parse(r.Date),r.Teacher,r.TimeSlot,r.StudentGrade,r.SubjectShortName,studentLabels[r.Student])).ToArray();
        var overviewUnavailabilities=report.TeacherUnavailabilities.Select(u=>new OverviewUnavailability(DateOnly.Parse(u.Date),u.Teacher,u.TimeSlot)).ToArray();
        var grid=OverviewGridLayout.Build(report.StartDate,report.EndDate,report.OpenDates.ToHashSet(),report.SlotLabels,overviewAssignments,overviewUnavailabilities);
        WriteOverview(overview,grid);

        var flatSheet=workbook.AddWorksheet("配置一覧");
        string[] headers=["日付","コマ","生徒","科目","講師","固定"];for(var i=0;i<headers.Length;i++)flatSheet.Cell(1,i+1).Value=headers[i];
        for(var i=0;i<report.Rows.Count;i++){var r=report.Rows[i];flatSheet.Cell(i+2,1).Value=r.Date;flatSheet.Cell(i+2,2).Value=r.TimeSlot;flatSheet.Cell(i+2,3).Value=r.Student;flatSheet.Cell(i+2,4).Value=r.Subject;flatSheet.Cell(i+2,5).Value=r.Teacher;flatSheet.Cell(i+2,6).Value=r.IsLocked?"固定":"自動";}
        flatSheet.Row(1).Style.Font.Bold=true;flatSheet.SheetView.FreezeRows(1);flatSheet.Columns().AdjustToContents();

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
        var shortfallStart=report.Unassigned.Count+3;issues.Cell(shortfallStart,1).Value="通常担当不足";issues.Cell(shortfallStart,1).Style.Font.Bold=true;
        for(var i=0;i<report.RegularTeacherShortfalls.Count;i++)issues.Cell(shortfallStart+1+i,1).Value=report.RegularTeacherShortfalls[i];
        var absentStart=shortfallStart+report.RegularTeacherShortfalls.Count+2;issues.Cell(absentStart,1).Value="講習欠席一覧";issues.Cell(absentStart,1).Style.Font.Bold=true;
        for(var i=0;i<report.AbsentStudents.Count;i++){issues.Cell(absentStart+1+i,1).Value=report.AbsentStudents[i].Grade;issues.Cell(absentStart+1+i,2).Value=report.AbsentStudents[i].Name;}
        issues.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    /// <summary>
    /// Python版6節「講師別出力」相当。講師ごとに独立したファイルとして、担当一覧（通常担当を先に列挙）と
    /// 個別の週calendarを生成する。呼び出し側（output service）が講師別folderへ配置する。
    /// </summary>
    public void RenderTeacherPacket(ScheduleReport report,string teacherName,string path)
    {
        var teacherRows=report.Rows.Where(x=>x.Teacher==teacherName).ToArray();
        var studentLabels=WeeklyCalendarLayout.BuildStudentLabels(report.Rows.Select(x=>x.Student));
        using var workbook=new XLWorkbook();

        var roster=workbook.AddWorksheet("担当一覧");
        roster.Cell(1,1).Value=$"{teacherName} 担当一覧";roster.Cell(1,1).Style.Font.Bold=true;
        var regular=teacherRows.Where(x=>x.IsRegularTeacher).Select(x=>$"{studentLabels[x.Student]} {x.SubjectShortName}").Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var others=teacherRows.Where(x=>!x.IsRegularTeacher).Select(x=>$"{studentLabels[x.Student]} {x.SubjectShortName}").Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var row=3;
        roster.Cell(row,1).Value="通常担当";roster.Cell(row,1).Style.Font.Bold=true;row++;
        foreach(var line in regular){roster.Cell(row,1).Value=line;row++;}
        row++;
        roster.Cell(row,1).Value="講習担当（その他）";roster.Cell(row,1).Style.Font.Bold=true;row++;
        foreach(var line in others){roster.Cell(row,1).Value=line;row++;}
        roster.Column(1).AdjustToContents();

        var calendar=workbook.AddWorksheet("時間割");
        calendar.Cell(1,1).Value=teacherName;calendar.Cell(1,1).Style.Font.Bold=true;
        var linesByDate=teacherRows.GroupBy(x=>DateOnly.Parse(x.Date)).ToDictionary(g=>g.Key,IReadOnlyList<string> (g)=>g.OrderBy(x=>x.TimeSlot).Select(x=>$"{x.SubjectShortName} {studentLabels[x.Student]}").ToArray());
        WriteCalendar(calendar,report.StartDate,report.EndDate,linesByDate);

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

    private static void WriteOverview(IXLWorksheet sheet,OverviewGrid grid)
    {
        var row=1;
        foreach(var week in grid.Weeks)
        {
            sheet.Cell(row,1).Value=$"{week.SundayStart:yyyy/M/d}週";sheet.Cell(row,1).Style.Font.Bold=true;
            var dayHeaderRow=row+1;var teacherHeaderRow=row+2;var slotStartRow=row+3;
            sheet.Cell(teacherHeaderRow,1).Value="コマ";sheet.Cell(teacherHeaderRow,1).Style.Font.Bold=true;

            var col=2;
            foreach(var day in week.Days)
            {
                var teacherCount=Math.Max(1,day.Teachers.Count);var dayStartCol=col;
                sheet.Cell(dayHeaderRow,dayStartCol).Value=$"{day.Date:M/d}({WeeklyCalendarLayout.WeekdayHeaders[(int)day.Date.DayOfWeek]})";
                sheet.Cell(dayHeaderRow,dayStartCol).Style.Font.Bold=true;sheet.Cell(dayHeaderRow,dayStartCol).Style.Fill.BackgroundColor=XLColor.LightGray;
                if(teacherCount>1)sheet.Range(dayHeaderRow,dayStartCol,dayHeaderRow,dayStartCol+teacherCount-1).Merge();

                if(day.Teachers.Count==0){sheet.Cell(teacherHeaderRow,dayStartCol).Value="(配置なし)";col++;continue;}
                foreach(var teacher in day.Teachers)
                {
                    sheet.Cell(teacherHeaderRow,col).Value=teacher.TeacherName;sheet.Cell(teacherHeaderRow,col).Style.Font.Bold=true;
                    for(var s=0;s<grid.SlotLabels.Count;s++)
                    {
                        var cell=sheet.Cell(slotStartRow+s,col);cell.Style.Alignment.WrapText=true;
                        var cards=teacher.Cells[s].Cards;
                        if(cards.Count>0)cell.Value=string.Join("\n",cards.Select(c=>$"{c.Grade} {c.SubjectShortName} {c.Student}"));
                        else if(teacher.Cells[s].Unavailable)cell.Style.Fill.BackgroundColor=XLColor.LightGray;
                    }
                    col++;
                }
            }
            for(var s=0;s<grid.SlotLabels.Count;s++){sheet.Cell(slotStartRow+s,1).Value=grid.SlotLabels[s];sheet.Cell(slotStartRow+s,1).Style.Font.Bold=true;}
            row=slotStartRow+grid.SlotLabels.Count+1;
        }
        sheet.Columns().AdjustToContents();sheet.Column(1).Width=Math.Max(sheet.Column(1).Width,14);
    }
}
