using System.Text;
using ClosedXML.Excel;
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

    [Fact]
    public async Task PreviewAndApply_XlsxAndCp932Availability_PersistsAuditAndSourceSnapshots()
    {
        Directory.CreateDirectory(_directory);
        var db = Path.Combine(_directory, "availability.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(db, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(db, new Student(0, "S-001", "架空 生徒", "中2"));
        var teacher = await master.SaveTeacherAsync(db, new Teacher(0, "T-001", "架空 講師"));
        var subject = await master.SaveSubjectAsync(db, new Subject(0, "JH_MATH", "数学", "数", "中学", 1));
        var course = new SqliteCourseSettingsRepository();
        var slot = await course.SaveTimeSlotAsync(db, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        await course.SaveCourseDayAsync(db, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));

        var students = Path.Combine(_directory, "students.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("回答");
            string[] headers = ["生徒ID", "科目コード", "必要回数", "日付", "1", "第1希望講師ID"];
            for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
            object[] values = ["S-001", "JH_MATH", 3, new DateTime(2026, 7, 20), 2, "T-001"];
            for (var index = 0; index < values.Length; index++) sheet.Cell(2, index + 1).Value = XLCellValue.FromObject(values[index]);
            workbook.SaveAs(students);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var teachers = Path.Combine(_directory, "teachers.csv");
        await File.WriteAllTextAsync(teachers, "講師ID,日付,1\r\nT-001,2026/7/20,0\r\n", Encoding.GetEncoding(932));

        var service = new CsvResponseImportService();
        var preview = await service.PreviewAsync(db, students, teachers);
        Assert.True(preview.CanApply, string.Join(Environment.NewLine, preview.Issues.Select(issue => issue.Message)));
        await service.ApplyAsync(db, preview);

        await using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AvailabilityLevel FROM StudentAvailability WHERE StudentId=$id;";
        command.Parameters.AddWithValue("$id", student.Id);
        Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT AvailabilityLevel FROM TeacherAvailability WHERE TeacherId=$id;";
        command.Parameters.Clear(); command.Parameters.AddWithValue("$id", teacher.Id);
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT COUNT(*) FROM TeacherUnavailability WHERE TeacherId=$id;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT PreferredTeacher1Id FROM LessonRequest WHERE StudentId=$student AND SubjectId=$subject;";
        command.Parameters.Clear(); command.Parameters.AddWithValue("$student", student.Id); command.Parameters.AddWithValue("$subject", subject.Id);
        Assert.Equal(teacher.Id, Convert.ToInt64(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT (SELECT COUNT(*) FROM ImportBatch),(SELECT COUNT(*) FROM ImportSourceSnapshot),(SELECT COUNT(*) FROM AuditLog WHERE Action='availability_imported');";
        command.Parameters.Clear();
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
    }
    [Fact]
    public async Task PreviewAsync_SimpleFormat_ComputesRequiredSessionsAndUnavailableListDiff()
    {
        Directory.CreateDirectory(_directory);var db=Path.Combine(_directory,"simple-diff.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(db,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,20)));
        var master=new SqliteMasterDataRepository();await master.SaveStudentAsync(db,new Student(0,"S-001","架空 生徒","中2"));await master.SaveTeacherAsync(db,new Teacher(0,"T-001","架空 講師"));await master.SaveSubjectAsync(db,new Subject(0,"JH_MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(db,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));await course.SaveCourseDayAsync(db,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));
        var students=Path.Combine(_directory,"simple-students.csv");var teachers=Path.Combine(_directory,"simple-teachers.csv");
        await File.WriteAllTextAsync(students,"生徒ID,科目コード,必要回数\nS-001,JH_MATH,3\n");await File.WriteAllTextAsync(teachers,"講師ID,勤務不可\nT-001,2026-07-20|1\n");
        var service=new CsvResponseImportService();

        var firstPreview=await service.PreviewAsync(db,students,teachers);
        Assert.Equal(1,firstPreview.Diff.StudentAdded);Assert.Equal(0,firstPreview.Diff.StudentChanged);Assert.Equal(0,firstPreview.Diff.StudentUnchanged);
        Assert.Equal(1,firstPreview.Diff.TeacherAdded);Assert.Equal(0,firstPreview.Diff.TeacherChanged);Assert.Equal(0,firstPreview.Diff.TeacherUnchanged);
        await service.ApplyAsync(db,firstPreview);

        var unchangedPreview=await service.PreviewAsync(db,students,teachers);
        Assert.Equal(0,unchangedPreview.Diff.StudentAdded);Assert.Equal(0,unchangedPreview.Diff.StudentChanged);Assert.Equal(1,unchangedPreview.Diff.StudentUnchanged);
        Assert.Equal(0,unchangedPreview.Diff.TeacherAdded);Assert.Equal(0,unchangedPreview.Diff.TeacherChanged);Assert.Equal(1,unchangedPreview.Diff.TeacherUnchanged);

        await File.WriteAllTextAsync(students,"生徒ID,科目コード,必要回数\nS-001,JH_MATH,4\n");await File.WriteAllTextAsync(teachers,"講師ID,勤務不可\nT-001,\n");
        var changedPreview=await service.PreviewAsync(db,students,teachers);
        Assert.Equal(0,changedPreview.Diff.StudentAdded);Assert.Equal(1,changedPreview.Diff.StudentChanged);Assert.Equal(0,changedPreview.Diff.StudentUnchanged);
        Assert.Equal(0,changedPreview.Diff.TeacherAdded);Assert.Equal(1,changedPreview.Diff.TeacherChanged);Assert.Equal(0,changedPreview.Diff.TeacherUnchanged);
    }

    [Fact]
    public async Task PreviewAsync_ComputesDiffAndRemovalCandidatesRequireExplicitConfirmation()
    {
        Directory.CreateDirectory(_directory);var db=Path.Combine(_directory,"diff.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(db,CourseProjectDefinition.Create(2026,CourseSeason.Summer,new DateOnly(2026,7,20),new DateOnly(2026,7,21)));
        var master=new SqliteMasterDataRepository();await master.SaveStudentAsync(db,new Student(0,"S-001","架空 生徒","中2"));await master.SaveTeacherAsync(db,new Teacher(0,"T-001","架空 講師"));await master.SaveSubjectAsync(db,new Subject(0,"JH_MATH","数学","数","中学",1));
        var course=new SqliteCourseSettingsRepository();var slot=await course.SaveTimeSlotAsync(db,new TimeSlot(0,"1","1限",new TimeOnly(9,0),new TimeOnly(10,0),1));
        await course.SaveCourseDayAsync(db,new CourseDay(new DateOnly(2026,7,20),true,"",[slot.Id]));await course.SaveCourseDayAsync(db,new CourseDay(new DateOnly(2026,7,21),true,"",[slot.Id]));

        var service=new CsvResponseImportService();
        var firstStudents=Path.Combine(_directory,"first-students.csv");var firstTeachers=Path.Combine(_directory,"first-teachers.csv");
        await File.WriteAllTextAsync(firstStudents,"生徒ID,科目コード,日付,1\nS-001,JH_MATH,2026-07-20,2\nS-001,JH_MATH,2026-07-21,2\n");
        await File.WriteAllTextAsync(firstTeachers,"講師ID,日付,1\nT-001,2026-07-20,2\n");
        var firstPreview=await service.PreviewAsync(db,firstStudents,firstTeachers);Assert.True(firstPreview.CanApply,string.Join(Environment.NewLine,firstPreview.Issues.Select(issue=>issue.Message)));
        Assert.Equal(2,firstPreview.Diff.StudentAdded);Assert.Empty(firstPreview.Diff.StudentRemovalCandidates);
        await service.ApplyAsync(db,firstPreview);

        var secondStudents=Path.Combine(_directory,"second-students.csv");
        await File.WriteAllTextAsync(secondStudents,"生徒ID,科目コード,日付,1\nS-001,JH_MATH,2026-07-20,1\n");
        var secondPreview=await service.PreviewAsync(db,secondStudents,firstTeachers);Assert.True(secondPreview.CanApply,string.Join(Environment.NewLine,secondPreview.Issues.Select(issue=>issue.Message)));
        Assert.Equal(1,secondPreview.Diff.StudentChanged);
        var removal=Assert.Single(secondPreview.Diff.StudentRemovalCandidates);Assert.Equal("S-001",removal.ExternalId);Assert.Equal("2026-07-21",removal.Date);

        await service.ApplyAsync(db,secondPreview,removeUnlistedAvailability:false);
        await using(var connection=new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM StudentAvailability;";Assert.Equal(2L,Convert.ToInt64(await command.ExecuteScalarAsync()));}

        await service.ApplyAsync(db,secondPreview,removeUnlistedAvailability:true);
        await using(var connection=new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False")){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM StudentAvailability;";Assert.Equal(1L,Convert.ToInt64(await command.ExecuteScalarAsync()));}
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
