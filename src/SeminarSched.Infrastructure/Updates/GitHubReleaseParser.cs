using System.Text.Json;
using SeminarSched.Application;
using SeminarSched.Application.Updates;

namespace SeminarSched.Infrastructure.Updates;

/// <summary>
/// GitHub Releases APIの`GET /repos/{owner}/{repo}/releases/latest`レスポンス（公開済み・
/// Draft/Prereleaseを除く最新releaseのJSON）を解釈する純粋な部分を、HTTP通信から切り離して
/// 独立にテストできるようにしている。
/// </summary>
public static class GitHubReleaseParser
{
    public static UpdateCheckResult Parse(string releaseJson, ApplicationVersion currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseJson);
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var document = JsonDocument.Parse(releaseJson);
        var root = document.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProperty) ? tagProperty.GetString() : null;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return UpdateCheckResult.Failure("最新リリースの情報を取得できませんでした。");
        }

        ApplicationVersion latest;
        try
        {
            latest = ApplicationVersion.Parse(tagName.TrimStart('v', 'V'));
        }
        catch (FormatException)
        {
            return UpdateCheckResult.Failure($"最新リリースのバージョン表記を解釈できませんでした（{tagName}）。");
        }

        if (!latest.IsNewerThan(currentVersion))
        {
            return UpdateCheckResult.UpToDate();
        }

        var htmlUrl = root.TryGetProperty("html_url", out var urlProperty) ? urlProperty.GetString() : null;
        return UpdateCheckResult.Available(
            latest,
            htmlUrl ?? $"https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/{tagName}");
    }
}
