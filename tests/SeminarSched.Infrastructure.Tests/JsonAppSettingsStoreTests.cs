using SeminarSched.Application.Settings;
using SeminarSched.Infrastructure.Settings;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Infrastructure.Tests;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SeminarSched.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsLevelThree()
    {
        using var store = CreateStore();

        var settings = await store.LoadAsync();

        Assert.Equal(OptimizationQualityLevel.Standard, settings.OptimizationQualityLevel);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsSelectedLevel()
    {
        using var store = CreateStore();
        await store.SaveAsync(new AppSettings(OptimizationQualityLevel.Highest));

        var settings = await store.LoadAsync();

        Assert.Equal(OptimizationQualityLevel.Highest, settings.OptimizationQualityLevel);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SaveAsync_RoundTripsUnrestrictedResourceUsage()
    {
        using var store = CreateStore();
        await store.SaveAsync(new AppSettings(OptimizationQualityLevel.Standard, UnrestrictedResourceUsage: true));

        var settings = await store.LoadAsync();

        Assert.True(settings.UnrestrictedResourceUsage);
    }

    // ユーザー指示による新規チェックボックス追加前に保存された設定ファイル（このフィールドを
    // 持たないJSON）を読み込んでも、既定値false（＝CPU使用率を制限する）へ安全にfallbackすることを
    // 確認する。
    [Fact]
    public async Task LoadAsync_JsonWithoutUnrestrictedResourceUsage_DefaultsToFalse()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"optimizationQualityLevel":2}""");
        using var store = new JsonAppSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.False(settings.UnrestrictedResourceUsage);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReturnsSafeDefault()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "not-json");
        using var store = new JsonAppSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal(AppSettings.Default, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonAppSettingsStore CreateStore() =>
        new(Path.Combine(_directory, "settings.json"));
}
