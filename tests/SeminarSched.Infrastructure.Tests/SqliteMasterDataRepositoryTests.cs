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
        await repository.SaveStudentAsync(path, new Student(student.Id, "S-001", "架空 生徒・更新", "中3", 2, true, "更新", false));
        await repository.SaveTeacherAsync(path, new Teacher(teacher.Id, "T-001", "架空 講師・更新", true, "更新", false));
        await repository.SaveSubjectAsync(path, new Subject(subject.Id, "JH_MATH", "数学・更新", "数", "中学", 2, false));

        var storedStudent=Assert.Single(await repository.GetStudentsAsync(path));Assert.Equal("架空 生徒・更新",storedStudent.Name);Assert.False(storedStudent.Active);Assert.True(storedStudent.AllowGap);
        var storedTeacher=Assert.Single(await repository.GetTeachersAsync(path));Assert.Equal("架空 講師・更新",storedTeacher.Name);Assert.False(storedTeacher.Active);
        var storedSubject=Assert.Single(await repository.GetSubjectsAsync(path));Assert.Equal("JH_MATH",storedSubject.Code);Assert.False(storedSubject.Active);Assert.Equal(2,storedSubject.SortOrder);
        Assert.Empty(await repository.GetStudentsAsync(path,includeInactive:false));
        var qualification=Assert.Single(await repository.GetQualificationsAsync(path));Assert.Equal(teacher.Id,qualification.TeacherId);Assert.True(qualification.CanTeach);
        var regular=Assert.Single(await repository.GetRegularLessonsAsync(path));Assert.Equal(student.Id,regular.StudentId);Assert.Equal(4,regular.RegularTeacherPriority);Assert.True(regular.OneToOneRequired);
        Assert.True((await new SqliteProjectRepository().CheckIntegrityAsync(path)).IsValid);
    }

    [Fact]
    public async Task SaveGetDeleteLessonRequest_RoundTripsAllFieldsAndUpsertsOnConflict()
    {
        var path = Path.Combine(_directory, "request.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var repository = new SqliteMasterDataRepository();
        var student = await repository.SaveStudentAsync(path, new Student(0, "S-001", "架空 生徒", "中2"));
        var teacher1 = await repository.SaveTeacherAsync(path, new Teacher(0, "T-001", "架空 講師1"));
        var teacher2 = await repository.SaveTeacherAsync(path, new Teacher(0, "T-002", "架空 講師2"));
        var subject = await repository.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "数学", "数", "中学", 1));

        await repository.SaveLessonRequestAsync(path, new LessonRequest(0, student.Id, subject.Id, 4,
            teacher1.Id, 5, teacher1.Id, teacher2.Id, null, true, 3, false, "初回"));
        var stored = Assert.Single(await repository.GetLessonRequestsAsync(path));
        Assert.Equal(4, stored.RequiredSessions); Assert.Equal(teacher1.Id, stored.RegularTeacherId); Assert.Equal(5, stored.RegularTeacherPriority);
        Assert.Equal(teacher1.Id, stored.PreferredTeacher1Id); Assert.Equal(teacher2.Id, stored.PreferredTeacher2Id); Assert.Null(stored.PreferredTeacher3Id);
        Assert.True(stored.OneToOneRequired); Assert.Equal(3, stored.MaxConsecutiveSlotsOverride); Assert.False(stored.AllowGapOverride); Assert.Equal("初回", stored.Note);

        await repository.SaveLessonRequestAsync(path, new LessonRequest(0, student.Id, subject.Id, 6, note: "更新"));
        var updated = Assert.Single(await repository.GetLessonRequestsAsync(path));
        Assert.Equal(6, updated.RequiredSessions); Assert.Null(updated.RegularTeacherId); Assert.Equal("更新", updated.Note);

        await repository.DeleteLessonRequestAsync(path, student.Id, subject.Id);
        Assert.Empty(await repository.GetLessonRequestsAsync(path));
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

    // 過去のバグ（共通名簿Excel取込みが略称列を表示名そのままで上書きしていた）で作られた既存の
    // 壊れた略称データを、SqliteProjectSchema.EnsureCurrentAsync（各リポジトリ呼び出しのたびに実行
    // される）が自己修復することを確認する。新規保存はドメイン層のバリデーションで防げるが、
    // 既にファイルへ書き込まれてしまった過去のデータはこの自己修復でしか直せないため。
    [Fact]
    public async Task GetSubjectsAsync_SelfHealsShortNamesLeftInvalidByThePastImportBug()
    {
        var path = Path.Combine(_directory, "backfill.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var repository = new SqliteMasterDataRepository();
        var subject = await repository.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "中学校・数学", "数", "中学校", 1));

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Subject SET ShortName='中学校・数学' WHERE Id=$id;";
            command.Parameters.AddWithValue("$id", subject.Id);
            await command.ExecuteNonQueryAsync();
        }

        var healed = Assert.Single(await repository.GetSubjectsAsync(path));
        Assert.Equal("数", healed.ShortName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
