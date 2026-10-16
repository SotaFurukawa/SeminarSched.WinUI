// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeminarSched_WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private string? _latestReleaseUrl;
    private string? _latestInstallerDownloadUrl;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await App.SettingsStore.LoadAsync();
        LicenseStatusTextBlock.Text = settings.ProductKeyLicenseLabel is { } label
            ? $"認証済み（{label}）"
            : "未認証";
        UpdateStatusTextBlock.Text = settings.LastUpdateCheckUtc is { } lastChecked
            ? $"前回確認: {lastChecked.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "まだ確認していません。";
    }

    // ユーザー要望（checkpoint129）「これは動作確認のためでもあるが、ライセンスの解除が
    // できるようにしてほしい」への対応。保存済みのプロダクトキー認証状態（マスターキー／年度）を
    // 消去する。次回起動時（MainWindowのRootGrid_Loadedで毎回判定している）に再度プロダクトキーの
    // 入力が必要になる。
    private async void ReleaseLicense_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "ライセンスを解除しますか？",
            Content = "次回このアプリを起動したときに、再度プロダクトキーの入力が必要になります。",
            PrimaryButtonText = "解除する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var settings = await App.SettingsStore.LoadAsync();
        await App.SettingsStore.SaveAsync(settings with { ProductKeyIsMaster = false, ProductKeyYear = null });
        LicenseStatusTextBlock.Text = "未認証（次回起動時に再度プロダクトキーの入力が必要です）";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        PerformUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatusTextBlock.Text = "確認しています…";
        try
        {
            var currentVersion = ApplicationVersion.FromAssembly(typeof(App).Assembly);
            var result = await App.UpdateCheck.CheckForUpdateAsync(currentVersion);
            if (!result.Succeeded)
            {
                UpdateStatusTextBlock.Text = result.ErrorMessage ?? "確認に失敗しました。";
                return;
            }

            var settings = await App.SettingsStore.LoadAsync();
            await App.SettingsStore.SaveAsync(settings with { LastUpdateCheckUtc = DateTimeOffset.UtcNow });

            if (result.IsUpdateAvailable && result.LatestVersion is not null)
            {
                UpdateStatusTextBlock.Text = $"新しいバージョン（{result.LatestVersion.DisplayVersion}）が利用可能です。現在のバージョン: {currentVersion.DisplayVersion}";
                _latestReleaseUrl = result.ReleaseUrl;
                _latestInstallerDownloadUrl = result.InstallerDownloadUrl;
                PerformUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusTextBlock.Text = $"最新バージョンです（{currentVersion.DisplayVersion}）。";
            }
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    // ユーザー指示（「ダウンロードページを開くボタンを作るのではなく、アップデートを行うボタンを
    // 作る感じで、それが押されたら自動的にアップデート」）への対応。インストーラーをアプリ内で
    // ダウンロードし、そのまま起動する。Setup.exeはPrivilegesRequired=adminのため、起動時に
    // WindowsがUACの昇格プロンプトを表示する。その先のインストール本体（証明書信頼・このアプリの
    // 強制終了・Add-AppxPackage）はSetup.exe側が行うため、起動後はユーザーの操作を待つだけでよい。
    private async void PerformUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_latestInstallerDownloadUrl is null)
        {
            // インストーラー資産が見つからない場合の保険。通常はここへ来ない想定。
            if (_latestReleaseUrl is not null)
            {
                UpdateStatusTextBlock.Text = "自動更新用のインストーラーが見つからなかったため、リリースページを開きます。";
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_latestReleaseUrl));
            }
            return;
        }

        PerformUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(ratio => UpdateStatusTextBlock.Text = $"ダウンロード中…（{ratio:P0}）");
            var installerPath = await App.UpdateInstaller.DownloadAsync(_latestInstallerDownloadUrl, progress);
            UpdateStatusTextBlock.Text = "インストーラーを起動しています…";
            var started = App.UpdateInstaller.Launch(installerPath);
            UpdateStatusTextBlock.Text = started
                ? "インストーラーを起動しました。表示される画面の案内に従って更新を進めてください（このアプリは更新の途中で自動的に終了します）。"
                : "インストーラーを起動できませんでした。";
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            UpdateStatusTextBlock.Text = $"アップデートに失敗しました: {exception.Message}";
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            PerformUpdateButton.IsEnabled = true;
        }
    }

    // ユーザー要望「こういう不具合が発生したときのためにアンインストールボタンを追加したい」への
    // 対応。インストーラー（Inno Setup）がWindowsへ登録した標準のアンインストーラーを起動する。
    // プロジェクトファイルや共通名簿等のユーザーデータは{app}フォルダーの外（%LOCALAPPDATA%\
    // SeminarSched.WinUI）にあり、インストーラー側に削除処理が無いため、アンインストールしても
    // 消えない（installer/SeminarSched.WinUI.issの該当コメント参照）。
    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "アンインストールしますか？",
            Content = "Windows標準のアンインストーラーを起動します。プロジェクトファイルや共通名簿等のデータは削除されません（アプリ本体だけが削除されます）。このアプリは終了します。",
            PrimaryButtonText = "アンインストールする",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var uninstallCommand = App.AppUninstaller.FindUninstallCommand();
        if (uninstallCommand is null)
        {
            UninstallStatusTextBlock.Text = "アンインストーラーが見つかりませんでした。「設定」の「アプリ」>「インストールされているアプリ」から手動でアンインストールしてください。";
            return;
        }

        var started = App.AppUninstaller.Launch(uninstallCommand);
        UninstallStatusTextBlock.Text = started
            ? "アンインストーラーを起動しました。表示される画面の案内に従って進めてください。"
            : "アンインストーラーを起動できませんでした。";
    }
}
