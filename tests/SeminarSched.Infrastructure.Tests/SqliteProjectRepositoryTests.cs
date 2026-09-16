using Microsoft.Data.Sqlite;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteProjectRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SeminarSched.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateAsync_WritesValidProjectAtomically()
    {
        var repository = new SqliteProjectRepository();
        var path = Path.Combine(_directory, "2026夏期講習.jukuschedule");
        var definition = CourseProjectDefinition.Create(
            2026,
            CourseSeason.Summer,
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 22));

        var summary = await repository.CreateAsync(path, definition);

        Assert.Equal("2026夏期講習", summary.Title);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
        Assert.True((await repository.CheckIntegrityAsync(path)).IsValid);

        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OpenDate;";
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task CreateAsync_ExistingTarget_DoesNotOverwrite()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "existing.jukuschedule");
        await File.WriteAllTextAsync(path, "keep-me");
        var repository = new SqliteProjectRepository();
        var definition = CourseProjectDefinition.Create(
            2026,
            CourseSeason.Spring,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 2));

        await Assert.ThrowsAsync<IOException>(() => repository.CreateAsync(path, definition));

        Assert.Equal("keep-me", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CreateBackupAsync_ProducesValidatedIndependentSnapshot()
    {
        var repository = new SqliteProjectRepository();
        var source = Path.Combine(_directory, "source.jukuschedule");
        var backup = Path.Combine(_directory, "backups", "snapshot.jukuschedule");
        await repository.CreateAsync(source, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));

        await repository.CreateBackupAsync(source, backup);

        Assert.True(File.Exists(backup));
        Assert.True((await repository.CheckIntegrityAsync(backup)).IsValid);
        Assert.Equal("2026夏期講習", (await repository.OpenAsync(backup)).Title);
    }

    [Fact]
    public async Task RestoreBackupAsync_AtomicallyReplacesTarget()
    {
        var repository = new SqliteProjectRepository();
        var target = Path.Combine(_directory, "active.jukuschedule");
        var backup = Path.Combine(_directory, "winter.jukuschedule");
        await repository.CreateAsync(target, CourseProjectDefinition.Create(
            2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        await repository.CreateAsync(backup, CourseProjectDefinition.Create(
            2026, CourseSeason.Winter, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 22)));

        await repository.RestoreBackupAsync(backup, target);

        Assert.Equal("2026冬期講習", (await repository.OpenAsync(target)).Title);
        var safetyBackup = Assert.Single(Directory.GetFiles(_directory, "active_before_restore_*.jukuschedule"));
        Assert.Equal("2026夏期講習", (await repository.OpenAsync(safetyBackup)).Title);
        Assert.Empty(Directory.GetFiles(_directory, "*.rollback", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
