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

    [Fact]
    public async Task GetBoardAsync_PlacesCardsInCorrectCellAndListsUnplaced()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();
        Assert.Single(await editor.GetUnplacedSessionsAsync(state.Path));
        await editor.AddManualAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,false);
        Assert.Empty(await editor.GetUnplacedSessionsAsync(state.Path));
        var board=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.Equal(2,board.Slots.Count);Assert.Equal(2,board.Teachers.Count);
        var occupied=Assert.Single(board.Cell(state.Slot1Id,state.Teacher1Id)!.Cards);
        Assert.StartsWith("S-BOARD",occupied.StudentLabel);Assert.Contains("盤生徒",occupied.StudentLabel);
        Assert.Empty(board.Cell(state.Slot2Id,state.Teacher2Id)!.Cards);
        Assert.False(board.Cell(state.Slot1Id,state.Teacher1Id)!.Blocked);
    }

    [Fact]
    public async Task MoveAsync_RelocatesAssignmentAndMarksManual()
    {
        var state=await CreateBoardStateAsync();
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:false,isManual:false);
        var editor=new SqliteScheduleEditorService();var before=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.False(before.IsManual);
        await editor.MoveAsync(state.Path,before.Id,state.Teacher2Id,state.DateId,state.Slot2Id);
        var after=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.True(after.IsManual);Assert.Equal(state.Teacher2Id,after.TeacherId);Assert.Equal(state.Slot2Id,after.TimeSlotId);
        var board=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.Empty(board.Cell(state.Slot1Id,state.Teacher1Id)!.Cards);Assert.Single(board.Cell(state.Slot2Id,state.Teacher2Id)!.Cards);
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM AuditLog WHERE Action='manual_assignment_moved';";Assert.Equal(1L,Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task MoveAsync_RejectsLockedSameCellAndStudentCollision()
    {
        var state=await CreateBoardStateAsync();
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:true,isManual:false);
        var editor=new SqliteScheduleEditorService();var locked=Assert.Single(await editor.GetAssignmentsAsync(state.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.MoveAsync(state.Path,locked.Id,state.Teacher2Id,state.DateId,state.Slot2Id));
        await editor.SetLockedAsync(state.Path,locked.Id,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.MoveAsync(state.Path,locked.Id,state.Teacher1Id,state.DateId,state.Slot1Id));

        var master=new SqliteMasterDataRepository();
        var subject2=await master.SaveSubjectAsync(state.Path,new Subject(0,"JH_BOARD2","盤科目二","盤二","中学校",2));
        await master.SaveQualificationAsync(state.Path,new TeacherQualification(state.Teacher2Id,subject2.Id,true));
        long secondRequest;await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,StudentId,$subject,1 FROM LessonRequest WHERE Id=$request;SELECT last_insert_rowid();";command.Parameters.AddWithValue("$subject",subject2.Id);command.Parameters.AddWithValue("$request",state.RequestId);secondRequest=Convert.ToInt64(await command.ExecuteScalarAsync());}
        await editor.AddManualAsync(state.Path,secondRequest,state.Teacher2Id,state.DateId,state.Slot2Id,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.MoveAsync(state.Path,locked.Id,state.Teacher1Id,state.DateId,state.Slot2Id));
    }

    [Fact]
    public async Task SetTeacherUnavailableAsync_HidesFullyBlockedTeacherAndRejectsWhenOccupied()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();
        await editor.SetTeacherUnavailableAsync(state.Path,state.Teacher2Id,state.DateId,state.Slot1Id,true);
        await editor.SetTeacherUnavailableAsync(state.Path,state.Teacher2Id,state.DateId,state.Slot2Id,true);
        var board=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.DoesNotContain(board.Teachers,t=>t.TeacherId==state.Teacher2Id);
        var forced=await editor.GetBoardAsync(state.Path,state.DateId,[state.Teacher2Id]);
        Assert.Contains(forced.Teachers,t=>t.TeacherId==state.Teacher2Id);
        Assert.True(forced.Cell(state.Slot1Id,state.Teacher2Id)!.Blocked);

        await editor.SetTeacherUnavailableAsync(state.Path,state.Teacher2Id,state.DateId,state.Slot1Id,false);
        var restored=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.Contains(restored.Teachers,t=>t.TeacherId==state.Teacher2Id);
        Assert.False(restored.Cell(state.Slot1Id,state.Teacher2Id)!.Blocked);

        await editor.AddManualAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.SetTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.Slot1Id,true));
    }

    private static async Task InsertRawAssignmentAsync(string path,long requestId,long teacherId,long openDateId,long timeSlotId,bool isLocked,bool isManual)
    {
        await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();
        await using var command=connection.CreateCommand();
        command.CommandText="INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,IsLocked,Source,SessionIndex,IsManual) VALUES($request,$teacher,$date,$slot,$locked,'cp-sat',1,$manual);";
        command.Parameters.AddWithValue("$request",requestId);command.Parameters.AddWithValue("$teacher",teacherId);command.Parameters.AddWithValue("$date",openDateId);command.Parameters.AddWithValue("$slot",timeSlotId);command.Parameters.AddWithValue("$locked",isLocked);command.Parameters.AddWithValue("$manual",isManual);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<State> CreateStateAsync(){Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));var master=new SqliteMasterDataRepository();var student=await master.SaveStudentAsync(path,new Student(0,"S-EDIT","架空 編集生徒","J2"));var teacher=await master.SaveTeacherAsync(path,new Teacher(0,"T-EDIT","架空 編集講師"));var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_EDIT","編集科目","編","中学校",1));await master.SaveQualificationAsync(path,new TeacherQualification(teacher.Id,subject.Id,true));var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);SELECT last_insert_rowid();";command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",subject.Id);var request=Convert.ToInt64(await command.ExecuteScalarAsync());command.CommandText="SELECT Id FROM OpenDate LIMIT 1;";command.Parameters.Clear();var date=Convert.ToInt64(await command.ExecuteScalarAsync());return new State(path,request,teacher.Id,date,slot.Id);}

    private async Task<BoardState> CreateBoardStateAsync()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();
        var student=await master.SaveStudentAsync(path,new Student(0,"S-BOARD","架空 盤生徒","J2"));
        var teacher1=await master.SaveTeacherAsync(path,new Teacher(0,"T-BOARD1","架空 盤講師一"));
        var teacher2=await master.SaveTeacherAsync(path,new Teacher(0,"T-BOARD2","架空 盤講師二"));
        var subject=await master.SaveSubjectAsync(path,new Subject(0,"JH_BOARD","盤科目","盤","中学校",1));
        await master.SaveQualificationAsync(path,new TeacherQualification(teacher1.Id,subject.Id,true));
        await master.SaveQualificationAsync(path,new TeacherQualification(teacher2.Id,subject.Id,true));
        var course=new SqliteCourseSettingsRepository();
        var slot1=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"B1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        var slot2=await course.SaveTimeSlotAsync(path,new TimeSlot(0,"B2","2限",new TimeOnly(10,10),new TimeOnly(11,10),2));
        await course.SaveCourseDayAsync(path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot1.Id,slot2.Id]));
        await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) VALUES(1,$student,$subject,1);SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$student",student.Id);command.Parameters.AddWithValue("$subject",subject.Id);
        var request=Convert.ToInt64(await command.ExecuteScalarAsync());
        command.CommandText="SELECT Id FROM OpenDate LIMIT 1;";command.Parameters.Clear();
        var date=Convert.ToInt64(await command.ExecuteScalarAsync());
        return new BoardState(path,request,teacher1.Id,teacher2.Id,date,slot1.Id,slot2.Id);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long RequestId,long TeacherId,long DateId,long SlotId);
    private sealed record BoardState(string Path,long RequestId,long Teacher1Id,long Teacher2Id,long DateId,long Slot1Id,long Slot2Id);
}
