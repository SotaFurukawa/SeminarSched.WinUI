using SeminarSched.Application.Settings;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Tests;

public sealed class RecentProjectServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SeminarSched.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TouchAsync_MovesExistingPathToFrontWithoutDuplicates()
    {
        var store = new MemorySettingsStore();
        var service = new RecentProjectService(store);
        var first = Path.Combine(_directory, "first.jukuschedule");
        var second = Path.Combine(_directory, "second.jukuschedule");

        await service.TouchAsync(first, "First");
        await service.TouchAsync(second, "Second");
        await service.TouchAsync(first, "First updated");

        var entries = await service.GetAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("First updated", entries[0].Title);
        Assert.Equal(Path.GetFullPath(first), entries[0].Path);
    }

    [Fact]
    public async Task TouchAsync_KeepsAtMostTenEntries()
    {
        var store = new MemorySettingsStore();
        var service = new RecentProjectService(store);
        for (var index = 0; index < 12; index++)
        {
            await service.TouchAsync(Path.Combine(_directory, $"{index}.jukuschedule"), $"Project {index}");
        }

        Assert.Equal(10, (await service.GetAsync()).Count);
    }

    [Fact]
    public async Task RemoveAsync_HidesEntryWithoutAffectingOthers()
    {
        var store = new MemorySettingsStore();
        var service = new RecentProjectService(store);
        var first = Path.Combine(_directory, "first.jukuschedule");
        var second = Path.Combine(_directory, "second.jukuschedule");
        await service.TouchAsync(first, "First");
        await service.TouchAsync(second, "Second");

        await service.RemoveAsync(first);

        var entries = await service.GetAsync();
        var remaining = Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(second), remaining.Path);
    }

    public void Dispose() { }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        private AppSettings _settings = new(OptimizationQualityLevel.Standard);
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }
}
