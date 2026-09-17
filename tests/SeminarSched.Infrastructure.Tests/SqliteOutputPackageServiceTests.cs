using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Output;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteOutputPackageServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task GenerateAsync_CreatesExcelAndJapanesePdfAtomically()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"report.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();var st=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}
        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);Assert.True(File.Exists(result.ExcelPath));Assert.True(File.Exists(result.PdfPath));Assert.True(new FileInfo(result.ExcelPath).Length>1000);var bytes=await File.ReadAllBytesAsync(result.PdfPath);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));Assert.Empty(Directory.GetDirectories(_directory,"*.tmp-*"));
    }

    [Fact]
    public async Task GenerateAsync_MultiWeekPeriod_RendersWeeklyCalendarWithLabelsAndAbsenceList()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"multiweek.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,8,10)));
        var m=new SqliteMasterDataRepository();
        var s1=await m.SaveStudentAsync(path,new Student(0,"S-001","田中 太郎","中2"));
        var s2=await m.SaveStudentAsync(path,new Student(0,"S-002","田中 次郎","中1"));
        await m.SaveStudentAsync(path,new Student(0,"S-003","架空 欠席生徒","中3"));
        var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,8,3),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{s1.Id},{sub.Id},2),(2,1,{s2.Id},{sub.Id},1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-08-03';
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 2,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        Assert.True(new FileInfo(result.ExcelPath).Length>1000);
        var pdfBytes=await File.ReadAllBytesAsync(result.PdfPath);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(pdfBytes,0,4));

        using var workbook=new ClosedXML.Excel.XLWorkbook(result.ExcelPath);
        var studentSheets=workbook.Worksheets.Where(ws=>ws.Name.StartsWith("生徒",StringComparison.Ordinal)).ToArray();
        Assert.Contains(studentSheets,ws=>ws.Cell(1,1).GetString()=="中2 田中太");
        Assert.Contains(studentSheets,ws=>ws.Cell(1,1).GetString()=="中1 田中次");
        var issuesSheet=workbook.Worksheet("未配置・警告");
        Assert.Contains(issuesSheet.CellsUsed(),cell=>cell.GetString()=="架空 欠席生徒");
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
