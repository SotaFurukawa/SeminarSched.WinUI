using Microsoft.Data.Sqlite;
using SeminarSched.Application.Output;
using SeminarSched.Reporting.Models;
using SeminarSched.Reporting.Renderers;

namespace SeminarSched.Infrastructure.Output;

public sealed class SqliteOutputPackageService:IOutputPackageService
{
    public async Task<OutputPackageResult> GenerateAsync(string projectPath,string parentDirectory,CancellationToken cancellationToken=default)
    {
        var report=await LoadAndValidate(projectPath,cancellationToken);var target=Path.Combine(Path.GetFullPath(parentDirectory),$"SeminarSched_Output_{DateTime.Now:yyyyMMdd_HHmmss}");var temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
        try{Directory.CreateDirectory(temporary);var xlsx=Path.Combine(temporary,"時間割.xlsx");var pdf=Path.Combine(temporary,"時間割.pdf");await Task.Run(()=>new ExcelScheduleReportRenderer().Render(report,xlsx),cancellationToken);await Task.Run(()=>new PdfScheduleReportRenderer().Render(report,pdf),cancellationToken);Directory.Move(temporary,target);return new(target,Path.Combine(target,"時間割.xlsx"),Path.Combine(target,"時間割.pdf"),report.Rows.Count,report.Unassigned.Count);}finally{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}
    }
    private static async Task<ScheduleReport> LoadAndValidate(string path,CancellationToken token)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false}.ToString());await c.OpenAsync(token);
        await using(var integrity=c.CreateCommand()){integrity.CommandText="PRAGMA integrity_check;";if(!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(token)),"ok",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("project DBの整合性検証に失敗しました。");}
        DateOnly startDate=default,endDate=default;
        await using(var q=c.CreateCommand()){q.CommandText="SELECT StartDate,EndDate FROM CourseProject WHERE Id=1;";await using var r=await q.ExecuteReaderAsync(token);if(await r.ReadAsync(token)){startDate=DateOnly.Parse(r.GetString(0));endDate=DateOnly.Parse(r.GetString(1));}}
        var openDates=new List<DateOnly>();await using(var q=c.CreateCommand()){q.CommandText="SELECT Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))openDates.Add(DateOnly.Parse(r.GetString(0)));}
        var slotLabels=new List<string>();await using(var q=c.CreateCommand()){q.CommandText="SELECT DisplayName||' '||StartTime||'-'||EndTime FROM TimeSlot WHERE Active=1 ORDER BY SortOrder;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))slotLabels.Add(r.GetString(0));}
        var rows=new List<ScheduleReportRow>();await using(var q=c.CreateCommand()){q.CommandText="SELECT d.Date,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime,s.Name,s.Grade,sub.DisplayName,COALESCE(NULLIF(sub.ShortName,''),sub.DisplayName),t.Name,a.IsLocked FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId JOIN Teacher t ON t.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId ORDER BY d.Date,ts.SortOrder,s.ExternalId;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetBoolean(7)));}
        var missing=new List<string>();await using(var q=c.CreateCommand()){q.CommandText="SELECT s.Name||' / '||sub.DisplayName||' : '||(r.RequiredSessions-COUNT(a.Id))||'回' FROM LessonRequest r JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId LEFT JOIN Assignment a ON a.LessonRequestId=r.Id GROUP BY r.Id HAVING COUNT(a.Id)<r.RequiredSessions ORDER BY s.ExternalId,sub.SortOrder;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))missing.Add(r.GetString(0));}
        var absent=new List<AbsentStudent>();await using(var q=c.CreateCommand()){q.CommandText="SELECT s.Grade,s.Name FROM Student s WHERE s.Active=1 AND NOT EXISTS(SELECT 1 FROM LessonRequest r WHERE r.StudentId=s.Id) ORDER BY s.ExternalId;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))absent.Add(new(r.GetString(0),r.GetString(1)));}
        var shortfalls=await LoadRegularTeacherShortfallsAsync(c,token);
        return new(Path.GetFileNameWithoutExtension(path)+" 時間割",startDate,endDate,openDates,slotLabels,rows,missing,absent,shortfalls);
    }

    /// <summary>
    /// Python版5.3節の目標割合（優先度5=100%,4=75%,3=50%,2=25%）を下回っている生徒・科目を列挙する。
    /// 目標値の切り上げ式は<see cref="SeminarSched.Optimization.Core.CpSatScheduleSolver.MinimumRegularTeacherSessions"/>と同一。
    /// </summary>
    private static async Task<List<string>> LoadRegularTeacherShortfallsAsync(SqliteConnection c,CancellationToken token)
    {
        var shortfalls=new List<string>();
        await using var q=c.CreateCommand();
        q.CommandText="""
            SELECT s.Name,sub.DisplayName,te.Name,r.RequiredSessions,
                   COALESCE(NULLIF(r.RegularTeacherPriority,1),p.RegularTeacherPriority,1) AS priority,
                   (SELECT COUNT(*) FROM Assignment a WHERE a.LessonRequestId=r.Id AND a.TeacherId=COALESCE(r.RegularTeacherId,p.RegularTeacherId)) AS actual
            FROM LessonRequest r
            JOIN Student s ON s.Id=r.StudentId
            JOIN Subject sub ON sub.Id=r.SubjectId
            JOIN Teacher te ON te.Id=COALESCE(r.RegularTeacherId,p.RegularTeacherId)
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            WHERE COALESCE(r.RegularTeacherId,p.RegularTeacherId) IS NOT NULL
              AND COALESCE(NULLIF(r.RegularTeacherPriority,1),p.RegularTeacherPriority,1)>=2
            ORDER BY s.ExternalId,sub.SortOrder;
            """;
        await using var reader=await q.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var student=reader.GetString(0);var subject=reader.GetString(1);var teacher=reader.GetString(2);
            var required=reader.GetInt32(3);var priority=reader.GetInt32(4);var actual=reader.GetInt32(5);
            var target=Math.Max(0,((priority-1)*required+3)/4);
            if(actual<target)shortfalls.Add($"{student} / {subject} : 通常担当{teacher}　目標{target}回中{actual}回");
        }
        return shortfalls;
    }
}
