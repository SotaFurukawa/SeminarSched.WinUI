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

public sealed class SqliteAvailabilityMatrixServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetLevelAsync_AppliesToAllSelectedStudentsAndDefaultsUnsetToLevelOne()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();

        var before = await service.GetDayMatrixAsync(state.Path, AvailabilityEntityKind.Student, state.DateId, [state.Student1Id, state.Student2Id]);
        Assert.All(before, row => Assert.Equal(1, row.LevelsBySlot[state.SlotId]));

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Student, [state.Student1Id, state.Student2Id], state.DateId, state.SlotId, 0);
        var after = await service.GetDayMatrixAsync(state.Path, AvailabilityEntityKind.Student, state.DateId, [state.Student1Id, state.Student2Id]);
        Assert.All(after, row => Assert.Equal(0, row.LevelsBySlot[state.SlotId]));
    }

    [Fact]
    public async Task SetLevelAsync_TeacherLevelZero_AlsoMarksTeacherUnavailableAndClearsOnLaterLevel()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Teacher, [state.TeacherId], state.DateId, state.SlotId, 0);
        await using (var connection = new SqliteConnection($"Data Source={state.Path};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TeacherUnavailability WHERE TeacherId=$id;";
            command.Parameters.AddWithValue("$id", state.TeacherId);
            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }

        await service.SetLevelAsync(state.Path, AvailabilityEntityKind.Teacher, [state.TeacherId], state.DateId, state.SlotId, 2);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetLevelAsync(state.Path, AvailabilityEntityKind.Student, [state.Student1Id], state.DateId, state.SlotId + 999, 1));
    }

    [Fact]
    public async Task SetLevelsAsync_AppliesToEveryDateAndSlotCombinationInOneCall()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var master = new SqliteMasterDataRepository();
        var student = await master.SaveStudentAsync(path, new Student(0, "S-BULK", "架空 生徒", "中2"));
        var course = new SqliteCourseSettingsRepository();
        var slot1 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "1", "1限", new TimeOnly(9, 0), new TimeOnly(10, 0), 1));
        var slot2 = await course.SaveTimeSlotAsync(path, new TimeSlot(0, "2", "2限", new TimeOnly(10, 0), new TimeOnly(11, 0), 2));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 20), true, "", [slot1.Id, slot2.Id]));
        await course.SaveCourseDayAsync(path, new CourseDay(new DateOnly(2026, 7, 21), true, "", [slot1.Id, slot2.Id]));
        long date1, date2;
        await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM OpenDate ORDER BY Date;";
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync(); date1 = reader.GetInt64(0);
            await reader.ReadAsync(); date2 = reader.GetInt64(0);
        }

        var service = new SqliteAvailabilityMatrixService();
        await service.SetLevelsAsync(path, AvailabilityEntityKind.Student, [student.Id], [(date1, slot1.Id), (date1, slot2.Id), (date2, slot1.Id), (date2, slot2.Id)], 0);

        var day1 = Assert.Single(await service.GetDayMatrixAsync(path, AvailabilityEntityKind.Student, date1, [student.Id]));
        var day2 = Assert.Single(await service.GetDayMatrixAsync(path, AvailabilityEntityKind.Student, date2, [student.Id]));
        Assert.Equal(0, day1.LevelsBySlot[slot1.Id]); Assert.Equal(0, day1.LevelsBySlot[slot2.Id]);
        Assert.Equal(0, day2.LevelsBySlot[slot1.Id]); Assert.Equal(0, day2.LevelsBySlot[slot2.Id]);
    }

    [Fact]
    public async Task SetLevelsAsync_InvalidPairAmongManyRollsBackTheWholeBatch()
    {
        var state = await CreateStateAsync();
        var service = new SqliteAvailabilityMatrixService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetLevelsAsync(
            state.Path, AvailabilityEntityKind.Student, [state.Student1Id], [(state.DateId, state.SlotId), (state.DateId, state.SlotId + 999)], 0));
        var after = Assert.Single(await service.GetDayMatrixAsync(state.Path, AvailabilityEntityKind.Student, state.DateId, [state.Student1Id]));
        Assert.Equal(1, after.LevelsBySlot[state.SlotId]);
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
