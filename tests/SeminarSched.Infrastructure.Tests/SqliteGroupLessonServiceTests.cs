using SeminarSched.Domain.GroupLessons;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
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

    private async Task<State> CreateStateAsync()
    {
        Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,$"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20),considerGroupLessons:true));
        var master=new SqliteMasterDataRepository();
        var student1=await master.SaveStudentAsync(path,new Student(0,"S-GROUP1","架空 集団生徒一","中2"));
        var student2=await master.SaveStudentAsync(path,new Student(0,"S-GROUP2","架空 集団生徒二","中1"));
        await using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT Id FROM OpenDate LIMIT 1;";
        var date=Convert.ToInt64(await command.ExecuteScalarAsync());
        return new State(path,student1.Id,student2.Id,date);
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
    private sealed record State(string Path,long Student1Id,long Student2Id,long DateId);
}
