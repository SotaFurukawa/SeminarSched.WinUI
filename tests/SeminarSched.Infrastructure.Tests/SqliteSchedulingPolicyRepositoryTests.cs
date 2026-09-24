using SeminarSched.Domain.Projects;
using SeminarSched.Domain.Scheduling;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Infrastructure.Scheduling;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteSchedulingPolicyRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_WithoutPriorSave_ReturnsDefault()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));

        var policy = await new SqliteSchedulingPolicyRepository().GetAsync(path);
        Assert.Equal(SchedulingPolicy.Default, policy);
        Assert.Equal(2, policy.MaxStudentsPerTeacher);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsCustomValues()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "project2.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));

        var repository = new SqliteSchedulingPolicyRepository();
        var custom = new SchedulingPolicy(
            maxStudentsPerTeacher: 4,
            teacherCountPerDayPreference: TeacherCountPerDayPreference.Minimize,
            teacherLoadBalancePreference: TeacherLoadBalancePreference.Balance,
            studentAttendanceDaysPreference: StudentAttendanceDaysPreference.Concentrate,
            pairingSizePreference: PairingSizePreference.Minimize,
            timeOfDayPreference: TimeOfDayPreference.Late,
            maxConcurrentSeats: 12);
        await repository.SaveAsync(path, custom);
        var reloaded = await repository.GetAsync(path);

        Assert.Equal(custom, reloaded);

        // 2回目の保存は上書き（新規行を作らない）。
        var updated = new SchedulingPolicy(
            maxStudentsPerTeacher: 3,
            teacherCountPerDayPreference: custom.TeacherCountPerDayPreference,
            teacherLoadBalancePreference: custom.TeacherLoadBalancePreference,
            studentAttendanceDaysPreference: custom.StudentAttendanceDaysPreference,
            pairingSizePreference: custom.PairingSizePreference,
            timeOfDayPreference: TimeOfDayPreference.Early,
            maxConcurrentSeats: custom.MaxConcurrentSeats);
        await repository.SaveAsync(path, updated);
        var reReloaded = await repository.GetAsync(path);
        Assert.Equal(3, reReloaded.MaxStudentsPerTeacher);
        Assert.Equal(TimeOfDayPreference.Early, reReloaded.TimeOfDayPreference);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
