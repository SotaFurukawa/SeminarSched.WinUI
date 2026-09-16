using System.Text.Json;
using SeminarSched.Application.Settings;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Infrastructure.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAppSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppSettings.Default;
            }

            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return settings is not null && Enum.IsDefined(settings.OptimizationQualityLevel)
                ? settings
                : AppSettings.Default;
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.OptimizationQualityLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Unknown optimization quality level.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
