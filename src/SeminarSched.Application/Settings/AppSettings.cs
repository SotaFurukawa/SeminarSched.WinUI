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
    double? HardwareBenchmarkElapsedSeconds = null,
    /// <summary>ユーザー要望（checkpoint124）「プロダクトキーを実装したい」への対応。マスターキーで
    /// 認証された場合はtrue（年度に関わらず無期限）。ProductKeyYearは年度キーで認証された年度
    /// （毎年2/1に現在の年度と一致しなくなるため、再度プロダクトキーを要求する判定に使う）。</summary>
    bool ProductKeyIsMaster = false,
    int? ProductKeyYear = null,
    /// <summary>ユーザー要望（checkpoint125）「自動アップデート機能を追加しておきたい。週に1度、
    /// アップデートがないかのチェックを行い...」への対応。GitHub Releases APIへの問い合わせに
    /// 成功した直近時刻（UTC）。nullは「まだ一度も確認していない」ことを表す。問い合わせに失敗
    /// した場合は更新しない（次回起動時に再試行させるため）。</summary>
    DateTimeOffset? LastUpdateCheckUtc = null)
{
    public static AppSettings Default { get; } = new(OptimizationProfileCatalog.DefaultLevel);

    public IReadOnlyList<RecentProjectEntry> SafeRecentProjects => RecentProjects ?? [];

    public string? ProductKeyLicenseLabel => ProductKeyIsMaster
        ? "完全版"
        : ProductKeyYear is { } year ? $"{year}年版" : null;
}
