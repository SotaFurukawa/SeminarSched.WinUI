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
        var rows=new List<ScheduleReportRow>();await using(var q=c.CreateCommand()){q.CommandText="SELECT d.Date,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime,s.Name,sub.DisplayName,t.Name,a.IsLocked FROM Assignment a JOIN LessonRequest r ON r.Id=a.LessonRequestId JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId JOIN Teacher t ON t.Id=a.TeacherId JOIN OpenDate d ON d.Id=a.OpenDateId JOIN TimeSlot ts ON ts.Id=a.TimeSlotId ORDER BY d.Date,ts.SortOrder,s.ExternalId;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetBoolean(5)));}
        var missing=new List<string>();await using(var q=c.CreateCommand()){q.CommandText="SELECT s.Name||' / '||sub.DisplayName||' : '||(r.RequiredSessions-COUNT(a.Id))||'回' FROM LessonRequest r JOIN Student s ON s.Id=r.StudentId JOIN Subject sub ON sub.Id=r.SubjectId LEFT JOIN Assignment a ON a.LessonRequestId=r.Id GROUP BY r.Id HAVING COUNT(a.Id)<r.RequiredSessions ORDER BY s.ExternalId,sub.SortOrder;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))missing.Add(r.GetString(0));}
        return new(Path.GetFileNameWithoutExtension(path)+" 時間割",rows,missing);
    }
}
