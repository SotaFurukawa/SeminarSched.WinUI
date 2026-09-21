using Microsoft.Data.Sqlite;
using SeminarSched.Application;
using SeminarSched.Application.Output;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Reporting.Layout;
using SeminarSched.Reporting.Models;
using SeminarSched.Reporting.Renderers;

namespace SeminarSched.Infrastructure.Output;

public sealed class SqliteOutputPackageService:IOutputPackageService
{
    public async Task<OutputPackageResult> GenerateAsync(string projectPath,string parentDirectory,CancellationToken cancellationToken=default)
    {
        var report=await LoadAndValidate(projectPath,cancellationToken);
        var outputSettings=await new SqliteOutputSettingsRepository().GetAsync(projectPath,cancellationToken);
        // ユーザー指示: 出力は校舎ごとではなくプロジェクトごとにフォルダを分ける（同じ既定出力先フォルダを
        // 複数projectで使い回しても、実行ごとのタイムスタンプfolderだけが並んで混ざらないようにする）。
        var projectFolder=Path.Combine(Path.GetFullPath(parentDirectory),SeminarSched.Domain.Output.OutputSettings.SanitizeForFileName(report.ProjectTitle));
        var today=DateOnly.FromDateTime(DateTime.Now);
        var target=Path.Combine(projectFolder,$"SeminarSched_Output_{DateTime.Now:yyyyMMdd_HHmmssfff}");var temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporary);
            var excel=new ExcelScheduleReportRenderer();var pdf=new PdfScheduleReportRenderer();
            var styleSettings=TryLoadPreviousHandoutStyleSettings(projectFolder);

            string FileName(string reportName)=>outputSettings.BuildFileName(report.ProjectTitle,reportName,today);

            var overallXlsx=Path.Combine(temporary,FileName("全体時間割")+".xlsx");var overallPdf=Path.Combine(temporary,FileName("全体時間割")+".pdf");
            await Task.Run(()=>excel.RenderOverall(report,overallXlsx),cancellationToken);
            await Task.Run(()=>pdf.RenderOverall(report,overallPdf,outputSettings),cancellationToken);

            var studentXlsx=Path.Combine(temporary,FileName("生徒配布用生徒別時間割")+".xlsx");var studentPdf=Path.Combine(temporary,FileName("生徒配布用生徒別時間割")+".pdf");
            await Task.Run(()=>excel.RenderStudentHandouts(report,studentXlsx,styleSettings),cancellationToken);
            await Task.Run(()=>pdf.RenderStudentHandouts(report,studentPdf,outputSettings),cancellationToken);

            var teacherXlsx=Path.Combine(temporary,FileName("講師配布用学年別時間割")+".xlsx");var teacherPdf=Path.Combine(temporary,FileName("講師配布用学年別時間割")+".pdf");
            await Task.Run(()=>excel.RenderTeacherHandouts(report,teacherXlsx,styleSettings),cancellationToken);
            await Task.Run(()=>pdf.RenderTeacherHandouts(report,teacherPdf,outputSettings),cancellationToken);

            var issuesXlsx=Path.Combine(temporary,FileName("未配置・警告一覧")+".xlsx");var issuesPdf=Path.Combine(temporary,FileName("未配置・警告一覧")+".pdf");
            await Task.Run(()=>excel.RenderIssues(report,issuesXlsx),cancellationToken);
            await Task.Run(()=>pdf.RenderIssues(report,issuesPdf,outputSettings),cancellationToken);

            var teacherNames=report.Rows.Select(x=>x.Teacher).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();

            var teacherPacketDirectory=Path.Combine(temporary,"講師配布用講師別時間割");
            Directory.CreateDirectory(teacherPacketDirectory);
            var usedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var teacherName in teacherNames)
            {
                var excelFileName=SanitizeTeacherFileName(teacherName,"xlsx",usedNames);
                await Task.Run(()=>excel.RenderTeacherPacket(report,teacherName,Path.Combine(teacherPacketDirectory,excelFileName),styleSettings),cancellationToken);
                var pdfFileName=SanitizeTeacherFileName(teacherName,"pdf",usedNames);
                await Task.Run(()=>pdf.RenderTeacherPacket(report,teacherName,Path.Combine(teacherPacketDirectory,pdfFileName),outputSettings),cancellationToken);
            }

            var combinedTeacherXlsx=Path.Combine(temporary,FileName("講師配布用講師別時間割(一括)")+".xlsx");var combinedTeacherPdf=Path.Combine(temporary,FileName("講師配布用講師別時間割(一括)")+".pdf");
            await Task.Run(()=>excel.RenderTeacherPacketsCombined(report,teacherNames,combinedTeacherXlsx,styleSettings),cancellationToken);
            await Task.Run(()=>pdf.RenderTeacherPacketsCombined(report,teacherNames,combinedTeacherPdf,outputSettings),cancellationToken);

            Directory.CreateDirectory(projectFolder);
            Directory.Move(temporary,target);
            return new(
                target,
                Path.Combine(target,Path.GetFileName(overallXlsx)),Path.Combine(target,Path.GetFileName(overallPdf)),
                Path.Combine(target,Path.GetFileName(studentXlsx)),Path.Combine(target,Path.GetFileName(studentPdf)),
                Path.Combine(target,Path.GetFileName(teacherXlsx)),Path.Combine(target,Path.GetFileName(teacherPdf)),
                Path.Combine(target,Path.GetFileName(issuesXlsx)),Path.Combine(target,Path.GetFileName(issuesPdf)),
                Path.Combine(target,"講師配布用講師別時間割"),
                Path.Combine(target,Path.GetFileName(combinedTeacherXlsx)),Path.Combine(target,Path.GetFileName(combinedTeacherPdf)),
                report.Rows.Count,report.UnassignedRequests.Sum(r=>r.Missing));
        }
        finally{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}
    }

    /// <summary>
    /// 校舎が直前の出力フォルダのxlsx内「デザイン設定」シートを編集していた場合、その値を
    /// 読み戻して今回の出力に反映する。同じプロジェクトフォルダに過去の出力が無い・読み取りに失敗した等の
    /// 場合はnull（＝各レンダラーが既定値を使う）を返す。生成中の一時フォルダ（.tmp-*）は対象外。
    /// ファイル名はOutputSettings.FileNamePatternで変わりうるため、「生徒配布用生徒別時間割」を
    /// 含むxlsxをfolder内から探す（固定ファイル名には依存しない）。
    /// </summary>
    private static HandoutStyleSettings? TryLoadPreviousHandoutStyleSettings(string projectFolder)
    {
        try
        {
            if(!Directory.Exists(projectFolder))return null;
            var previous=Directory.GetDirectories(projectFolder,"SeminarSched_Output_*")
                .Where(d=>!Path.GetFileName(d).Contains(".tmp-",StringComparison.Ordinal))
                .OrderByDescending(d=>Path.GetFileName(d),StringComparer.Ordinal)
                .FirstOrDefault();
            if(previous is null)return null;
            var studentXlsx=Directory.GetFiles(previous,"*.xlsx").FirstOrDefault(f=>Path.GetFileName(f).Contains("生徒配布用生徒別時間割",StringComparison.Ordinal));
            return studentXlsx is null?null:ExcelScheduleReportRenderer.TryReadHandoutStyleSettings(studentXlsx);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeTeacherFileName(string teacherName,string extension,HashSet<string> usedNames)
    {
        var invalid=Path.GetInvalidFileNameChars();
        var sanitized=new string(WeeklyCalendarLayout.Surname(teacherName).Where(ch=>!invalid.Contains(ch)).ToArray()).Trim();
        if(sanitized.Length==0)sanitized="講師";
        var candidate=$"{sanitized}t.{extension}";
        var suffix=2;
        while(!usedNames.Add(candidate))candidate=$"{sanitized}t_{suffix++}.{extension}";
        return candidate;
    }

    private static async Task<ScheduleReport> LoadAndValidate(string path,CancellationToken token)
    {
        await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false}.ToString());await c.OpenAsync(token);
        await using(var integrity=c.CreateCommand()){integrity.CommandText="PRAGMA integrity_check;";if(!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(token)),"ok",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("project DBの整合性検証に失敗しました。");}

        string projectTitle="";DateOnly startDate=default,endDate=default;var academicYear=0;var seasonName="";
        await using(var q=c.CreateCommand())
        {
            q.CommandText="SELECT Title,AcademicYear,Season,StartDate,EndDate FROM CourseProject WHERE Id=1;";
            await using var r=await q.ExecuteReaderAsync(token);
            if(await r.ReadAsync(token))
            {
                projectTitle=r.GetString(0);academicYear=r.GetInt32(1);seasonName=((CourseSeason)r.GetInt32(2)).ToJapaneseName();
                startDate=DateOnly.Parse(r.GetString(3));endDate=DateOnly.Parse(r.GetString(4));
            }
        }

        var openDates=new List<DateOnly>();await using(var q=c.CreateCommand()){q.CommandText="SELECT Date FROM OpenDate WHERE IsOpen=1 ORDER BY Date;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))openDates.Add(DateOnly.Parse(r.GetString(0)));}
        var slotDefinitions=new List<SlotDefinition>();await using(var q=c.CreateCommand()){q.CommandText="SELECT Code,DisplayName,StartTime,EndTime FROM TimeSlot WHERE Active=1 ORDER BY SortOrder;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token)){var code=r.GetString(0);var displayName=r.GetString(1);var start=r.GetString(2);var end=r.GetString(3);slotDefinitions.Add(new($"{displayName} {start}-{end}",code,start,end));}}
        var rows=new List<ScheduleReportRow>();await using(var q=c.CreateCommand()){q.CommandText="""
            SELECT d.Date,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime,s.Name,s.Grade,sub.DisplayName,sub.ShortName,sub.Code,t.Name,a.IsLocked,
                   CASE WHEN a.TeacherId=COALESCE(r.RegularTeacherId,p.RegularTeacherId) THEN 1 ELSE 0 END,r.OneToOneRequired,a.IsManual
            FROM Assignment a
            JOIN LessonRequest r ON r.Id=a.LessonRequestId
            JOIN Student s ON s.Id=r.StudentId
            JOIN Subject sub ON sub.Id=r.SubjectId
            JOIN Teacher t ON t.Id=a.TeacherId
            JOIN OpenDate d ON d.Id=a.OpenDateId
            JOIN TimeSlot ts ON ts.Id=a.TimeSlotId
            LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
            ORDER BY d.Date,ts.SortOrder,s.ExternalId;
            """;await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),SubjectAbbreviation.Resolve(r.GetString(4),r.GetString(5),r.GetString(6)),r.GetString(7),r.GetBoolean(8),r.GetBoolean(9),r.GetBoolean(10),r.GetBoolean(11)));}

        var unassigned=await LoadUnassignedRequestsAsync(c,token);
        var absent=new List<AbsentStudent>();await using(var q=c.CreateCommand()){q.CommandText="SELECT s.Grade,s.Name FROM Student s WHERE s.Active=1 AND NOT EXISTS(SELECT 1 FROM LessonRequest r WHERE r.StudentId=s.Id) ORDER BY s.ExternalId;";await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))absent.Add(new(r.GetString(0),r.GetString(1)));}
        var warnings=await LoadWarningsAsync(c,token);
        var unavailabilities=new List<TeacherUnavailabilityCell>();await using(var q=c.CreateCommand()){q.CommandText="""
            SELECT d.Date,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime,t.Name
            FROM TeacherUnavailability u
            JOIN OpenDate d ON d.Id=u.OpenDateId
            JOIN TimeSlot ts ON ts.Id=u.TimeSlotId
            JOIN Teacher t ON t.Id=u.TeacherId;
            """;await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))unavailabilities.Add(new(r.GetString(0),r.GetString(1),r.GetString(2)));}
        var groupLessonAttendances=new List<GroupLessonAttendance>();await using(var q=c.CreateCommand()){q.CommandText="""
            SELECT s.Name,d.Date,sess.StartTime,sess.EndTime
            FROM GroupLessonEnrollment e
            JOIN GroupLessonSession sess ON sess.ClassId=e.ClassId
            JOIN Student s ON s.Id=e.StudentId
            JOIN OpenDate d ON d.Id=sess.OpenDateId;
            """;await using var r=await q.ExecuteReaderAsync(token);while(await r.ReadAsync(token))groupLessonAttendances.Add(new(r.GetString(0),DateOnly.Parse(r.GetString(1)),TimeOnly.ParseExact(r.GetString(2),"HH:mm",System.Globalization.CultureInfo.InvariantCulture),TimeOnly.ParseExact(r.GetString(3),"HH:mm",System.Globalization.CultureInfo.InvariantCulture)));}

        var generatedAtText=$"{DateTime.Now:yyyy/MM/dd HH:mm}／アプリ {ApplicationVersion.FromAssembly(typeof(SqliteOutputPackageService).Assembly).DisplayVersion}";
        return new(projectTitle,academicYear,seasonName,generatedAtText,startDate,endDate,openDates,slotDefinitions,rows,unassigned,absent,warnings,unavailabilities,groupLessonAttendances);
    }

    /// <summary>
    /// Python版unassigned_builder.pyのUnassignedRecord相当を、現在のC#が持つ診断情報（
    /// <see cref="SeminarSched.Infrastructure.Scheduling.SqliteScheduleEditorService.GetUnplacedSessionsAsync"/>
    /// と同じ3段階理由・候補コマ抽出クエリ）から組み立てる。Python版は候補ごとに現在の配置へ仮追加して
    /// 独立validatorで再検証するが、ここでは同じ候補生成条件（資格・空き時間・出勤不可・生徒衝突）を
    /// 満たす具体的な日時・講師の組をそのまま上位3件提示する簡略版とする。
    /// </summary>
    private static async Task<List<UnassignedRequestRow>> LoadUnassignedRequestsAsync(SqliteConnection c,CancellationToken token)
    {
        var baseRows=new List<(long Id,string Student,string Subject,int Required,int Placed,int Priority,string? RegularTeacher,bool OneToOne,string Note,long SubjectId)>();
        await using(var q=c.CreateCommand())
        {
            q.CommandText="""
                SELECT r.Id,st.Name,su.DisplayName,su.ShortName,su.Code,r.RequiredSessions,COUNT(a.Id),
                       COALESCE(NULLIF(r.RegularTeacherPriority,1),p.RegularTeacherPriority,1),te.Name,r.OneToOneRequired,r.Note,r.SubjectId
                FROM LessonRequest r
                JOIN Student st ON st.Id=r.StudentId
                JOIN Subject su ON su.Id=r.SubjectId
                LEFT JOIN RegularLessonProfile p ON p.ProjectId=r.ProjectId AND p.StudentId=r.StudentId AND p.SubjectId=r.SubjectId
                LEFT JOIN Teacher te ON te.Id=COALESCE(r.RegularTeacherId,p.RegularTeacherId)
                LEFT JOIN Assignment a ON a.LessonRequestId=r.Id
                GROUP BY r.Id HAVING r.RequiredSessions-COUNT(a.Id)>0
                ORDER BY st.ExternalId,su.SortOrder;
                """;
            await using var reader=await q.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
                baseRows.Add((reader.GetInt64(0),reader.GetString(1),SubjectAbbreviation.Resolve(reader.GetString(2),reader.GetString(3),reader.GetString(4)),reader.GetInt32(5),reader.GetInt32(6),reader.GetInt32(7),reader.IsDBNull(8)?null:WeeklyCalendarLayout.Surname(reader.GetString(8)),reader.GetBoolean(9),reader.GetString(10),reader.GetInt64(11)));
        }

        var qualifiedSubjects=new HashSet<long>();
        await using(var q=c.CreateCommand()){q.CommandText="SELECT DISTINCT SubjectId FROM TeacherQualification q JOIN Teacher t ON t.Id=q.TeacherId WHERE q.CanTeach=1 AND t.Active=1;";await using var reader=await q.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))qualifiedSubjects.Add(reader.GetInt64(0));}

        var withCollision=await CountAndListCandidatesAsync(c,includeStudentCollisionCheck:true,token);
        var withoutCollision=await CountAndListCandidatesAsync(c,includeStudentCollisionCheck:false,token);

        var result=new List<UnassignedRequestRow>();
        foreach(var row in baseRows)
        {
            var missing=row.Required-row.Placed;
            var withCollisionEntry=withCollision.GetValueOrDefault(row.Id);
            string mainReason;List<string> candidates=new();
            if(withCollisionEntry is{Count:>0})
            {
                mainReason="現在の時間割では未配置です";
                candidates=withCollisionEntry.Top.Select(x=>$"{DateOnly.Parse(x.Date):yyyy/MM/dd} {x.Slot} {x.Teacher}（単独配置可）").ToList();
            }
            else
            {
                var withoutCollisionCount=withoutCollision.GetValueOrDefault(row.Id)?.Count??0;
                mainReason=!qualifiedSubjects.Contains(row.SubjectId)
                    ?"この科目を担当できる講師が設定されていません。"
                    :withoutCollisionCount==0
                        ?"講師の空き時間・出勤可否の条件を満たすコマがありません。"
                        :"生徒の他の授業と重なるため配置できるコマがありません。";
            }
            result.Add(new(row.Student,row.Subject,row.Required,row.Placed,missing,mainReason,candidates,row.Priority,row.RegularTeacher,row.OneToOne,row.Note));
        }
        return result;
    }

    private sealed class CandidateAccumulator{public int Count;public List<(string Date,string Slot,string Teacher)> Top{get;}=new();}

    private static async Task<Dictionary<long,CandidateAccumulator>> CountAndListCandidatesAsync(SqliteConnection c,bool includeStudentCollisionCheck,CancellationToken token)
    {
        var result=new Dictionary<long,CandidateAccumulator>();
        await using var q=c.CreateCommand();
        q.CommandText=$"""
            SELECT r.Id,d.Date,ts.DisplayName||' '||ts.StartTime||'-'||ts.EndTime,t.Name
            FROM LessonRequest r
            JOIN CourseProject cp ON cp.Id=r.ProjectId
            JOIN TeacherQualification tq ON tq.SubjectId=r.SubjectId AND tq.CanTeach=1
            JOIN Teacher t ON t.Id=tq.TeacherId AND t.Active=1
            CROSS JOIN OpenDateTimeSlot ds
            JOIN OpenDate d ON d.Id=ds.OpenDateId AND d.IsOpen=1
            JOIN TimeSlot ts ON ts.Id=ds.TimeSlotId AND ts.Active=1
            LEFT JOIN StudentAvailability sa ON sa.ProjectId=r.ProjectId AND sa.StudentId=r.StudentId AND sa.OpenDateId=ds.OpenDateId AND sa.TimeSlotId=ds.TimeSlotId
            LEFT JOIN TeacherAvailability ta ON ta.ProjectId=r.ProjectId AND ta.TeacherId=tq.TeacherId AND ta.OpenDateId=ds.OpenDateId AND ta.TimeSlotId=ds.TimeSlotId
            WHERE (NOT EXISTS(SELECT 1 FROM StudentAvailability WHERE ProjectId=r.ProjectId AND StudentId=r.StudentId) OR COALESCE(sa.AvailabilityLevel,0)>0)
              AND (NOT EXISTS(SELECT 1 FROM TeacherAvailability WHERE ProjectId=r.ProjectId AND TeacherId=tq.TeacherId) OR COALESCE(ta.AvailabilityLevel,0)>0)
              AND NOT EXISTS(SELECT 1 FROM TeacherUnavailability u WHERE u.TeacherId=tq.TeacherId AND u.OpenDateId=ds.OpenDateId AND u.TimeSlotId=ds.TimeSlotId)
              {(includeStudentCollisionCheck?"AND NOT EXISTS(SELECT 1 FROM Assignment a JOIN LessonRequest ar ON ar.Id=a.LessonRequestId WHERE (a.IsLocked=1 OR a.IsManual=1) AND a.OpenDateId=ds.OpenDateId AND a.TimeSlotId=ds.TimeSlotId AND ar.StudentId=r.StudentId)":"")}
            ORDER BY r.Id,d.Date,ts.SortOrder;
            """;
        await using var reader=await q.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var id=reader.GetInt64(0);
            if(!result.TryGetValue(id,out var acc)){acc=new CandidateAccumulator();result[id]=acc;}
            acc.Count++;
            if(acc.Top.Count<3)acc.Top.Add((reader.GetString(1),reader.GetString(2),WeeklyCalendarLayout.Surname(reader.GetString(3))));
        }
        return result;
    }

    /// <summary>
    /// Python版issue_builder.pyの警告一覧相当。現時点のC#が検知できる警告種別は通常担当優先度の
    /// 目標割合未達（Python版5.3節）のみのため、そこからissue_type="通常担当不足"の警告行を組み立てる。
    /// Python版が持つ他の診断種別（日程競合・定員超過等）はまだ移植されていない。
    /// </summary>
    private static async Task<List<WarningRow>> LoadWarningsAsync(SqliteConnection c,CancellationToken token)
    {
        var warnings=new List<WarningRow>();
        await using var q=c.CreateCommand();
        q.CommandText="""
            SELECT s.Name,sub.DisplayName,sub.ShortName,sub.Code,te.Name,r.RequiredSessions,
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
            var student=reader.GetString(0);var subject=SubjectAbbreviation.Resolve(reader.GetString(1),reader.GetString(2),reader.GetString(3));var teacher=WeeklyCalendarLayout.Surname(reader.GetString(4));
            var required=reader.GetInt32(5);var priority=reader.GetInt32(6);var actual=reader.GetInt32(7);
            var target=Math.Max(0,((priority-1)*required+3)/4);
            if(actual<target)warnings.Add(new("警告","通常担当不足",null,null,student,teacher,$"{subject}：通常担当{teacher}　目標{target}回中{actual}回","未対応"));
        }
        return warnings;
    }
}
