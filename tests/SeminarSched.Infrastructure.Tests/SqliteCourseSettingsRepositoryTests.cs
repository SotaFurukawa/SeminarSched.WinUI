using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteCourseSettingsRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TimeSlotsAndCourseDays_RoundTrip()
    {
        var path = await CreateProjectAsync();
        var repository = new SqliteCourseSettingsRepository();
        var first = await repository.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 20), 1));
        var second = await repository.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 30), new TimeOnly(11, 50), 2));
        var day = new CourseDay(new DateOnly(2026, 7, 21), true, "短縮日", [first.Id, second.Id]);

        await repository.SaveCourseDayAsync(path, day);
        await repository.SaveTimeSlotAsync(path, new TimeSlot(first.Id,"1","1限・更新",new TimeOnly(9,15),new TimeOnly(10,15),3,false));

        var slots=await repository.GetTimeSlotsAsync(path);var updated=Assert.Single(slots,item=>item.Id==first.Id);Assert.Equal("1限・更新",updated.DisplayName);Assert.False(updated.Active);Assert.Single(slots,item=>item.Id==second.Id);
        var stored = Assert.Single(await repository.GetCourseDaysAsync(path), x => x.Date == day.Date);
        Assert.Equal("短縮日", stored.Note);
        Assert.Equal([first.Id, second.Id], stored.EnabledTimeSlotIds);
    }

    [Fact]
    public async Task ClosedDay_ClearsEnabledSlots()
    {
        var path = await CreateProjectAsync();
        var repository = new SqliteCourseSettingsRepository();
        var slot = await repository.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var date = new DateOnly(2026, 7, 20);
        await repository.SaveCourseDayAsync(path, new CourseDay(date, true, "", [slot.Id]));
        await repository.SaveCourseDayAsync(path, new CourseDay(date, false, "休校", [slot.Id]));

        var stored = Assert.Single(await repository.GetCourseDaysAsync(path), x => x.Date == date);
        Assert.False(stored.IsOpen);
        Assert.Empty(stored.EnabledTimeSlotIds);
    }

    private async Task<string> CreateProjectAsync()
    {
        var path = Path.Combine(_directory, "settings.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        return path;
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
