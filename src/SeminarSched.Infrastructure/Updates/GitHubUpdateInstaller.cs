using System.Diagnostics;
using SeminarSched.Application.Updates;

namespace SeminarSched.Infrastructure.Updates;

/// <summary>
/// <see cref="GitHubReleaseParser"/>が見つけたSetup.exe資産を一時フォルダーへダウンロードし、
/// 既定のプロセスとして起動する。起動したSetup.exeはPrivilegesRequired=adminのため、Windowsが
/// UACの昇格プロンプトを自動的に表示する（<see cref="ProcessStartInfo.UseShellExecute"/>により、
/// マニフェストどおりの昇格要求がそのまま働く）。インストール本体（証明書信頼・既存アプリの強制
/// 終了・Add-AppxPackage）はSetup.exe側（installer/Install-Package.ps1の-ForceApplicationShutdown）
/// が行うため、このクラスはダウンロードと起動だけを担う。
/// </summary>
public sealed class GitHubUpdateInstaller : IUpdateInstaller, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<string> DownloadAsync(string downloadUrl, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);

        var destinationPath = Path.Combine(Path.GetTempPath(), $"SeminarSched.WinUI-Update-{Guid.NewGuid():N}.exe");
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (totalBytes is > 0) progress?.Report((double)readTotal / totalBytes.Value);
        }

        return destinationPath;
    }

    public bool Launch(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        var started = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        return started is not null;
    }

    public void Dispose() => _httpClient.Dispose();
}
