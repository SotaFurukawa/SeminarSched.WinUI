using System.Net;
using System.Net.Http.Headers;
using SeminarSched.Application;
using SeminarSched.Application.Updates;

namespace SeminarSched.Infrastructure.Updates;

/// <summary>
/// GitHub Releases APIを無認証で呼び出す。本リポジトリはpublicのため、公開済み（Draft/Prerelease
/// を除く）releaseの一覧・最新releaseは認証不要で取得できる（詳細はdocs/adr/0007-auto-update.md）。
/// </summary>
public sealed class GitHubUpdateCheckService : IUpdateCheckService, IDisposable
{
    private const string ReleasesLatestUrl = "https://api.github.com/repos/SotaFurukawa/SeminarSched.WinUI/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubUpdateCheckService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SeminarSched.WinUI", "1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(ApplicationVersion currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ReleasesLatestUrl, cancellationToken).ConfigureAwait(false);

            // 公開済み（Draftではない）releaseが1件も無い場合。本リポジトリの開発中はこれが通常状態。
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.UpToDate();
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure($"GitHubへの問い合わせに失敗しました（HTTP {(int)response.StatusCode}）。");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return GitHubReleaseParser.Parse(json, currentVersion);
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResult.Failure("通信がタイムアウトしました。ネットワーク接続を確認してください。");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failure($"ネットワークエラーが発生しました: {ex.Message}");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
