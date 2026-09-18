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

        var overviewSheet=workbook.Worksheet("全体時間割");
        var overviewCells=overviewSheet.CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(overviewCells,text=>text.Contains("架空 講師"));
        Assert.Contains(overviewCells,text=>text.Contains("中2 数 田中太")||text.Contains("中1 数 田中次"));
        Assert.DoesNotContain(workbook.Worksheets,ws=>ws.Name=="時間割");
    }

    [Fact]
    public async Task GenerateAsync_ReportsRegularTeacherShortfallBelowTargetPercentage()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"shortfall.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,21)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var regular=await m.SaveTeacherAsync(path,new Teacher(0,"T-REG","架空 通常担当"));
        var substitute=await m.SaveTeacherAsync(path,new Teacher(0,"T-SUB","架空 代講"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        await m.SaveQualificationAsync(path,new TeacherQualification(regular.Id,sub.Id,true));
        await m.SaveQualificationAsync(path,new TeacherQualification(substitute.Id,sub.Id,true));
        await m.SaveRegularLessonAsync(path,new RegularLessonProfile(0,student.Id,sub.Id,regular.Id,regularTeacherPriority:5));
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,21),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},2);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{substitute.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var workbook=new ClosedXML.Excel.XLWorkbook(result.ExcelPath);
        var issuesSheet=workbook.Worksheet("未配置・警告");
        Assert.Contains(issuesSheet.CellsUsed(),cell=>cell.GetString().Contains("通常担当架空 通常担当")&&cell.GetString().Contains("目標2回中0回"));

        Assert.True(Directory.Exists(result.TeacherPacketDirectory));
        var substituteFile=Path.Combine(result.TeacherPacketDirectory,"架空 代講t.xlsx");
        Assert.True(File.Exists(substituteFile));
        using var substituteWorkbook=new ClosedXML.Excel.XLWorkbook(substituteFile);
        var roster=substituteWorkbook.Worksheet("担当一覧");
        Assert.Contains(roster.CellsUsed(),cell=>cell.GetString()=="講習担当");
        Assert.Contains(roster.CellsUsed(),cell=>cell.GetString().Contains("架空") && cell.GetString().Contains("数"));
        Assert.True(substituteWorkbook.Worksheets.Contains("時間割"));
    }

    [Fact]
    public async Task GenerateAsync_OverviewGrid_GraysOutUnavailableSlotForTeacherWithOtherAssignmentsThatDay()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"unavailable.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();
        var student=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));
        var teacher=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));
        var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();
        var slot1=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        var slot2=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"2","2限",new TimeOnly(10,0),new TimeOnly(11,0),2));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot1.Id,slot2.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();
            q.CommandText=$"""
                INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{student.Id},{sub.Id},1);
                INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source)
                  SELECT 1,{teacher.Id},d.Id,{slot1.Id},0,'test' FROM OpenDate d WHERE d.Date='2026-07-20';
                INSERT INTO TeacherUnavailability(TeacherId,OpenDateId,TimeSlotId)
                  SELECT {teacher.Id},d.Id,{slot2.Id} FROM OpenDate d WHERE d.Date='2026-07-20';
                """;
            await q.ExecuteNonQueryAsync();
        }

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        using var workbook=new ClosedXML.Excel.XLWorkbook(result.ExcelPath);
        var overview=workbook.Worksheet("全体時間割");
        var slot1Row=overview.CellsUsed().First(cell=>cell.GetString()=="1限 09:00-10:00").Address.RowNumber;
        var slot2Row=overview.CellsUsed().First(cell=>cell.GetString()=="2限 10:00-11:00").Address.RowNumber;
        var col=overview.CellsUsed().First(cell=>cell.GetString()=="架空 講師").Address.ColumnNumber;
        Assert.Contains("架空",overview.Cell(slot1Row,col).GetString());
        Assert.Equal(string.Empty,overview.Cell(slot2Row,col).GetString());
        Assert.Equal(ClosedXML.Excel.XLColor.LightGray,overview.Cell(slot2Row,col).Style.Fill.BackgroundColor);
        Assert.NotEqual(ClosedXML.Excel.XLColor.LightGray,overview.Cell(slot1Row,col).Style.Fill.BackgroundColor);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
