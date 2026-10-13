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
            htmlUrl ?? $"https://github.com/SotaFurukawa/SeminarSched.WinUI/releases/tag/{tagName}",
            FindInstallerAssetUrl(root));
    }

    // Setup.exe（Inno Setup）資産を探す。ファイル名の規約（SeminarSched.WinUI-Setup-<version>.exe）を
    // 決め打ちせず、拡張子が.exeの資産を探すことで、命名規則が将来変わっても壊れないようにする。
    private static string? FindInstallerAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
            if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (asset.TryGetProperty("browser_download_url", out var downloadUrlProperty))
            {
                return downloadUrlProperty.GetString();
            }
        }

        return null;
    }
}
