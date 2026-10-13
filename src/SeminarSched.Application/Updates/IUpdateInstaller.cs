namespace SeminarSched.Application.Updates;

/// <summary>
/// ユーザー指示（「ダウンロードページを開くボタンを作るのではなく、アップデートを行うボタンを作る」）
/// への対応。<see cref="IUpdateCheckService"/>が見つけた最新releaseのインストーラーを、アプリ内から
/// 直接ダウンロード・起動する。起動後の実際のインストール（証明書信頼・Add-AppxPackage）は、
/// 既存のSetup.exe（Inno Setup）がUACの昇格を伴って行う。
/// </summary>
public interface IUpdateInstaller
{
    Task<string> DownloadAsync(string downloadUrl, IProgress<double>? progress, CancellationToken cancellationToken = default);

    bool Launch(string installerPath);
}
