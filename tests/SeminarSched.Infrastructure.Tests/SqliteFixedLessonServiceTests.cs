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
    [Fact]
    public async Task AddAsync_TwoPairableStudentsForOneTeacherAndSlot_AreAllowed()
    {
        var state=await CreateState();var secondRequest=await AddRequest(state,"S-002","架空 生徒二");var service=new SqliteFixedLessonService();
        await service.AddAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId);
        await service.AddAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId);
        Assert.Equal(2,(await service.GetFixedLessonsAsync(state.Path)).Count);
    }
    [Fact]
    public async Task AddAsync_OneToOneLesson_ConsumesBothTeacherSeats()
    {
        var state=await CreateState();var secondRequest=await AddRequest(state,"S-003","架空 生徒三");
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="UPDATE LessonRequest SET OneToOneRequired=1 WHERE Id=$id;";command.Parameters.AddWithValue("$id",state.RequestId);await command.ExecuteNonQueryAsync();}
        var service=new SqliteFixedLessonService();await service.AddAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AddAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId));
    }
    private async Task<State> CreateState(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"fixed.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},2);SELECT last_insert_rowid();";var request=Convert.ToInt64(await q.ExecuteScalarAsync());await using var d=c.CreateCommand();d.CommandText="SELECT Id FROM OpenDate LIMIT 1";var date=Convert.ToInt64(await d.ExecuteScalarAsync());return new(path,request,teacher.Id,date,slot.Id,subject.Id);}
    private static async Task<long> AddRequest(State state,string externalId,string name){var student=await new SqliteMasterDataRepository().SaveStudentAsync(state.Path,new Student(0,externalId,name,"中2"));await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,2);SELECT last_insert_rowid();";command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",state.SubjectId);return Convert.ToInt64(await command.ExecuteScalarAsync());}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId,long SubjectId);
}
