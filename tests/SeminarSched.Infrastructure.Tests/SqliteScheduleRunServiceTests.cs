using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteScheduleRunServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task RunAsync_BuildsValidCandidatesAndPersistsSolverResult()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"solver.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var c=new SqliteConnection($"Data Source={path};Pooling=False")){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},1);";await q.ExecuteNonQueryAsync();}
        var result=await new SqliteScheduleRunService().RunAsync(path,TimeSpan.FromSeconds(5));Assert.Equal(1,result.PlacedLessons);Assert.Equal(0,result.UnassignedLessons);
        await using var verify=new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");await verify.OpenAsync();await using var count=verify.CreateCommand();count.CommandText="SELECT COUNT(*) FROM Assignment WHERE Source='cp-sat';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
        count.CommandText="SELECT COUNT(*) FROM OptimizationRun WHERE Status='completed' AND UnassignedCount=0;";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
        count.CommandText="SELECT SessionIndex FROM Assignment WHERE Source='cp-sat';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
    }
    [Fact]
    public async Task RunAsync_PreservesUnlockedManualPlacement()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"manual.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-002","架空 手動生徒","中1"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-002","架空 手動講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_ENG","英語","英","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        await using(var connection=new SqliteConnection($"Data Source={path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},1);INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) SELECT last_insert_rowid(),{teacher.Id},Id,{slot.Id},0,'manual',1,1 FROM OpenDate LIMIT 1;";await command.ExecuteNonQueryAsync();}
        var result=await new SqliteScheduleRunService().RunAsync(path,TimeSpan.FromSeconds(2));Assert.Equal(0,result.PlacedLessons);Assert.Equal(0,result.UnassignedLessons);
        await using var verify=new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");await verify.OpenAsync();await using var count=verify.CreateCommand();count.CommandText="SELECT COUNT(*) FROM Assignment WHERE IsManual=1 AND IsLocked=0 AND Source='manual';";Assert.Equal(1L,Convert.ToInt64(await count.ExecuteScalarAsync()));
    }
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
