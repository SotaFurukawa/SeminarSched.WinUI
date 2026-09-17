using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteScheduleEditorServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ManualPlacement_IsValidatedAuditedAndPreservedByOptimization()
    {
        var state=await CreateStateAsync();var editor=new SqliteScheduleEditorService();await editor.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,false);
        var manual=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.True(manual.IsManual);Assert.False(manual.IsLocked);
        var optimized=await new SqliteScheduleRunService().RunAsync(state.Path,TimeSpan.FromSeconds(2));Assert.Equal(0,optimized.PlacedLessons);Assert.Equal(0,optimized.UnassignedLessons);manual=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.True(manual.IsManual);
        await editor.SetLockedAsync(state.Path,manual.Id,true);Assert.True(Assert.Single(await editor.GetAssignmentsAsync(state.Path)).IsLocked);
        await editor.RemoveManualAsync(state.Path,manual.Id);Assert.Empty(await editor.GetAssignmentsAsync(state.Path));
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM AuditLog WHERE Action IN('manual_assignment_added','assignment_lock_changed','manual_assignment_removed');";Assert.Equal(3L,Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ResetAutomatic_LeavesManualAndLockedAssignments()
    {
        var state=await CreateStateAsync();var editor=new SqliteScheduleEditorService();await editor.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,false);
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,StudentId,SubjectId,1 FROM LessonRequest LIMIT 1 ON CONFLICT(ProjectId,StudentId,SubjectId) DO NOTHING;INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) SELECT LessonRequestId,TeacherId,OpenDateId,TimeSlotId+100,0,'cp-sat',2,0 FROM Assignment LIMIT 1;";await Assert.ThrowsAsync<SqliteException>(async()=>await command.ExecuteNonQueryAsync());}
        // Insert an automatic row at a second valid slot so the reset target is realistic.
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False")){await connection.OpenAsync();await using var addSlot=connection.CreateCommand();addSlot.CommandText="INSERT INTO TimeSlot(Code,DisplayName,StartTime,EndTime,SortOrder,Active) VALUES('2','2限','10:10','11:10',2,1);INSERT INTO OpenDateTimeSlot(OpenDateId,TimeSlotId) SELECT $date,last_insert_rowid();INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) SELECT $request,$teacher,$date,Id,0,'cp-sat',2,0 FROM TimeSlot WHERE Code='2';";addSlot.Parameters.AddWithValue("$date",state.DateId);addSlot.Parameters.AddWithValue("$request",state.RequestId);addSlot.Parameters.AddWithValue("$teacher",state.TeacherId);await addSlot.ExecuteNonQueryAsync();}
        await editor.ResetAutomaticAsync(state.Path);var remaining=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.True(remaining.IsManual);
    }

    private async Task<State> CreateStateAsync(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-EDIT","架空 編集生徒","J2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-EDIT","架空 編集講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_EDIT","編集科目","編","中学校",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);SELECT last_insert_rowid();";command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",subject.Id);var request=Convert.ToInt64(await command.ExecuteScalarAsync());command.CommandText="SELECT Id FROM OpenDate LIMIT 1;";command.Parameters.Clear();var date=Convert.ToInt64(await command.ExecuteScalarAsync());return new State(path,request,teacher.Id,date,slot.Id);}
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId);
}
