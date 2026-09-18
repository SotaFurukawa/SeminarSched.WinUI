using SeminarSched.Application.Questionnaires;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class QuestionnaireKitServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GenerateAsync_WritesConfiguredAppsScriptAtomically()
    {
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var course = new SqliteCourseSettingsRepository();
        var master = new SqliteMasterDataRepository();
        var slot = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 20), 1));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));
        await master.SaveSubjectAsync(path, new Subject(0, "ES_MATH", "算数", "算", "小学校", 1));
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "数学", "数", "中学", 2));
        await master.SaveSubjectAsync(path, new Subject(0, "HS_MATH", "数学", "数", "高等学校", 3));
        await master.SaveSubjectAsync(path, new Subject(0, "ADULT_MATH", "数学（社会人）", "数", "社会人", 4));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory);

        var script = await File.ReadAllTextAsync(Path.Combine(output, "Code.gs"));
        Assert.Contains("2026-07-20", script);
        Assert.Contains("createSeminarSchedForms", script);Assert.Contains("createStudentForm", script);Assert.Contains("createTeacherForm", script);Assert.Contains("createTeacherQualificationForm", script);
        Assert.Contains("GRADE_CHOICES", script);

        var jsonStart = script.IndexOf('{', script.IndexOf("const CONFIG", StringComparison.Ordinal));
        var jsonEnd = script.IndexOf("};", jsonStart, StringComparison.Ordinal);
        using var config = System.Text.Json.JsonDocument.Parse(script[jsonStart..(jsonEnd + 1)]);
        var byLevel = config.RootElement.GetProperty("subjectsByLevel");
        Assert.Equal("ES_MATH", byLevel.GetProperty("elementary")[0].GetProperty("Code").GetString());
        Assert.Equal("JH_MATH", byLevel.GetProperty("juniorHigh")[0].GetProperty("Code").GetString());
        Assert.Equal("HS_MATH", byLevel.GetProperty("seniorHigh")[0].GetProperty("Code").GetString());
        Assert.Equal("ADULT_MATH", byLevel.GetProperty("other")[0].GetProperty("Code").GetString());
        Assert.Empty(Directory.GetDirectories(_directory, "*.tmp-*"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
