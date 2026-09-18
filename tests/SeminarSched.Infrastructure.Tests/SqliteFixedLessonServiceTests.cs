using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

/// <summary>
/// Python版と同様、事前確定（旧IFixedLessonService.AddAsync）は専用の永続状態を持たず、
/// IScheduleEditorService.AddManualAsync(isLocked:true)と同じ「手動配置＋ロック」として
/// 保存される。ここでは検証ロジック（重複・1対1席消費など）がそのまま維持されていることを確認する。
/// </summary>
public sealed class SqliteFixedLessonServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task AddManualAsync_Locked_QualifiedTeacherAndOpenSlot_CreatesLockedManualAssignment()
    {
        var state=await CreateState();var service=new SqliteScheduleEditorService();
        await service.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true);
        var assignment=Assert.Single(await service.GetAssignmentsAsync(state.Path));
        Assert.True(assignment.IsManual);Assert.True(assignment.IsLocked);Assert.Contains("架空 生徒",assignment.Label);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RemoveManualAsync(state.Path,assignment.Id));
        await service.SetLockedAsync(state.Path,assignment.Id,false);
        await service.RemoveManualAsync(state.Path,assignment.Id);
        Assert.Empty(await service.GetAssignmentsAsync(state.Path));
    }
    [Fact]
    public async Task AddManualAsync_SameStudentAndSlotTwice_IsRejected()
    {var state=await CreateState();var service=new SqliteScheduleEditorService();await service.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true));}
    [Fact]
    public async Task AddManualAsync_TwoPairableStudentsForOneTeacherAndSlot_AreAllowed()
    {
        var state=await CreateState();var secondRequest=await AddRequest(state,"S-002","架空 生徒二");var service=new SqliteScheduleEditorService();
        await service.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true);
        await service.AddManualAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId,true);
        Assert.Equal(2,(await service.GetAssignmentsAsync(state.Path)).Count);
    }
    [Fact]
    public async Task AddManualAsync_OneToOneLesson_ConsumesBothTeacherSeats()
    {
        var state=await CreateState();var secondRequest=await AddRequest(state,"S-003","架空 生徒三");
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="UPDATE LessonRequest SET OneToOneRequired=1 WHERE Id=$id;";command.Parameters.AddWithValue("$id",state.RequestId);await command.ExecuteNonQueryAsync();}
        var service=new SqliteScheduleEditorService();await service.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AddManualAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId,true));
    }
    private async Task<State> CreateState(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"fixed.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空 生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空 講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},2);SELECT last_insert_rowid();";var request=Convert.ToInt64(await q.ExecuteScalarAsync());await using var d=c.CreateCommand();d.CommandText="SELECT Id FROM OpenDate LIMIT 1";var date=Convert.ToInt64(await d.ExecuteScalarAsync());return new(path,request,teacher.Id,date,slot.Id,subject.Id);}
    private static async Task<long> AddRequest(State state,string externalId,string name){var student=await new SqliteMasterDataRepository().SaveStudentAsync(state.Path,new Student(0,externalId,name,"中2"));await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,2);SELECT last_insert_rowid();";command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",state.SubjectId);return Convert.ToInt64(await command.ExecuteScalarAsync());}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId,long SubjectId);
}
