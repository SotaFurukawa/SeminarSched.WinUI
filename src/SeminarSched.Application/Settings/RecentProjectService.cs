namespace SeminarSched.Application.Settings;

public sealed class RecentProjectService
{
    private const int MaximumEntries = 10;
    private readonly IAppSettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecentProjectService(IAppSettingsStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<IReadOnlyList<RecentProjectEntry>> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return settings.SafeRecentProjects
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .OrderByDescending(entry => entry.LastOpenedUtc)
            .Take(MaximumEntries)
            .ToArray();
    }

    public async Task TouchAsync(string path, string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var fullPath = Path.GetFullPath(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var entries = settings.SafeRecentProjects
                .Where(entry => !string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentProjectEntry(fullPath, title.Trim(), DateTimeOffset.UtcNow))
                .Take(MaximumEntries)
                .ToArray();
            await _store.SaveAsync(settings with { RecentProjects = entries }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var entries = settings.SafeRecentProjects
                .Where(entry => !string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            await _store.SaveAsync(settings with { RecentProjects = entries }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
