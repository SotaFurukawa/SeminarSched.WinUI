using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteMasterDataRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndList_RoundTripsMasterDataAndRelations()
    {
        var path = Path.Combine(_directory, "master.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var repository = new SqliteMasterDataRepository();

        var student = await repository.SaveStudentAsync(path, new Student(0, "S-001", "架空 生徒", "中2", 3));
        var teacher = await repository.SaveTeacherAsync(path, new Teacher(0, "T-001", "架空 講師"));
        var subject = await repository.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "数学", "数", "中学", 1));
        await repository.SaveQualificationAsync(path, new TeacherQualification(teacher.Id, subject.Id, true));
        await repository.SaveRegularLessonAsync(path, new RegularLessonProfile(0, student.Id, subject.Id, teacher.Id, 4, true));

        Assert.Equal("架空 生徒", Assert.Single(await repository.GetStudentsAsync(path)).Name);
        Assert.Equal("架空 講師", Assert.Single(await repository.GetTeachersAsync(path)).Name);
        Assert.Equal("JH_MATH", Assert.Single(await repository.GetSubjectsAsync(path)).Code);
        Assert.True((await new SqliteProjectRepository().CheckIntegrityAsync(path)).IsValid);
    }

    [Fact]
    public async Task SaveStudent_DuplicateExternalId_IsRejectedByDatabase()
    {
        var path = Path.Combine(_directory, "duplicate.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(
            2026, CourseSeason.Spring, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2)));
        var repository = new SqliteMasterDataRepository();
        await repository.SaveStudentAsync(path, new Student(0, "S-001", "架空 一郎", "中1"));

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            repository.SaveStudentAsync(path, new Student(0, "S-001", "架空 二郎", "中2")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
