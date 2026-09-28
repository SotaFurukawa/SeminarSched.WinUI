using Microsoft.Data.Sqlite;
using SeminarSched.Application.Importing;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Infrastructure.Importing;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

// ユーザー要望（checkpoint111）「可用性の手動編集について、これをカレンダーで変更することはできないか。
// ...複数日付・複数コマへ一括適用というのは撤廃し、ここは一人一人入力していく形で」「出勤出席可能日の
// 可能と優先がありますが...優先は削除してください」への対応で、複数選択・一括適用・優先(level=2)の
// API一式を廃止し、単一対象への即時反映（SetLevelAsync）とカレンダー一括取得（GetCalendarAsync）へ
// 置き換えた。
public sealed class SqliteAvailabilityMatrixServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetLevelAsync_UpdatesTheSingleEntityAndDefaultsUnsetSlotsToLevelOne()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();

        var before = await service.GetCalendarAsync(state.Path, AvailabilityEntityKind.Student, state.Student1Id);
        var beforeDay = Assert.Single(before.Days);
        var beforeSlot = Assert.Single(beforeDay.Slots);
        Assert.Equal(1, beforeSlot.Level);

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Student, state.Student1Id, state.DateId, state.SlotId, 0);

        var after = await service.GetCalendarAsync(state.Path, AvailabilityEntityKind.Student, state.Student1Id);
        Assert.Equal(0, Assert.Single(Assert.Single(after.Days).Slots).Level);

        // 別の生徒には影響しない（複数選択の一括適用を単一対象へ置き換えたための確認）。
        var other = await service.GetCalendarAsync(state.Path, AvailabilityEntityKind.Student, state.Student2Id);
        Assert.Equal(1, Assert.Single(Assert.Single(other.Days).Slots).Level);
    }

    [Fact]
    public async Task SetLevelAsync_TeacherLevelZero_AlsoMarksTeacherUnavailableAndClearsOnLevelOne()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Teacher, state.TeacherId, state.DateId, state.SlotId, 0);
        await using (var connection = new SqliteConnection($"Data Source={state.Path};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TeacherUnavailability WHERE TeacherId=$id;";
            command.Parameters.AddWithValue("$id", state.TeacherId);
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Teacher, state.TeacherId, state.DateId, state.SlotId, 1);
        await using (var connection = new SqliteConnection($"Data Source={state.Path};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TeacherUnavailability WHERE TeacherId=$id;";
            command.Parameters.AddWithValue("$id", state.TeacherId);
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task SetLevelAsync_RejectsSlotNotOpenOnThatDate()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetLevelAsync(state.Path, AvailabilityEntityKind.Student, state.Student1Id, state.DateId, state.SlotId + 999, 1));
    }

    // ユーザー指示「出勤出席可能日の可能と優先がありますが...優先は削除してください」を検証する。
    // 従来のlevel=2（優先）はもう受け付けない。
    [Fact]
    public async Task SetLevelAsync_RejectsTheRemovedPriorityLevel()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetLevelAsync(state.Path, AvailabilityEntityKind.Student, state.Student1Id, state.DateId, state.SlotId, 2));
    }

    [Fact]
    public async Task GetCalendarAsync_ReturnsEveryOpenDateAndSlotAcrossTheWholePeriod()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-CAL", "架空 生徒", "中2"));
        var course = new SqliteCourseSettingsRepository();
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 0), new TimeOnly(11, 0), 2));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot1.Id, slot2.Id]));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 21), true, "", [slot1.Id]));

        var service = new SqliteAvailabilityMatrixService();
        var calendar = await service.GetCalendarAsync(path, AvailabilityEntityKind.Student, student.Id);

        Assert.Equal(2, calendar.Days.Count);
        var day1 = calendar.Days.Single(d => d.Date == new DateOnly(2026, 7, 20));
        var day2 = calendar.Days.Single(d => d.Date == new DateOnly(2026, 7, 21));
        Assert.Equal(2, day1.Slots.Count);
        Assert.Single(day2.Slots);
        Assert.All(day1.Slots.Concat(day2.Slots), s => Assert.Equal(1, s.Level));

        await service.SetLevelAsync(path, AvailabilityEntityKind.Student, student.Id, day2.OpenDateId, slot1.Id, 0);
        var updated = await service.GetCalendarAsync(path, AvailabilityEntityKind.Student, student.Id);
        Assert.Equal(0, Assert.Single(updated.Days.Single(d => d.Date == new DateOnly(2026, 7, 21)).Slots).Level);
    }

    private async Task<State> CreateStateAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));
        var master = new SqliteMasterDataRepository();
        var s1 = await master.SaveStudentAsync(path, new Student(0, "S-MX1", "架空 生徒一", "中2"));
        var s2 = await master.SaveStudentAsync(path, new Student(0, "S-MX2", "架空 生徒二", "中2"));
        var teacher = await master.SaveTeacherAsync(path, new Teacher(0, "T-MX1", "架空 講師"));
        var course = new SqliteCourseSettingsRepository();
        var slot = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot.Id]));
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM OpenDate LIMIT 1;";
        var date = Convert.ToInt64(await command.ExecuteScalarAsync());
        return new State(path, s1.Id, s2.Id, teacher.Id, date, slot.Id);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed record State(string Path, long Student1Id, long Student2Id, long TeacherId, long DateId, long SlotId);
}
