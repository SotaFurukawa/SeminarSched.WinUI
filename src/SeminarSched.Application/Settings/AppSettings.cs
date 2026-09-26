using SeminarSched.Optimization.Diagnostics;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Settings;

public sealed record RecentProjectEntry(string Path, string Title, DateTimeOffset LastOpenedUtc);

public sealed record AppSettings(
    OptimizationQualityLevel OptimizationQualityLevel,
    IReadOnlyList<RecentProjectEntry>? RecentProjects = null,
    bool UnrestrictedResourceUsage = false,
    /// <summary>ユーザー要望（checkpoint108）「このスコアはアプリ内のテストをする場合、毎回やる
    /// ものではなくて、初めて自動作成する際に、一度だけ調べることにする」への対応。nullは「この機体
    /// ではまだ計測していない」ことを表す。一度計測したら、この設定ファイル（機体・インストールごと、
    /// プロジェクトファイルとは無関係）へ保存し、以降のすべての自動作成実行で再利用する。</summary>
    HardwareTier? HardwareTier = null,
    double? HardwareBenchmarkElapsedSeconds = null)
{
    public static AppSettings Default { get; } = new(OptimizationProfileCatalog.DefaultLevel);

    public IReadOnlyList<RecentProjectEntry> SafeRecentProjects => RecentProjects ?? [];
}
