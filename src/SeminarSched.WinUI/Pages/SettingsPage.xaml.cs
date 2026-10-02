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
        OpenReleaseButton.Visibility = Visibility.Collapsed;
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
                OpenReleaseButton.Visibility = Visibility.Visible;
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

    private async void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_latestReleaseUrl is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_latestReleaseUrl));
        }
    }
}
