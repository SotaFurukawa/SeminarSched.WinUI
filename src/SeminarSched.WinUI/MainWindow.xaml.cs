using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SeminarSched.Application;
using SeminarSched.Application.Settings;
using SeminarSched.Application.Updates;
using SeminarSched.Domain.Licensing;
using SeminarSched_WinUI.Controls;
using SeminarSched_WinUI.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeminarSched_WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // ⑤時間割自動作成は別画面(OptimizationPage)に切り替えても裏で継続するため、どの画面を
        // 見ていても進行状況が分かるようナビゲーションペイン下部（既定のSettings項目のすぐ上）に
        // 常設インジケーターを表示する。
        OptimizationRunState.Changed += OnOptimizationRunStateChanged;
        Closed += (_, _) => OptimizationRunState.Changed -= OnOptimizationRunStateChanged;
        NavView.DisplayModeChanged += (_, _) => RefreshOptimizationStatus();
        RefreshOptimizationStatus();

        // 3.1/3.2は、開いている（または作成時に）「集団授業の日程を考慮する」を有効にしたプロジェクトの
        // ときだけ表示する。プロジェクト未選択・当該オプション無効のプロジェクトでは常時非表示にする。
        App.ProjectService.Changed += OnProjectServiceChanged;
        Closed += (_, _) => App.ProjectService.Changed -= OnProjectServiceChanged;
        UpdateGroupLessonNavVisibility();

        // ユーザー要望（checkpoint122）「左側のタブにもこれらを選択できるようにしておきたい。
        // 設定を開いている間は...設定が開かれていないときはこれを表示しない」への対応。
        SetupPageNavState.Changed += OnSetupPageNavStateChanged;
        Closed += (_, _) => SetupPageNavState.Changed -= OnSetupPageNavStateChanged;
        RefreshSetupNavState();

        // ユーザー要望「保存などの成功通知を、iPhoneの通知のように上部からスライドインさせたい」
        // への対応。各ページはToastNotificationState.ShowSuccess()を呼ぶだけで、実際の表示は
        // ここ（ウィンドウに1つだけ存在するオーバーレイ）が担う。
        ToastNotificationState.Requested += OnToastRequested;
        Closed += (_, _) => ToastNotificationState.Requested -= OnToastRequested;
    }

    private bool _productKeyCheckStarted;

    // ユーザー要望（checkpoint124）「プロダクトキーを実装したい。アプリの初回立ち上げ時に
    // プロダクトキーを要求するようにする。2/1以降にアプリが立ち上げられた時も再度要求する」への
    // 対応。ContentDialogにはXamlRootが必要だが、コンストラクタ直後やActivate直後の時点では
    // まだ存在しない（実機確認: 無言で例外が握りつぶされ、ダイアログが一切表示されなかった）ため、
    // ルートGridのLoaded（実際にvisual treeへ接続された後）まで遅延させる
    // （詳細はdocs/adr/0006-product-key-licensing.md）。
    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_productKeyCheckStarted)
        {
            return;
        }

        _productKeyCheckStarted = true;
        _ = InitializeStartupChecksAsync();
    }

    private async Task InitializeStartupChecksAsync()
    {
        if (!await EnsureProductKeyAuthorizedAsync())
        {
            return;
        }

        await CheckForUpdateIfDueAsync();
    }

    private async Task<bool> EnsureProductKeyAuthorizedAsync()
    {
        var settings = await App.SettingsStore.LoadAsync();
        if (IsLicensed(settings))
        {
            ApplyLicenseLabel(settings);
            return true;
        }

        ProductKeyValidationResult? accepted = null;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "プロダクトキーの入力",
            PrimaryButtonText = "認証",
            CloseButtonText = "終了",
            DefaultButton = ContentDialogButton.Primary,
        };

        var keyEntry = new ProductKeyEntryControl();
        var errorText = new TextBlock
        {
            Text = "プロダクトキーが正しくないか、期限が切れています。",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        dialog.Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "ShikiWariを利用するにはプロダクトキーを入力してください。プロダクトキーを所持していない、または不明な場合は契約事業者にお問い合わせください。",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                },
                keyEntry,
                errorText,
            },
        };
        var successPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 16),
            Children =
            {
                new TextBlock { Text = "Success", FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new FontIcon { Glyph = "", FontSize = 28, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"], VerticalAlignment = VerticalAlignment.Center },
            },
        };

        dialog.Opened += (_, _) => keyEntry.FocusFirst();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var result = ProductKeyService.Validate(keyEntry.Value, DateTimeOffset.Now);
            if (!result.IsValid)
            {
                args.Cancel = true;
                errorText.Visibility = Visibility.Visible;
                keyEntry.ResetAndFocus();
                return;
            }

            accepted = result;

            // ユーザー要望「正しいプロダクトキーが入力された場合には、成功画面を出してほしい」への
            // 対応。args.Cancel=trueでダイアログの自動クローズを止め、入力フォームを「Success」＋
            // 緑色のチェックマークへ短時間差し替えてからHide()で閉じる。Deferralを使い、この
            // 非同期処理（表示の差し替え・待機）が終わるまでPrimaryButtonClickの処理完了を遅らせる。
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.Content = successPanel;
                dialog.PrimaryButtonText = string.Empty;
                dialog.CloseButtonText = string.Empty;
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
                dialog.Hide();
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
        if (accepted is not { } validated)
        {
            Application.Current.Exit();
            return false;
        }

        settings = settings with
        {
            ProductKeyIsMaster = validated.IsMaster,
            ProductKeyYear = validated.IsMaster ? null : validated.Year,
        };
        await App.SettingsStore.SaveAsync(settings);
        ApplyLicenseLabel(settings);
        return true;
    }

    private static bool IsLicensed(AppSettings settings) =>
        settings.ProductKeyIsMaster || settings.ProductKeyYear == ProductKeyService.CurrentPeriodYear(DateTimeOffset.Now);

    private void ApplyLicenseLabel(AppSettings settings)
    {
        var title = settings.ProductKeyLicenseLabel is { } suffix ? $"ShikiWari {suffix}" : "ShikiWari";
        Title = title;
        AppTitleBar.Title = title;
    }

    // ユーザー要望（checkpoint125）「週に1度、アップデートがないかのチェックを行い、もしあるなら
    // アップデートをするかの警告を出すようにする」への対応。GitHub Releases APIへの問い合わせに
    // 成功した場合のみLastUpdateCheckUtcを更新する（失敗時は次回起動時に再試行させるため）。
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(7);

    private async Task CheckForUpdateIfDueAsync()
    {
        var settings = await App.SettingsStore.LoadAsync();
        var now = DateTimeOffset.UtcNow;
        if (settings.LastUpdateCheckUtc is { } lastChecked && now - lastChecked < UpdateCheckInterval)
        {
            return;
        }

        var currentVersion = ApplicationVersion.FromAssembly(typeof(App).Assembly);
        var result = await App.UpdateCheck.CheckForUpdateAsync(currentVersion);
        if (!result.Succeeded)
        {
            return;
        }

        await App.SettingsStore.SaveAsync(settings with { LastUpdateCheckUtc = now });

        if (result.IsUpdateAvailable && result.LatestVersion is not null)
        {
            await ShowUpdateAvailableDialogAsync(currentVersion, result);
        }
    }

    private async Task ShowUpdateAvailableDialogAsync(ApplicationVersion currentVersion, UpdateCheckResult result)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "新しいバージョンがあります",
            Content = $"現在のバージョン: {currentVersion.DisplayVersion}\n最新バージョン: {result.LatestVersion!.DisplayVersion}\n\nダウンロードページを開きますか？",
            PrimaryButtonText = "ダウンロードページを開く",
            CloseButtonText = "後で",
            DefaultButton = ContentDialogButton.Primary,
        };

        var dialogResult = await dialog.ShowAsync();
        if (dialogResult == ContentDialogResult.Primary && result.ReleaseUrl is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(result.ReleaseUrl));
        }
    }

    private bool _syncingSetupNav;

    private void OnSetupPageNavStateChanged() => DispatcherQueue.TryEnqueue(RefreshSetupNavState);

    private void RefreshSetupNavState()
    {
        SetupNavItem.IsExpanded = SetupPageNavState.IsActive;
        if (!SetupPageNavState.IsActive) return;
        var tag = $"setupTab:{SetupPageNavState.SelectedTabIndex}";
        foreach (var child in SetupNavItem.MenuItems.OfType<NavigationViewItem>())
        {
            if (child.Tag as string != tag) continue;
            _syncingSetupNav = true;
            NavView.SelectedItem = child;
            _syncingSetupNav = false;
            break;
        }
    }

    private void OnProjectServiceChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateGroupLessonNavVisibility);

    private void UpdateGroupLessonNavVisibility()
    {
        var visibility = App.ProjectService.Current?.ConsiderGroupLessons == true ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is "groupLessonClass" or "groupLessonEnrollment") item.Visibility = visibility;
        }
    }

    private void OnOptimizationRunStateChanged() => DispatcherQueue.TryEnqueue(RefreshOptimizationStatus);

    private void RefreshOptimizationStatus()
    {
        var running = OptimizationRunState.IsRunning;
        var isPaneCompact = NavView.DisplayMode != NavigationViewDisplayMode.Expanded;
        var showCompact = running && isPaneCompact;
        var showExpanded = running && !isPaneCompact;
        OptimizationStatusPanelCompact.Visibility = showCompact ? Visibility.Visible : Visibility.Collapsed;
        OptimizationStatusPanelExpanded.Visibility = showExpanded ? Visibility.Visible : Visibility.Collapsed;
        OptimizationStatusRing.IsActive = showCompact;
        if (!running) return;
        var (percent, _, remaining) = OptimizationRunState.Estimate();
        OptimizationStatusRing.Value = percent;
        OptimizationStatusRingText.Text = $"{percent:F0}%";
        OptimizationStatusBar.Value = percent;
        var remainingText = remaining is { } remainingValue ? FormatDuration(remainingValue) : "計算中…";
        OptimizationStatusText.Text = $"{percent:F0}%　残り目安 {remainingText}";
    }

    private static string FormatDuration(TimeSpan span) => span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}分{span.Seconds}秒" : $"{span.TotalSeconds:F0}秒";

    private void OptimizationStatusPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        NavFrame.Navigate(typeof(OptimizationPage));
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if ((string)item.Tag == "optimization") { NavView.SelectedItem = item; break; }
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSetupNav) return;
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            if (item.Tag is string tabTag && tabTag.StartsWith("setupTab:", StringComparison.Ordinal))
            {
                var tabIndex = int.Parse(tabTag.AsSpan("setupTab:".Length));
                if (NavFrame.Content is SetupPage setupPage)
                {
                    setupPage.SelectTab(tabIndex);
                }
                else
                {
                    SetupPageNavState.RequestedTabIndex = tabIndex;
                    NavFrame.Navigate(typeof(SetupPage));
                }
                return;
            }
            switch (item.Tag)
            {
                case "home":
                    NavFrame.Navigate(typeof(HomePage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
                case "optimization":
                    NavFrame.Navigate(typeof(OptimizationPage));
                    break;
                case "setup":
                    NavFrame.Navigate(typeof(SetupPage));
                    break;
                case "questionnaire":
                    NavFrame.Navigate(typeof(QuestionnairePage));
                    break;
                case "import":
                    NavFrame.Navigate(typeof(ImportPage));
                    break;
                case "groupLessonClass":
                    NavFrame.Navigate(typeof(GroupLessonClassPage));
                    break;
                case "groupLessonEnrollment":
                    NavFrame.Navigate(typeof(GroupLessonEnrollmentPage));
                    break;
                case "scheduleEditor":
                    NavFrame.Navigate(typeof(ScheduleEditorPage));
                    break;
                case "output":
                    NavFrame.Navigate(typeof(OutputPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    // ユーザー要望「保存などの成功通知について、iPhoneの通知のように上部からスライドインして
    // くる形にしたい。2個目の通知が来た場合には、元々存在していた通知はスライドアウトさせ、
    // その後2個目の通知がスライドインしてくるように」への対応。TranslateTransform.Yをアニメー
    // ションさせてスライドイン・アウトを行う。InfoBar自体の開閉アニメーションには頼らず
    // （Visibility=Visibleのままにして）、自前で「表示中に次の通知が来たら先にスライドアウトを
    // 完了させてから次をスライドインする」という逐次処理を制御する。
    private const double ToastOffScreenY = -160;
    private bool _toastVisible;
    private string? _pendingToastMessage;
    private DispatcherTimer? _toastDismissTimer;

    private void OnToastRequested(string message) => DispatcherQueue.TryEnqueue(() => ShowToast(message));

    private void ShowToast(string message)
    {
        _toastDismissTimer?.Stop();

        if (_toastVisible)
        {
            _pendingToastMessage = message;
            AnimateToast(toOffScreen: true, onCompleted: () =>
            {
                ToastText.Text = _pendingToastMessage;
                _pendingToastMessage = null;
                AnimateToast(toOffScreen: false, onCompleted: StartToastAutoDismissTimer);
            });
            return;
        }

        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        AnimateToast(toOffScreen: false, onCompleted: StartToastAutoDismissTimer);
    }

    private void StartToastAutoDismissTimer()
    {
        _toastDismissTimer?.Stop();
        _toastDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastDismissTimer.Tick += (_, _) =>
        {
            _toastDismissTimer!.Stop();
            HideToast();
        };
        _toastDismissTimer.Start();
    }

    private void HideToast() => AnimateToast(toOffScreen: true, onCompleted: () => ToastBorder.Visibility = Visibility.Collapsed);

    private void AnimateToast(bool toOffScreen, Action? onCompleted)
    {
        _toastVisible = !toOffScreen;
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = toOffScreen ? ToastOffScreenY : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = toOffScreen ? Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn : Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, ToastTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Y");
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        if (onCompleted is not null)
        {
            storyboard.Completed += (_, _) => onCompleted();
        }
        storyboard.Begin();
    }

    private void ToastClose_Click(object sender, RoutedEventArgs e)
    {
        _toastDismissTimer?.Stop();
        HideToast();
    }
}
