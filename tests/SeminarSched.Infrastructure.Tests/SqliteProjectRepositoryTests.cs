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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
