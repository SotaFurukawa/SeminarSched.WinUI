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
        await master.SaveSubjectAsync(path, new Subject(0, "JH_MATH", "数学", "数", "中学", 1));

        var output = await new QuestionnaireKitService(course, master).GenerateAsync(path, _directory);

        var script = await File.ReadAllTextAsync(Path.Combine(output, "Code.gs"));
        Assert.Contains("2026-07-20", script);
        Assert.Contains("JH_MATH", script);
        Assert.Contains("createSeminarSchedForms", script);
        Assert.Empty(Directory.GetDirectories(_directory, "*.tmp-*"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
