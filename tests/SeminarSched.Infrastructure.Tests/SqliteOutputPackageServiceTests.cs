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
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"CREATE TABLE LessonRequest(Id INTEGER PRIMARY KEY,ProjectId INTEGER,StudentId INTEGER,SubjectId INTEGER,RequiredSessions INTEGER);CREATE TABLE Assignment(Id INTEGER PRIMARY KEY,LessonRequestId INTEGER,TeacherId INTEGER,OpenDateId INTEGER,TimeSlotId INTEGER,IsLocked INTEGER,Source TEXT);INSERT INTO LessonRequest VALUES(1,1,{st.Id},{sub.Id},1);INSERT INTO Assignment SELECT 1,1,{te.Id},d.Id,{slot.Id},0,'test' FROM OpenDate d LIMIT 1;";await q.ExecuteNonQueryAsync();}
        var result=await new SqliteOutputPackageService().GenerateAsync(path,_directory);Assert.True(File.Exists(result.ExcelPath));Assert.True(File.Exists(result.PdfPath));Assert.True(new FileInfo(result.ExcelPath).Length>1000);var bytes=await File.ReadAllBytesAsync(result.PdfPath);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));Assert.Empty(Directory.GetDirectories(_directory,"*.tmp-*"));
    }
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
