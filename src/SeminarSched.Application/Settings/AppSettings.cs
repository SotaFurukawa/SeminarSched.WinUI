using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Settings;

public sealed record RecentProjectEntry(string Path, string Title, DateTimeOffset LastOpenedUtc);

public sealed record AppSettings(
    OptimizationQualityLevel OptimizationQualityLevel,
    IReadOnlyList<RecentProjectEntry>? RecentProjects = null)
{
    public static AppSettings Default { get; } = new(OptimizationProfileCatalog.DefaultLevel);

    public IReadOnlyList<RecentProjectEntry> SafeRecentProjects => RecentProjects ?? [];
}
