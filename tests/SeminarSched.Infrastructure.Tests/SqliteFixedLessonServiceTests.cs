using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteFixedLessonServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task AddAsync_QualifiedTeacherAndOpenSlot_CreatesLockedAssignment()
    {
        var state=await CreateState();var service=new SqliteFixedLessonService();
        await service.AddAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId);
        var fixedLesson=Assert.Single(await service.GetFixedLessonsAsync(state.Path));Assert.Contains("架空 生徒",fixedLesson.Label);
        await service.RemoveAsync(state.Path,fixedLesson.Id);Assert.Empty(await service.GetFixedLessonsAsync(state.Path));
    }
    [Fact]
    public async Task AddAsync_SameStudentAndSlotTwice_IsRejected()
    {var state=await CreateState();var service=new SqliteFixedLessonService();await service.AddAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AddAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId));}
    private async Task<State> CreateState(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"fixed.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"CREATE TABLE IF NOT EXISTS LessonRequest(Id INTEGER PRIMARY KEY AUTOINCREMENT,ProjectId INTEGER NOT NULL,StudentId INTEGER NOT NULL,SubjectId INTEGER NOT NULL,RequiredSessions INTEGER NOT NULL,UNIQUE(ProjectId,StudentId,SubjectId));INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},2);SELECT last_insert_rowid();";var request=Convert.ToInt64(await q.ExecuteScalarAsync());await using var d=c.CreateCommand();d.CommandText="SELECT Id FROM OpenDate LIMIT 1";var date=Convert.ToInt64(await d.ExecuteScalarAsync());return new(path,request,teacher.Id,date,slot.Id);}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId);
}
