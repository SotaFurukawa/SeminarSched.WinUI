using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        RefreshOptimizationStatus();

        // 3.1/3.2は、開いている（または作成時に）「集団授業の日程を考慮する」を有効にしたプロジェクトの
        // ときだけ表示する。プロジェクト未選択・当該オプション無効のプロジェクトでは常時非表示にする。
        App.ProjectService.Changed += OnProjectServiceChanged;
        Closed += (_, _) => App.ProjectService.Changed -= OnProjectServiceChanged;
        UpdateGroupLessonNavVisibility();
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
        OptimizationStatusPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running) return;
        var (percent, _, remaining) = OptimizationRunState.Estimate();
        OptimizationStatusBar.Value = percent;
        OptimizationStatusText.Text = $"{percent:F0}%　残り目安 {FormatDuration(remaining)}";
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
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
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
}
