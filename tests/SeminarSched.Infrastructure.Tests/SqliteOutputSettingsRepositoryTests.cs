using SeminarSched.Domain.Output;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.Output;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.Tests;

public sealed class SqliteOutputSettingsRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_WithoutPriorSave_ReturnsDefault()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "project.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));

        var settings = await new SqliteOutputSettingsRepository().GetAsync(path);
        Assert.Equal(OutputSettings.Default, settings);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsCustomValues()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "project2.jukuschedule");
        await new SqliteProjectRepository().CreateAsync(path, CourseProjectDefinition.Create(2026, CourseSeason.Summer, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)));

        var repository = new SqliteOutputSettingsRepository();
        var custom = new OutputSettings(OutputSettings.PaperSizeA3, OutputSettings.OrientationPortrait, 12, "{report}_{project}", "#FF0000", "#00FF00", "#0000FF");
        await repository.SaveAsync(path, custom);
        var reloaded = await repository.GetAsync(path);

        Assert.Equal(custom, reloaded);

        // 2回目の保存は上書き（新規行を作らない）。
        var updated = new OutputSettings(custom.PaperSize, custom.Orientation, 5, custom.FileNamePattern, custom.ClosedFillHex, custom.UnavailableFillHex, custom.GroupFillHex);
        await repository.SaveAsync(path, updated);
        Assert.Equal(5, (await repository.GetAsync(path)).MarginMm);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
