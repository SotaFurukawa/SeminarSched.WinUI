using ClosedXML.Excel;
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
    public async Task GenerateAsync_CreatesAllFiveReportKindsAtomically()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"report.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var m=new SqliteMasterDataRepository();var st=await m.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var te=await m.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var sub=await m.SaveSubjectAsync(path,new Subject(0,"MATH","数学","数","中学",1));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(Id,ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment(Id,LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source) SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}

        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);
        foreach(var xlsx in new[]{result.OverallExcelPath,result.StudentHandoutsExcelPath,result.TeacherHandoutsExcelPath,result.IssuesExcelPath})
            Assert.True(new FileInfo(xlsx).Length>500,$"{xlsx} should be a non-trivial workbook.");
        foreach(var pdf in new[]{result.OverallPdfPath,result.StudentHandoutsPdfPath,result.TeacherHandoutsPdfPath,result.IssuesPdfPath})
        {
            var bytes=await File.ReadAllBytesAsync(pdf);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));
        }
        Assert.Empty(Directory.GetDirectories(_directory,"*.tmp-*"));

        using var overall=new XLWorkbook(result.OverallExcelPath);
        Assert.True(overall.Worksheets.Contains("出力情報"));
        Assert.Equal("季節講習時間割",overall.Worksheet("出力情報").Cell(1,2).GetString());
        Assert.Contains(overall.Worksheets,ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));

        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中2_架空 生徒");
        var studentCells=studentHandouts.Worksheet("中2_架空 生徒").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(studentCells,text=>text=="数");
        Assert.DoesNotContain(studentCells,text=>text.Contains("架空",StringComparison.Ordinal)&&text.Contains("数",StringComparison.Ordinal));

        using var teacherHandouts=new XLWorkbook(result.TeacherHandoutsExcelPath);
        var teacherCells=teacherHandouts.Worksheet("中2_架空 生徒").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(teacherCells,text=>text.Contains("数",StringComparison.Ordinal)&&text.Contains("架空",StringComparison.Ordinal));

        using var issues=new XLWorkbook(result.IssuesExcelPath);
        Assert.True(issues.Worksheets.Contains("未配置一覧"));Assert.True(issues.Worksheets.Contains("警告一覧"));
        Assert.Equal("生徒",issues.Worksheet("未配置一覧").Cell(1,1).GetString());
        Assert.Equal("severity",issues.Worksheet("警告一覧").Cell(1,1).GetString());

        Assert.True(Directory.Exists(result.TeacherPacketDirectory));
        var teacherPacket=Path.Combine(result.TeacherPacketDirectory,"架空 講師t.xlsx");
        Assert.True(File.Exists(teacherPacket));
        using var packet=new XLWorkbook(teacherPacket);
        Assert.Contains(packet.Worksheets,ws=>ws.Name.EndsWith("_講師別",StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_MultiWeekPeriod_RendersPerWeekOverviewSheetsAndAbsenceList()
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

        using var studentHandouts=new XLWorkbook(result.StudentHandoutsExcelPath);
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中2_田中 太郎");
        Assert.Contains(studentHandouts.Worksheets,ws=>ws.Name=="中1_田中 次郎");
        Assert.True(studentHandouts.Worksheets.Contains("講習欠席一覧"));
        var absenceCells=studentHandouts.Worksheet("講習欠席一覧").CellsUsed().Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(absenceCells,text=>text=="架空 欠席生徒");

        using var overall=new XLWorkbook(result.OverallExcelPath);
        var weekSheets=overall.Worksheets.Where(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal)).ToArray();
        Assert.True(weekSheets.Length>=2,"Expected at least one week sheet per week containing an assignment.");
        var overviewCells=weekSheets.SelectMany(ws=>ws.CellsUsed()).Select(cell=>cell.GetString()).ToArray();
        Assert.Contains(overviewCells,text=>text=="架空");
        Assert.Contains(overviewCells,text=>text=="数");
    }

    [Fact]
    public async Task GenerateAsync_ReportsRegularTeacherShortfallAsWarningRow()
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
        using var issues=new XLWorkbook(result.IssuesExcelPath);
        var warningSheet=issues.Worksheet("警告一覧");
        var warningRow=warningSheet.RowsUsed().Skip(1).First();
        Assert.Equal("通常担当不足",warningRow.Cell(2).GetString());
        Assert.Contains("架空 通常担当",warningRow.Cell(6).GetString());
        Assert.Contains("目標2回中0回",warningRow.Cell(7).GetString());

        Assert.True(Directory.Exists(result.TeacherPacketDirectory));
        var substituteFile=Path.Combine(result.TeacherPacketDirectory,"架空 代講t.xlsx");
        Assert.True(File.Exists(substituteFile));
        using var substituteWorkbook=new XLWorkbook(substituteFile);
        Assert.Contains(substituteWorkbook.Worksheets,ws=>ws.Name.EndsWith("_講師別",StringComparison.Ordinal));

        var substitutePdf=Path.Combine(result.TeacherPacketDirectory,"架空 代講t.pdf");
        Assert.True(File.Exists(substitutePdf));
        var pdfBytes=await File.ReadAllBytesAsync(substitutePdf);
        Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(pdfBytes,0,4));
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
        using var workbook=new XLWorkbook(result.OverallExcelPath);
        var overview=workbook.Worksheets.First(ws=>ws.Name.StartsWith("週_",StringComparison.Ordinal));
        // The project also carries the default A/B/C slots seeded by SqliteProjectRepository.CreateAsync
        // alongside this test's own "1限"/"2限" slots, so slot rows must be located by label text rather
        // than by an assumed row offset from one another.
        var slot1Row=overview.CellsUsed().First(cell=>cell.GetString()=="1限 09:00-10:00").Address.RowNumber;
        var slot2Row=overview.CellsUsed().First(cell=>cell.GetString()=="2限 10:00-11:00").Address.RowNumber;
        var teacherCol=overview.CellsUsed().First(cell=>cell.GetString()=="架空").Address.ColumnNumber;
        Assert.Equal("中2",overview.Cell(slot1Row,teacherCol).GetString());
        Assert.Equal(string.Empty,overview.Cell(slot2Row,teacherCol).GetString());
        Assert.Equal(XLColor.LightGray,overview.Cell(slot2Row,teacherCol).Style.Fill.BackgroundColor);
        Assert.NotEqual(XLColor.LightGray,overview.Cell(slot1Row,teacherCol).Style.Fill.BackgroundColor);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
