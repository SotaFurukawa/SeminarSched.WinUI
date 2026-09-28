using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.GroupLessons;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.GroupLessons;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteGroupLessonServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveClassAsync_CreatesUpdatesAndRejectsDuplicateName()
    {
        var state=await CreateStateAsync();var service=new SqliteGroupLessonService();
        var created=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));
        Assert.True(created.Id>0);
        var listed=Assert.Single(await service.GetClassesAsync(state.Path));
        Assert.Equal("中2A",listed.Name);Assert.Equal("中2",listed.Grade);Assert.Equal("数学",listed.Subject);Assert.False(listed.AllowOtherGrades);

        var renamed=await service.SaveClassAsync(state.Path,new GroupLessonClass(created.Id,"中2B","中2","英語",allowOtherGrades:true));
        Assert.Equal(created.Id,renamed.Id);
        var afterRename=Assert.Single(await service.GetClassesAsync(state.Path));
        Assert.Equal("中2B",afterRename.Name);Assert.Equal("英語",afterRename.Subject);Assert.True(afterRename.AllowOtherGrades);

        await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"別クラス","中1","数学"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2B","中1","数学")));
    }

    [Fact]
    public async Task DeleteClassAsync_CascadesSessionsAndEnrollments()
    {
        var state=await CreateStateAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));
        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(17,10),new TimeOnly(18,30));
        await service.SetEnrollmentAsync(state.Path,cls.Id,state.Student1Id,true);

        await service.DeleteClassAsync(state.Path,cls.Id);

        Assert.Empty(await service.GetClassesAsync(state.Path));
        Assert.Empty(await service.GetAllSessionsAsync(state.Path));
    }

    [Fact]
    public async Task GetCalendarDatesAsync_ReturnsAllOpenDatesInProjectRange()
    {
        var state=await CreateStateAsync();var service=new SqliteGroupLessonService();
        var dates=await service.GetCalendarDatesAsync(state.Path);
        var date=Assert.Single(dates);
        Assert.Equal(state.DateId,date.OpenDateId);Assert.Equal(new DateOnly(2026,7,20),date.Date);
    }

    [Fact]
    public async Task AddSessionsAsync_AddsAcrossMultipleDatesAndIgnoresExactDuplicatesThenRemoves()
    {
        var state=await CreateStateAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));

        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(17,10),new TimeOnly(18,30));
        var session=Assert.Single(await service.GetAllSessionsAsync(state.Path));
        Assert.Equal(state.DateId,session.OpenDateId);Assert.Equal(cls.Name,session.ClassName);Assert.Equal(cls.Subject,session.ClassSubject);Assert.Equal("17:10～18:30",session.TimeRangeLabel);

        // 同じクラス・日付・時刻帯を再度追加しても無視され、エラーにも重複にもならない。
        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(17,10),new TimeOnly(18,30));
        Assert.Single(await service.GetAllSessionsAsync(state.Path));

        await Assert.ThrowsAsync<ArgumentException>(()=>service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(18,0),new TimeOnly(17,0)));

        await service.RemoveSessionAsync(state.Path,session.Id);
        Assert.Empty(await service.GetAllSessionsAsync(state.Path));
    }

    [Fact]
    public async Task GetEnrollmentCandidatesAsync_FiltersByGradeUnlessAllowOtherGradesIsSet()
    {
        var state=await CreateStateAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));

        var candidates=await service.GetEnrollmentCandidatesAsync(state.Path,cls.Id);
        var candidate=Assert.Single(candidates);
        Assert.Equal(state.Student1Id,candidate.StudentId);Assert.False(candidate.Enrolled);

        await service.SetEnrollmentAsync(state.Path,cls.Id,state.Student1Id,true);
        var afterEnroll=Assert.Single(await service.GetEnrollmentCandidatesAsync(state.Path,cls.Id));
        Assert.True(afterEnroll.Enrolled);

        await service.SetEnrollmentAsync(state.Path,cls.Id,state.Student1Id,false);
        Assert.False(Assert.Single(await service.GetEnrollmentCandidatesAsync(state.Path,cls.Id)).Enrolled);

        var allowOther=await service.SaveClassAsync(state.Path,new GroupLessonClass(cls.Id,cls.Name,cls.Grade,cls.Subject,allowOtherGrades:true));
        var withOtherGrades=await service.GetEnrollmentCandidatesAsync(state.Path,allowOther.Id);
        Assert.Equal(2,withOtherGrades.Count);
        Assert.Contains(withOtherGrades,c=>c.StudentId==state.Student1Id);
        Assert.Contains(withOtherGrades,c=>c.StudentId==state.Student2Id);
    }

    // ユーザー要望（checkpoint112）「集団授業のクラスに、担当講師（任意）チェックボックスを追加し...
    // ここで講師を割り当てると、その講師はその日時に個別授業を持てないようにブロックする」を検証する。
    [Fact]
    public async Task SaveClassAsync_AssigningTeacher_BlocksTheOverlappingSlotAndClearsOnUnassign()
    {
        var state=await CreateStateWithSlotAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));
        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(9,30),new TimeOnly(10,30));

        var assigned=await service.SaveClassAsync(state.Path,cls with { TeacherId=state.Teacher1Id });
        Assert.Equal(state.Teacher1Id,assigned.TeacherId);
        Assert.True(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));

        var unassigned=await service.SaveClassAsync(state.Path,assigned with { TeacherId=null });
        Assert.Null(unassigned.TeacherId);
        Assert.False(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));
    }

    [Fact]
    public async Task SaveClassAsync_AssigningTeacher_RejectsWhenTeacherAlreadyHasAnOccupyingIndividualAssignment()
    {
        var state=await CreateStateWithSlotAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学"));
        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(9,30),new TimeOnly(10,30));
        await OccupyIndividualAssignmentAsync(state.Path,state.Teacher1Id,state.Student1Id,state.DateId,state.SlotId);

        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveClassAsync(state.Path,cls with { TeacherId=state.Teacher1Id }));
        Assert.Null((await service.GetClassesAsync(state.Path)).Single().TeacherId);
    }

    [Fact]
    public async Task AddSessionsAsync_WhenTeacherAlreadyAssigned_BlocksTheNewOverlappingSlotToo()
    {
        var state=await CreateStateWithSlotAsync();var service=new SqliteGroupLessonService();
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学",teacherId:state.Teacher1Id));

        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(9,30),new TimeOnly(10,30));
        Assert.True(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));

        var session=Assert.Single(await service.GetAllSessionsAsync(state.Path));
        await service.RemoveSessionAsync(state.Path,session.Id);
        Assert.False(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));
    }

    [Fact]
    public async Task SaveClassAsync_ReassigningToADifferentTeacher_MovesTheBlockToTheNewTeacher()
    {
        var state=await CreateStateWithSlotAsync();var service=new SqliteGroupLessonService();
        var master=new SqliteMasterDataRepository();
        var teacher2=await master.SaveTeacherAsync(state.Path,new Teacher(0,"T-GROUP2","架空 集団講師二"));
        var cls=await service.SaveClassAsync(state.Path,new GroupLessonClass(0,"中2A","中2","数学",teacherId:state.Teacher1Id));
        await service.AddSessionsAsync(state.Path,cls.Id,[state.DateId],new TimeOnly(9,30),new TimeOnly(10,30));
        Assert.True(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));

        var reassigned=await service.GetClassesAsync(state.Path);
        var current=Assert.Single(reassigned);
        await service.SaveClassAsync(state.Path,current with { TeacherId=teacher2.Id });

        Assert.False(await IsTeacherUnavailableAsync(state.Path,state.Teacher1Id,state.DateId,state.SlotId));
        Assert.True(await IsTeacherUnavailableAsync(state.Path,teacher2.Id,state.DateId,state.SlotId));
    }

    private static async Task<bool> IsTeacherUnavailableAsync(string path,long teacherId,long openDateId,long timeSlotId)
    {
        await using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="SELECT EXISTS(SELECT 1 FROM TeacherUnavailability WHERE TeacherId=$teacher AND OpenDateId=$date AND TimeSlotId=$slot);";
        command.Parameters.AddWithValue("$teacher",teacherId);command.Parameters.AddWithValue("$date",openDateId);command.Parameters.AddWithValue("$slot",timeSlotId);
        return Convert.ToInt64(await command.ExecuteScalarAsync())!=0;
    }

    private static async Task OccupyIndividualAssignmentAsync(string path,long teacherId,long studentId,long openDateId,long timeSlotId)
    {
        await using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO LessonRequest(ProjectId,StudentId,SubjectId,RequiredSessions) SELECT 1,$student,Id,1 FROM Subject LIMIT 1;
            INSERT INTO Assignment(LessonRequestId,TeacherId,OpenDateId,TimeSlotId,Source) VALUES(last_insert_rowid(),$teacher,$date,$slot,'manual');
            """;
        command.Parameters.AddWithValue("$student",studentId);command.Parameters.AddWithValue("$teacher",teacherId);command.Parameters.AddWithValue("$date",openDateId);command.Parameters.AddWithValue("$slot",timeSlotId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<State> CreateStateAsync()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20),considerGroupLessons:true));
        var master=new SqliteMasterDataRepository();
        var student1=await master.SaveStudentAsync(path,new Student(0,"S-GROUP1","架空 集団生徒一","中2"));
        var student2=await master.SaveStudentAsync(path,new Student(0,"S-GROUP2","架空 集団生徒二","中1"));
        var teacher1=await master.SaveTeacherAsync(path,new Teacher(0,"T-GROUP1","架空 集団講師一"));
        await using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT Id FROM OpenDate LIMIT 1;";
        var date=Convert.ToInt64(await command.ExecuteScalarAsync());
        return new State(path,student1.Id,student2.Id,teacher1.Id,date);
    }

    // 担当講師の割り当てはコマ(TimeSlot)との重なりで判定するため、TimeSlot・開講コマの紐付け・
    // 講師の指導可能科目(占有チェック用)を追加で必要とするテスト向けのfixture。
    private async Task<StateWithSlot> CreateStateWithSlotAsync()
    {
        var state=await CreateStateAsync();
        var course=new SqliteCourseSettingsRepository();
        var slot=await course.SaveTimeSlotAsync(state.Path,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(state.Path,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        var master=new SqliteMasterDataRepository();
        var subject=await master.SaveSubjectAsync(state.Path,new Subject(0,"JH_GROUP","集団科目","集","中学校",1));
        await master.SaveQualificationAsync(state.Path,new TeacherQualification(state.Teacher1Id,subject.Id,true));
        return new StateWithSlot(state.Path,state.Student1Id,state.Student2Id,state.Teacher1Id,state.DateId,slot.Id);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long Student1Id,long Student2Id,long Teacher1Id,long DateId);
    private sealed record StateWithSlot(string Path,long Student1Id,long Student2Id,long Teacher1Id,long DateId,long SlotId);
}
