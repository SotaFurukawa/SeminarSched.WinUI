using Microsoft.Data.Sqlite;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.Importing;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class CsvResponseImportServiceTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"SeminarSched.Tests",Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task PreviewAndApply_ValidFiles_CommitsBothTogether()
    {
        Directory.CreateDirectory(_directory);var db=Path.Combine(_directory,"p.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(db,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();await master.SaveStudentAsync(db,new Student(0,"S-001","架空 生徒","中2"));await master.SaveTeacherAsync(db,new Teacher(0,"T-001","架空 講師"));await master.SaveSubjectAsync(db,new Subject(0,"JH_MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(db,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(db,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        var students=Path.Combine(_directory,"students.csv");var teachers=Path.Combine(_directory,"teachers.csv");
        await File.WriteAllTextAsync(students,"生徒ID,科目コード,必要回数\nS-001,JH_MATH,3\n");await File.WriteAllTextAsync(teachers,"講師ID,勤務不可\nT-001,2026-07-20|1\n");
        var service=new CsvResponseImportService();var preview=await service.PreviewAsync(db,students,teachers);Assert.True(preview.CanApply);await service.ApplyAsync(db,preview);
        await using var connection=new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False");await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT (SELECT COUNT(*) FROM LessonRequest),(SELECT COUNT(*) FROM TeacherUnavailability);";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(1,reader.GetInt32(0));Assert.Equal(1,reader.GetInt32(1));
    }

    [Fact]
    public async Task ApplyAsync_FileChangedAfterPreview_IsRejected()
    {
        Directory.CreateDirectory(_directory);var db=Path.Combine(_directory,"changed.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(db,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();await master.SaveStudentAsync(db,new Student(0,"S-001","架空 生徒","中2"));await master.SaveTeacherAsync(db,new Teacher(0,"T-001","架空 講師"));await master.SaveSubjectAsync(db,new Subject(0,"JH_MATH","数学","数","中学",1));
        var students=Path.Combine(_directory,"changed-students.csv");var teachers=Path.Combine(_directory,"changed-teachers.csv");await File.WriteAllTextAsync(students,"生徒ID,科目コード,必要回数\nS-001,JH_MATH,2\n");await File.WriteAllTextAsync(teachers,"講師ID,勤務不可\nT-001,\n");
        var service=new CsvResponseImportService();var preview=await service.PreviewAsync(db,students,teachers);await File.AppendAllTextAsync(students,"S-001,JH_MATH,3\n");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ApplyAsync(db,preview));
    }
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
