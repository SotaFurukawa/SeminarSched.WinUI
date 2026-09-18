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
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.RemoveManualAsync(state.Path,manual.Id));
        await editor.SetLockedAsync(state.Path,manual.Id,false);
        await editor.RemoveManualAsync(state.Path,manual.Id);Assert.Empty(await editor.GetAssignmentsAsync(state.Path));
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM AuditLog WHERE Action IN('manual_assignment_added','assignment_lock_changed','manual_assignment_removed');";Assert.Equal(4L,Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task GetAuditHistoryAsync_ReturnsSchedulingActionsNewestFirstWithJapaneseLabels()
    {
        var state=await CreateStateAsync();var editor=new SqliteScheduleEditorService();
        await editor.AddManualAsync(state.Path,state.RequestId,state.TeacherId,state.DateId,state.SlotId,true);
        var added=Assert.Single(await editor.GetAssignmentsAsync(state.Path));
        await editor.SetLockedAsync(state.Path,added.Id,false);

        var history=await editor.GetAuditHistoryAsync(state.Path);
        Assert.Equal(2,history.Count);
        Assert.Equal("ロック状態を変更",history[0].ActionLabel);
        Assert.Equal("事前確定として追加",history[1].ActionLabel);
    }

    [Fact]
    public async Task GetLabelSetAsync_ResolvesRequestTeacherDateAndSlotLabels()
    {
        var state=await CreateStateAsync();var editor=new SqliteScheduleEditorService();
        var labels=await editor.GetLabelSetAsync(state.Path);
        Assert.Contains("架空 編集生徒",labels.Request(state.RequestId));
        Assert.Contains("編集科目",labels.Request(state.RequestId));
        Assert.Contains("架空 編集講師",labels.Teacher(state.TeacherId));
        Assert.Contains("1限",labels.DateSlot(state.DateId,state.SlotId));
        Assert.StartsWith("#",labels.Teacher(-1));
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
    public async Task GetUnplacedSessionsAsync_HidesRequestsWithNoAvailableSlotOnTheSelectedDateAndOmitsStudentId()
    {
        var state=await CreateStateAsync();var editor=new SqliteScheduleEditorService();var master=new SqliteMasterDataRepository();

        var subjectNoTeacher=await master.SaveSubjectAsync(state.Path,new Subject(0,"JH_NOTEACH","無資格科目","無資格","中学校",2));
        long noTeacherRequestId;
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False"))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();
            command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,StudentId,$subject,1 FROM LessonRequest WHERE Id=$request;SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$subject",subjectNoTeacher.Id);command.Parameters.AddWithValue("$request",state.RequestId);
            noTeacherRequestId=Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        var subjectOther=await master.SaveSubjectAsync(state.Path,new Subject(0,"JH_OTHER","他科目","他","中学校",3));
        var teacherOther=await master.SaveTeacherAsync(state.Path,new Teacher(0,"T-OTHER","架空 他講師"));
        await master.SaveQualificationAsync(state.Path,new TeacherQualification(teacherOther.Id,subjectOther.Id,true));
        long otherRequestId;
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False"))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();
            command.CommandText="INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,StudentId,$subject,1 FROM LessonRequest WHERE Id=$request;SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$subject",subjectOther.Id);command.Parameters.AddWithValue("$request",state.RequestId);
            otherRequestId=Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        await editor.AddManualAsync(state.Path,otherRequestId,teacherOther.Id,state.DateId,state.SlotId,false);

        // noTeacherRequestId: no qualified teacher at all → never has an available slot.
        // otherRequestId: already placed → RequiredSessions-COUNT(a.Id)==0, so it's not "unplaced" at all.
        // state.RequestId: the student's own other lesson (otherRequestId) already occupies state.DateId/state.SlotId
        // (the fixture's only slot that day), so on that date there is no remaining slot for it either.
        // Net result: nothing is placeable on state.DateId, so the list is empty.
        var unplaced=await editor.GetUnplacedSessionsAsync(state.Path,state.DateId);
        Assert.Empty(unplaced);
    }

    [Fact]
    public async Task GetUnplacedSessionsAsync_OnlyListsSlotsWhereTheStudentIsActuallyAvailable()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();
        await using(var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False"))
        {
            await connection.OpenAsync();await using var command=connection.CreateCommand();
            command.CommandText="""
                INSERT INTO StudentAvailability(ProjectId,StudentId,OpenDateId,TimeSlotId,AvailabilityLevel)
                SELECT 1,r.StudentId,$date,$slot1,0 FROM LessonRequest r WHERE r.Id=$request;
                INSERT INTO StudentAvailability(ProjectId,StudentId,OpenDateId,TimeSlotId,AvailabilityLevel)
                SELECT 1,r.StudentId,$date,$slot2,2 FROM LessonRequest r WHERE r.Id=$request;
                """;
            command.Parameters.AddWithValue("$date",state.DateId);command.Parameters.AddWithValue("$slot1",state.Slot1Id);command.Parameters.AddWithValue("$slot2",state.Slot2Id);command.Parameters.AddWithValue("$request",state.RequestId);
            await command.ExecuteNonQueryAsync();
        }
        var unplaced=Assert.Single(await editor.GetUnplacedSessionsAsync(state.Path,state.DateId));
        var slotCode=Assert.Single(unplaced.AvailableSlotCodes);
        Assert.Equal("B2",slotCode);
    }

    [Fact]
    public async Task GetBoardAsync_PlacesCardsInCorrectCellAndListsUnplaced()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();
        var unplaced=Assert.Single(await editor.GetUnplacedSessionsAsync(state.Path,state.DateId));
        Assert.Equal(2,unplaced.AvailableSlotCodes.Count);Assert.Contains("B1",unplaced.AvailableSlotCodes);Assert.Contains("B2",unplaced.AvailableSlotCodes);
        Assert.DoesNotContain("S-",unplaced.StudentName);
        await editor.AddManualAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,false);
        Assert.Empty(await editor.GetUnplacedSessionsAsync(state.Path,state.DateId));
        var board=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.Equal(2,board.Slots.Count);Assert.Equal(2,board.Teachers.Count);
        var occupied=Assert.Single(board.Cell(state.Slot1Id,state.Teacher1Id)!.Cards);
        Assert.DoesNotContain("S-",occupied.StudentLabel);Assert.Contains("盤生徒",occupied.StudentLabel);
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
    public async Task PreviewMoveAsync_ReturnsGreenWhenNoSoftMetricWorsens()
    {
        var state=await CreateBoardStateAsync();
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:false,isManual:false);
        var editor=new SqliteScheduleEditorService();var assignment=Assert.Single(await editor.GetAssignmentsAsync(state.Path));
        var preview=await editor.PreviewMoveAsync(state.Path,assignment.Id,state.Teacher2Id,state.DateId,state.Slot2Id);
        Assert.Equal(EditDecision.Green,preview.Decision);
        Assert.Empty(preview.WorsenedDeltas);
    }

    [Fact]
    public async Task PreviewMoveAsync_ReturnsRedMessageForHardConstraintViolation()
    {
        var state=await CreateBoardStateAsync();
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:true,isManual:false);
        var editor=new SqliteScheduleEditorService();var locked=Assert.Single(await editor.GetAssignmentsAsync(state.Path));
        var preview=await editor.PreviewMoveAsync(state.Path,locked.Id,state.Teacher2Id,state.DateId,state.Slot2Id);
        Assert.Equal(EditDecision.Red,preview.Decision);
        Assert.False(preview.Allowed);
        Assert.Contains("ロック",preview.Message);
    }

    [Fact]
    public async Task MoveAsync_YellowRequiresConfirmSoftWarningsAndPersistsReason()
    {
        var state=await CreateBoardStateAsync();
        await SetPreferredTeacherAsync(state.Path,state.RequestId,state.Teacher1Id);
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:false,isManual:false);
        var editor=new SqliteScheduleEditorService();var assignment=Assert.Single(await editor.GetAssignmentsAsync(state.Path));

        var preview=await editor.PreviewMoveAsync(state.Path,assignment.Id,state.Teacher2Id,state.DateId,state.Slot2Id);
        Assert.Equal(EditDecision.Yellow,preview.Decision);
        Assert.Contains(preview.SoftDeltas,d=>d.Code=="preferred_teacher"&&d.Worsened);

        await Assert.ThrowsAsync<SoftWarningConfirmationRequiredException>(()=>editor.MoveAsync(state.Path,assignment.Id,state.Teacher2Id,state.DateId,state.Slot2Id));
        await editor.MoveAsync(state.Path,assignment.Id,state.Teacher2Id,state.DateId,state.Slot2Id,confirmSoftWarnings:true,reason:"テスト理由で確認");

        var after=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.Equal(state.Teacher2Id,after.TeacherId);
        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.CommandText="SELECT Reason FROM AuditLog WHERE Action='manual_assignment_moved';";
        Assert.Equal("テスト理由で確認",Convert.ToString(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task MoveAsync_DowngradesUnqualifiedTeacherToYellowAndAllowsConfirmedOverride()
    {
        var state=await CreateBoardStateAsync();
        await InsertRawAssignmentAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,isLocked:false,isManual:false);
        var master=new SqliteMasterDataRepository();
        var unqualifiedTeacher=await master.SaveTeacherAsync(state.Path,new Teacher(0,"T-BOARD3","架空 盤講師三"));
        var editor=new SqliteScheduleEditorService();var assignment=Assert.Single(await editor.GetAssignmentsAsync(state.Path));

        var preview=await editor.PreviewMoveAsync(state.Path,assignment.Id,unqualifiedTeacher.Id,state.DateId,state.Slot2Id);
        Assert.Equal(EditDecision.Yellow,preview.Decision);
        Assert.Contains("指導可能科目",preview.Message);
        Assert.Contains(preview.SoftDeltas,d=>d.Code=="qualification_override"&&d.Worsened);

        await Assert.ThrowsAsync<SoftWarningConfirmationRequiredException>(()=>editor.MoveAsync(state.Path,assignment.Id,unqualifiedTeacher.Id,state.DateId,state.Slot2Id));
        await editor.MoveAsync(state.Path,assignment.Id,unqualifiedTeacher.Id,state.DateId,state.Slot2Id,confirmSoftWarnings:true,reason:"資格外だが確認して配置");
        var after=Assert.Single(await editor.GetAssignmentsAsync(state.Path));Assert.Equal(unqualifiedTeacher.Id,after.TeacherId);
    }

    private static async Task SetPreferredTeacherAsync(string path,long requestId,long preferredTeacherId)
    {
        await using var connection=new SqliteConnection($"Data Source={path};Pooling=False");await connection.OpenAsync();
        await using var command=connection.CreateCommand();command.CommandText="UPDATE LessonRequest SET PreferredTeacher1Id=$teacher WHERE Id=$request;";
        command.Parameters.AddWithValue("$teacher",preferredTeacherId);command.Parameters.AddWithValue("$request",requestId);
        await command.ExecuteNonQueryAsync();
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

    [Fact]
    public async Task SetTeacherUnavailableManyAsync_AppliesAllPairsInOneCallAndRollsBackOnConflict()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();

        await editor.SetTeacherUnavailableManyAsync(state.Path,state.DateId,[(state.Teacher1Id,state.Slot1Id),(state.Teacher1Id,state.Slot2Id),(state.Teacher2Id,state.Slot1Id),(state.Teacher2Id,state.Slot2Id)],true);
        var board=await editor.GetBoardAsync(state.Path,state.DateId,[state.Teacher1Id,state.Teacher2Id]);
        Assert.True(board.Cell(state.Slot1Id,state.Teacher1Id)!.Blocked);Assert.True(board.Cell(state.Slot2Id,state.Teacher1Id)!.Blocked);
        Assert.True(board.Cell(state.Slot1Id,state.Teacher2Id)!.Blocked);Assert.True(board.Cell(state.Slot2Id,state.Teacher2Id)!.Blocked);

        await editor.SetTeacherUnavailableManyAsync(state.Path,state.DateId,[(state.Teacher1Id,state.Slot1Id),(state.Teacher1Id,state.Slot2Id),(state.Teacher2Id,state.Slot1Id),(state.Teacher2Id,state.Slot2Id)],false);
        await editor.AddManualAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>editor.SetTeacherUnavailableManyAsync(state.Path,state.DateId,[(state.Teacher2Id,state.Slot2Id),(state.Teacher1Id,state.Slot1Id)],true));
        var afterFailure=await editor.GetBoardAsync(state.Path,state.DateId,[state.Teacher1Id,state.Teacher2Id]);
        Assert.False(afterFailure.Cell(state.Slot2Id,state.Teacher2Id)!.Blocked);
    }

    [Fact]
    public async Task SnapshotRoundTrip_RestoresPriorAssignmentsAndUnavailability()
    {
        var state=await CreateBoardStateAsync();var editor=new SqliteScheduleEditorService();
        var empty=await editor.CaptureSnapshotAsync(state.Path);
        Assert.Empty(empty.Assignments);Assert.Empty(empty.TeacherUnavailabilities);

        await editor.AddManualAsync(state.Path,state.RequestId,state.Teacher1Id,state.DateId,state.Slot1Id,false);
        await editor.SetTeacherUnavailableAsync(state.Path,state.Teacher2Id,state.DateId,state.Slot2Id,true);
        var populated=await editor.CaptureSnapshotAsync(state.Path);
        Assert.Single(populated.Assignments);Assert.Single(populated.TeacherUnavailabilities);

        await editor.RestoreSnapshotAsync(state.Path,empty);
        Assert.Empty(await editor.GetAssignmentsAsync(state.Path));
        var boardAfterUndo=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.False(boardAfterUndo.Cell(state.Slot2Id,state.Teacher2Id)!.Blocked);

        await editor.RestoreSnapshotAsync(state.Path,populated);
        var restored=Assert.Single(await editor.GetAssignmentsAsync(state.Path));
        Assert.Equal(state.Teacher1Id,restored.TeacherId);Assert.Equal(populated.Assignments[0].Id,restored.Id);
        var boardAfterRedo=await editor.GetBoardAsync(state.Path,state.DateId,[]);
        Assert.True(boardAfterRedo.Cell(state.Slot2Id,state.Teacher2Id)!.Blocked);

        await using var connection=new SqliteConnection($"Data Source={state.Path};Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM AuditLog WHERE Action='schedule_snapshot_restored';";Assert.Equal(2L,Convert.ToInt64(await command.ExecuteScalarAsync()));
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
        // GetBoardAsyncは出勤可否データが一件も無い講師を編集画面に表示しない（資格だけ持ち越されて
        // 出勤可否を未設定のまま放置された講師が全日程に紛れ込む不具合の修正）。このfixtureの講師は
        // 明示的に「出勤可能」として登録し、通常どおり盤に表示されるようにする。
        await using(var availability=connection.CreateCommand())
        {
            availability.CommandText="INSERT INTO TeacherAvailability(ProjectId,TeacherId,OpenDateId,TimeSlotId,AvailabilityLevel) SELECT 1,t.Id,d.Id,ts.Id,2 FROM Teacher t CROSS JOIN OpenDate d CROSS JOIN TimeSlot ts WHERE t.Id IN($teacher1,$teacher2) AND ts.Id IN($slot1,$slot2);";
            availability.Parameters.AddWithValue("$teacher1",teacher1.Id);availability.Parameters.AddWithValue("$teacher2",teacher2.Id);availability.Parameters.AddWithValue("$slot1",slot1.Id);availability.Parameters.AddWithValue("$slot2",slot2.Id);
            await availability.ExecuteNonQueryAsync();
        }
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
