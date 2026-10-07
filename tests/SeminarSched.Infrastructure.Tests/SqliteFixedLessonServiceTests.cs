using Microsoft.Data.Sqlite;
using SeminarSched.Application.Scheduling;
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
        await Assert.ThrowsAsync<SoftWarningConfirmationRequiredException>(()=>service.AddManualAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId,true));
        await service.AddManualAsync(state.Path,secondRequest,state.TeacherId,state.DateId,state.SlotId,true,confirmSoftWarnings:true);
        Assert.Equal(2,(await service.GetAssignmentsAsync(state.Path)).Count);
    }
    // ユーザー要望（checkpoint152）の入れ替え提案機能用。1講師・2コマ・1対1必須の受講希望2件という
    // 最小構成で、「既存の自動配置（IsManual=0）を別のコマへ動かせば、空いたコマへ未配置の受講希望を
    // 置ける」という状況を作る。
    [Fact]
    public async Task FindSwapSuggestionsAsync_SingleMoveFreesSlotForUnplacedDemand_ReturnsSuggestionAndApplySucceeds()
    {
        var state=await CreateSwapState();var service=new SqliteScheduleEditorService();
        var suggestion=Assert.Single(await service.FindSwapSuggestionsAsync(state.Path));
        Assert.Equal(state.UnassignedRequestId,suggestion.UnassignedRequestId);
        Assert.Equal(state.OccupyingAssignmentId,suggestion.MovingAssignmentId);
        Assert.Equal(state.TeacherId,suggestion.FreedTeacherId);Assert.Equal(state.DateId,suggestion.FreedOpenDateId);Assert.Equal(state.Slot1Id,suggestion.FreedTimeSlotId);
        Assert.Equal(state.TeacherId,suggestion.NewTeacherId);Assert.Equal(state.DateId,suggestion.NewOpenDateId);Assert.Equal(state.Slot2Id,suggestion.NewTimeSlotId);
        await service.ApplySwapSuggestionAsync(state.Path,suggestion);
        var assignments=await service.GetAssignmentsAsync(state.Path);
        Assert.Equal(2,assignments.Count);
        Assert.Contains(assignments,a=>a.Id==state.OccupyingAssignmentId&&a.TimeSlotId==state.Slot2Id);
        Assert.Contains(assignments,a=>a.LessonRequestId==state.UnassignedRequestId&&a.TimeSlotId==state.Slot1Id);
    }
    [Fact]
    public async Task FindSwapSuggestionsAsync_AllOccupyingAssignmentsAreManualOrLocked_NoSuggestion()
    {
        var state=await CreateSwapState();
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();
        await using(var command=connection.CreateCommand()){command.CommandText="UPDATE Assignment SET IsManual=1 WHERE Id=$id;";command.Parameters.AddWithValue("$id",state.OccupyingAssignmentId);await command.ExecuteNonQueryAsync();}
        var master=new SqliteMasterDataRepository();var student3=await master.SaveStudentAsync(state.Path,new Student(0,"S-103","架空","生徒三","中2"));
        await using var requestCommand=connection.CreateCommand();requestCommand.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,OneToOneRequired) VALUES(1,$student,$subject,1,1);SELECT last_insert_rowid();";requestCommand.Parameters.AddWithValue("$student",student3.Id);requestCommand.Parameters.AddWithValue("$subject",state.SubjectId);var request3=Convert.ToInt64(await requestCommand.ExecuteScalarAsync());
        await using var assignCommand=connection.CreateCommand();assignCommand.CommandText="INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) VALUES($request,$teacher,$date,$slot,1,'manual',1,1);";assignCommand.Parameters.AddWithValue("$request",request3);assignCommand.Parameters.AddWithValue("$teacher",state.TeacherId);assignCommand.Parameters.AddWithValue("$date",state.DateId);assignCommand.Parameters.AddWithValue("$slot",state.Slot2Id);await assignCommand.ExecuteNonQueryAsync();
        var service=new SqliteScheduleEditorService();
        Assert.Empty(await service.FindSwapSuggestionsAsync(state.Path));
    }
    [Fact]
    public async Task FindSwapSuggestionsAsync_NoAlternateSlotForMovingAssignment_NoSuggestion()
    {
        var state=await CreateSwapState();
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.CommandText="DELETE FROM OpenDateTimeSlot WHERE OpenDateId=$date AND TimeSlotId=$slot;";command.Parameters.AddWithValue("$date",state.DateId);command.Parameters.AddWithValue("$slot",state.Slot2Id);await command.ExecuteNonQueryAsync();
        var service=new SqliteScheduleEditorService();
        Assert.Empty(await service.FindSwapSuggestionsAsync(state.Path));
    }
    private async Task<SwapState> CreateSwapState()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"swap.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();
        var student1=await master.SaveStudentAsync(path,new Student(0,"S-101","架空","生徒一","中2"));
        var student2=await master.SaveStudentAsync(path,new Student(0,"S-102","架空","生徒二","中2"));
        var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-101","架空","講師"));
        var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));
        await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));
        var course=new SqliteCourseSettingsRepository();
        var slot1=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        var slot2=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"2","2限",new TimeOnly(10,0),new TimeOnly(11,0),2));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot1.Id,slot2.Id]));
        await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();
        await using var dateCommand=connection.CreateCommand();dateCommand.CommandText="SELECT Id FROM OpenDate LIMIT 1;";var dateId=Convert.ToInt64(await dateCommand.ExecuteScalarAsync());
        async Task<long> InsertRequestAsync(long studentId)
        {
            await using var command=connection.CreateCommand();
            command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions,OneToOneRequired) VALUES(1,$student,$subject,1,1);SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$student",studentId);command.Parameters.AddWithValue("$subject",subject.Id);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        var occupyingRequestId=await InsertRequestAsync(student1.Id);
        var unassignedRequestId=await InsertRequestAsync(student2.Id);
        await using var assignCommand=connection.CreateCommand();
        assignCommand.CommandText="INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) VALUES($request,$teacher,$date,$slot,0,'auto',1,0);SELECT last_insert_rowid();";
        assignCommand.Parameters.AddWithValue("$request",occupyingRequestId);assignCommand.Parameters.AddWithValue("$teacher",teacher.Id);assignCommand.Parameters.AddWithValue("$date",dateId);assignCommand.Parameters.AddWithValue("$slot",slot1.Id);
        var occupyingAssignmentId=Convert.ToInt64(await assignCommand.ExecuteScalarAsync());
        return new(path,teacher.Id,dateId,slot1.Id,slot2.Id,subject.Id,occupyingAssignmentId,unassignedRequestId);
    }
    private sealed record SwapState(string Path,long TeacherId,long DateId,long Slot1Id,long Slot2Id,long SubjectId,long OccupyingAssignmentId,long UnassignedRequestId);
    private async Task<State> CreateState(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"fixed.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-001","架空","生徒","中2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-001","架空","講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_MATH","数学","数","中学",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var c=new SqliteConnection($"Data Source={path};Pooling=False");await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,{student.Id},{subject.Id},2);SELECT last_insert_rowid();";var request=Convert.ToInt64(await q.ExecuteScalarAsync());await using var d=c.CreateCommand();d.CommandText="SELECT Id FROM OpenDate LIMIT 1";var date=Convert.ToInt64(await d.ExecuteScalarAsync());return new(path,request,teacher.Id,date,slot.Id,subject.Id);}
    private static async Task<long> AddRequest(State state,string externalId,string name){var spaceIndex=name.IndexOf(' ');var student=await new SqliteMasterDataRepository().SaveStudentAsync(state.Path,new Student(0,externalId,name[..spaceIndex],name[(spaceIndex+1)..],"中2"));await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,2);SELECT last_insert_rowid();";command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",state.SubjectId);return Convert.ToInt64(await command.ExecuteScalarAsync());}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId,long SubjectId);
}
