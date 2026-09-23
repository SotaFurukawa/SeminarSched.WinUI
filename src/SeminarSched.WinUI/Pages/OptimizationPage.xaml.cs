using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;
using SeminarSched_WinUI.ViewModels;

namespace SeminarSched_WinUI.Pages;

public sealed partial class OptimizationPage : WorkflowPageBase
{
    private CancellationTokenSource? _saveDebounce;
    private bool _isLoaded;

    private static readonly Dictionary<OptimizationStageKind, string> StageLabels = new()
    {
        [OptimizationStageKind.InitialExploration] = "初期探索",
        [OptimizationStageKind.CandidateAdvancement] = "候補改善",
        [OptimizationStageKind.NeighborhoodRepair] = "近傍再探索",
        [OptimizationStageKind.FinalPolishing] = "仕上げ探索",
        [OptimizationStageKind.Extension] = "延長探索",
    };

    private static readonly Dictionary<OptimizationStrategyKind, string> StrategyLabels = new()
    {
        [OptimizationStrategyKind.StandardCpSat] = "標準探索",
        [OptimizationStrategyKind.SeededCpSatA] = "多重試行A",
        [OptimizationStrategyKind.SeededCpSatB] = "多重試行B",
        [OptimizationStrategyKind.SeededCpSatC] = "多重試行C",
        [OptimizationStrategyKind.AlternateDecision] = "別探索方式",
        [OptimizationStrategyKind.MultiStage] = "並列強化探索",
        [OptimizationStrategyKind.HintImprovement] = "改善探索",
        [OptimizationStrategyKind.NeighborhoodRepair] = "部分修復探索",
        [OptimizationStrategyKind.NeighborhoodRepairB] = "部分修復探索B",
        [OptimizationStrategyKind.NeighborhoodRepairC] = "部分修復探索C",
        [OptimizationStrategyKind.NeighborhoodRepairD] = "部分修復探索D",
        [OptimizationStrategyKind.NeighborhoodRepairE] = "部分修復探索E",
        [OptimizationStrategyKind.GrindingNeighborhoodRepair] = "部分修復探索（連続）",
        [OptimizationStrategyKind.FinalPolishing] = "最終仕上げ探索",
        [OptimizationStrategyKind.FinalPolishingB] = "最終仕上げ探索B",
        [OptimizationStrategyKind.GrindingFinalPolishing] = "最終仕上げ探索（連続）",
    };

    public OptimizationPage()
    {
        InitializeComponent();
        Unloaded += Page_Unloaded;
    }

    public OptimizationQualityViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AlignQualityTickLabels();
        OptimizationRunState.Changed += OnRunStateChanged;
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            ViewModel.Select((int)settings.OptimizationQualityLevel);
            QualitySlider.Value = ViewModel.SliderValue;
            UnrestrictedResourceUsageCheckBox.IsChecked = settings.UnrestrictedResourceUsage;
            _isLoaded = true;
            var ready = EnsureProject(ProjectRequired);
            ContentPanel.IsEnabled = ready;
            RefreshRunUi();
        }
        catch (IOException)
        {
            SaveErrorInfoBar.IsOpen = true;
            _isLoaded = true;
        }
        catch (UnauthorizedAccessException)
        {
            SaveErrorInfoBar.IsOpen = true;
            _isLoaded = true;
        }
    }

    // WinUI既定テーマのSliderは、tickをthumb本体の半径ぶんだけ左右へ内側寄せした位置に描画する
    // （thumb幅=SliderHorizontalThumbWidthテーマリソース、既定18px→半径9px）。固定pxを決め打ちすると
    // 実際のテーマ値とズレて目盛り数字が正しい位置に来ないため、テーマリソースから実測して反映する。
    private void AlignQualityTickLabels()
    {
        var thumbWidth = Application.Current.Resources.TryGetValue("SliderHorizontalThumbWidth", out var value) && value is double width ? width : 18d;
        var inset = thumbWidth / 2;
        QualityTickLabels.Margin = new Thickness(inset, 4, inset, 0);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => OptimizationRunState.Changed -= OnRunStateChanged;

    private void OnRunStateChanged() => DispatcherQueue.TryEnqueue(RefreshRunUi);

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = App.ProjectService.Current!.Path;
            var profile = OptimizationProfileCatalog.Get(ViewModel.Level);
            await OptimizationRunState.StartAsync(path, profile, UnrestrictedResourceUsageCheckBox.IsChecked == true);
            RefreshRunUi();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            RunStatus.Severity = InfoBarSeverity.Error; RunStatus.Title = "時間割を作成できませんでした"; RunStatus.Message = ex.Message; RunStatus.IsOpen = true;
        }
    }

    private void AcceptBest_Click(object sender, RoutedEventArgs e)
    {
        OptimizationRunState.AcceptCurrentBest();
        AcceptBestButton.IsEnabled = false;
        RunStageText.Text = "現在の結果を採用しています…";
    }

    // ⑤の実行状態はOptimizationRunState（アプリ全体で1つ）が持つため、このPageは1秒ごとのTickや
    // 進捗報告のたびに現在の状態を読み直して表示するだけでよい。実行中に画面を離れて戻ってきても
    // （Frame.Navigateは新しいPageインスタンスを作るが）ここで最新状態をそのまま反映できる。
    private void RefreshRunUi()
    {
        var running = OptimizationRunState.IsRunning;
        RunButton.IsEnabled = !running && App.ProjectService.Current is not null;
        AcceptBestButton.IsEnabled = running;
        RunProgress.IsActive = running;
        if (running || OptimizationRunState.LatestProgress is not null) ProgressPanel.Visibility = Visibility.Visible;

        var (percent, elapsed, remaining) = OptimizationRunState.Estimate();
        RunProgressBar.Value = percent;
        RunPercentText.Text = $"{percent:F0}%";
        var remainingText = remaining is { } remainingValue ? FormatDuration(remainingValue) : "計算中…";
        RunEtaText.Text = OptimizationRunState.LatestProgress is null ? "" : $"経過 {FormatDuration(elapsed)} / 残り目安 {remainingText}";

        if (OptimizationRunState.LatestProgress is { } progress)
        {
            var stageLabel = StageLabels.GetValueOrDefault(progress.Stage, progress.Stage.ToString());
            var strategyLabel = StrategyLabels.GetValueOrDefault(progress.Strategy, progress.Strategy.ToString());
            RunStageText.Text = running ? $"{stageLabel}：{strategyLabel}（{progress.StrategiesCompleted}/{progress.StrategiesTotal}戦略）" : "完了しました。";
            ExtendingInfoBar.IsOpen = running && progress.IsExtending;
        }

        var outcome = OptimizationRunState.ConsumeLastOutcome();
        if (outcome is null) return;
        if (outcome is { Success: true, Summary: { } result })
        {
            RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "時間割を作成しました";
            var extendedNote = result.WasExtended ? "（指定した時間内には完成しなかったため延長して探索しました）" : "";
            RunStatus.Message = $"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒（採用戦略: {StrategyDisplayName(result.StrategyLabel)}）{extendedNote}。④時間割編集で変更内容を確認できます。";
            RunStatus.IsOpen = true;
        }
        else
        {
            RunStatus.Severity = InfoBarSeverity.Error; RunStatus.Title = "時間割を作成できませんでした"; RunStatus.Message = outcome.ErrorMessage ?? ""; RunStatus.IsOpen = true;
        }
    }

    private static string FormatDuration(TimeSpan span) => span.TotalMinutes>=1?$"{(int)span.TotalMinutes}分{span.Seconds}秒":$"{span.TotalSeconds:F0}秒";

    private string StrategyDisplayName(string strategyLabel) =>
        Enum.TryParse<OptimizationStrategyKind>(strategyLabel, out var kind) ? StrategyLabels.GetValueOrDefault(kind,strategyLabel) : strategyLabel;

    private void GoToEditor_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ScheduleEditorPage));

    private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Select(e.NewValue);
        if (!_isLoaded)
        {
            return;
        }

        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _saveDebounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_saveDebounce.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            var settings = await App.SettingsStore.LoadAsync(cancellationToken);
            await App.SettingsStore.SaveAsync(settings with { OptimizationQualityLevel = ViewModel.Level }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
        catch (UnauthorizedAccessException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
    }

    private async void UnrestrictedResourceUsageCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            await App.SettingsStore.SaveAsync(settings with { UnrestrictedResourceUsage = UnrestrictedResourceUsageCheckBox.IsChecked == true });
        }
        catch (IOException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
        catch (UnauthorizedAccessException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
    }
}
