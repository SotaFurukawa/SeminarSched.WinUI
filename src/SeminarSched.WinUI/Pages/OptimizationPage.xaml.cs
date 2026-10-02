using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SeminarSched.Domain.Scheduling;
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
        _runPolicyController = new SchedulingPolicyRowsController(RunPolicyRows, RunPolicyRowsList);
    }

    public OptimizationQualityViewModel ViewModel { get; } = new();

    // ユーザー要望（checkpoint126）「探索方針について、設定だけでなく、自動作成ページの一時的な
    // ものでもできるようにしてほしい」への対応。並び替えUI自体はSchedulingPolicyRowsControllerへ
    // 切り出し、「①設定」のPolicyRowsと同じ仕組みを共有している。
    public ObservableCollection<SchedulingPolicyRowViewModel> RunPolicyRows { get; } = new();

    private readonly SchedulingPolicyRowsController _runPolicyController;

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
            if (ready) await LoadRunPolicyAsync(App.ProjectService.Current!.Path);
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

    // ユーザー要望「両方（プロジェクトの既定値＋実行時に上書き可）」への対応。この画面を開いたときは
    // プロジェクトに保存済みの方針をそのまま表示し（＝何も変更しなければ既定値通りに実行される）、
    // ユーザーがこの画面だけで変更した内容は、実行時に一度だけ渡すoverrideとして使う
    // （「①設定」側の保存済み既定値そのものは変更しない）。
    // ユーザー要望（checkpoint126）「探索方針について、設定だけでなく、自動作成ページの一時的な
    // ものでもできるようにしてほしい」への対応。優先度の並び順（PreferenceOrder）もRunPolicyRows
    // として読み込み、この画面だけで一時的に並び替えられるようにした（「①設定」側は変更しない）。
    private async Task LoadRunPolicyAsync(string projectPath)
    {
        var policy = await App.SchedulingPolicy.GetAsync(projectPath);
        RunPolicyMaxStudentsPerTeacher.Value = policy.MaxStudentsPerTeacher;
        _runPolicyController.Load(policy);
        RunPolicyMaxConcurrentSeats.Value = policy.MaxConcurrentSeats;
        RunPolicyContinueBeyondNominalTimeYes.IsChecked = policy.ContinueBeyondNominalTimeIfIncomplete;
        RunPolicyContinueBeyondNominalTimeNo.IsChecked = !policy.ContinueBeyondNominalTimeIfIncomplete;
    }

    private void RunPolicyOption_Loaded(object sender, RoutedEventArgs e) => _runPolicyController.OptionLoaded(sender, e);

    private void RunPolicyOption_SelectionChanged(object sender, SelectionChangedEventArgs e) => _runPolicyController.OptionSelectionChanged(sender, e);

    private void MoveRunPolicyRowUp_Click(object sender, RoutedEventArgs e) => _runPolicyController.MoveUp(sender, e);

    private void MoveRunPolicyRowDown_Click(object sender, RoutedEventArgs e) => _runPolicyController.MoveDown(sender, e);

    private void RunPolicyRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SchedulingPolicyRowsController.RowPointerEntered(sender, e);

    private void RunPolicyRow_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulingPolicyRowsController.RowPointerExited(sender, e);

    private SchedulingPolicy BuildRunPolicyOverride() => SchedulingPolicyRowViewModel.BuildPolicy(
        RunPolicyRows,
        checked((int)RunPolicyMaxStudentsPerTeacher.Value),
        checked((int)RunPolicyMaxConcurrentSeats.Value),
        RunPolicyContinueBeyondNominalTimeNo.IsChecked != true);

    private void Page_Unloaded(object sender, RoutedEventArgs e) => OptimizationRunState.Changed -= OnRunStateChanged;

    private void OnRunStateChanged() => DispatcherQueue.TryEnqueue(RefreshRunUi);

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = App.ProjectService.Current!.Path;
            var profile = OptimizationProfileCatalog.Get(ViewModel.Level);
            await OptimizationRunState.StartAsync(path, profile, UnrestrictedResourceUsageCheckBox.IsChecked == true, BuildRunPolicyOverride());
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
        PauseButton.IsEnabled = false;
        RunStageText.Text = "現在の結果を採用しています…";
    }

    // ユーザー要望「一時停止ボタンを作ってほしい」（checkpoint99）。CP-SATの探索そのものは途中で
    // 止めて後から続きから再開することができないため、次の戦略・次の延長パスを開始する直前でだけ
    // 止める方式（OptimizationRunControl参照）。要求してから実際に止まるまで、実行中の1戦略・
    // 1延長パス分のタイムラグがありうる。
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (OptimizationRunState.IsPauseRequested) OptimizationRunState.ResumeFromPause();
        else OptimizationRunState.RequestPause();
        RefreshRunUi();
    }

    // ⑤の実行状態はOptimizationRunState（アプリ全体で1つ）が持つため、このPageは1秒ごとのTickや
    // 進捗報告のたびに現在の状態を読み直して表示するだけでよい。実行中に画面を離れて戻ってきても
    // （Frame.Navigateは新しいPageインスタンスを作るが）ここで最新状態をそのまま反映できる。
    private void RefreshRunUi()
    {
        var running = OptimizationRunState.IsRunning;
        var pauseRequested = OptimizationRunState.IsPauseRequested;
        var benchmarking = OptimizationRunState.IsBenchmarking;
        BenchmarkingInfoBar.IsOpen = benchmarking;
        RunButton.IsEnabled = !running && !benchmarking && App.ProjectService.Current is not null;
        PauseButton.IsEnabled = running;
        PauseButton.Content = pauseRequested ? "再開" : "一時停止";
        AcceptBestButton.IsEnabled = running;
        RunProgress.IsActive = running && !OptimizationRunState.IsPaused;
        if (running || OptimizationRunState.LatestProgress is not null) ProgressPanel.Visibility = Visibility.Visible;

        PausedInfoBar.IsOpen = running && pauseRequested;
        PausedInfoBar.Message = OptimizationRunState.IsPaused
            ? "「再開」を押すまで、次の探索を開始しません。"
            : "現在の探索が終わり次第、一時停止します（多少お待ちください）。";

        var (percent, elapsed, remaining) = OptimizationRunState.Estimate();
        RunProgressBar.Value = percent;
        RunPercentText.Text = $"{percent:F0}%";
        var remainingText = remaining is { } remainingValue ? FormatDuration(remainingValue) : "計算中…";
        RunEtaText.Text = OptimizationRunState.LatestProgress is null ? "" : $"経過 {FormatDuration(elapsed)} / 残り目安 {remainingText}";

        if (OptimizationRunState.LatestProgress is { } progress)
        {
            var stageLabel = StageLabels.GetValueOrDefault(progress.Stage, progress.Stage.ToString());
            var strategyLabel = StrategyLabels.GetValueOrDefault(progress.Strategy, progress.Strategy.ToString());
            RunStageText.Text = !running ? "完了しました。"
                : OptimizationRunState.IsPaused ? $"一時停止中（{stageLabel}：{strategyLabel}）"
                : $"{stageLabel}：{strategyLabel}（{progress.StrategiesCompleted}/{progress.StrategiesTotal}戦略）";
            ExtendingInfoBar.IsOpen = running && progress.IsExtending;
        }

        var outcome = OptimizationRunState.ConsumeLastOutcome();
        if (outcome is null) return;
        if (outcome is { Success: true, Summary: { } result })
        {
            var extendedNote = result.WasExtended ? "（指定した時間内には完成しなかったため延長して探索しました）" : "";
            var baseMessage = $"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒（採用戦略: {StrategyDisplayName(result.StrategyLabel)}）{extendedNote}。④時間割編集で変更内容を確認できます。";
            if (result.UnassignedLessons > 0)
            {
                // ユーザー報告「2倍の時間をかけてしまうと...何も生み出していないことになります」への対応。
                // 未配置が残った結果を、以前は他の完成ケースと同じ緑のSuccess表示で埋もれさせていた
                // （メッセージ文中に件数はあったが、見た目上は「成功しました」にしか見えない）。未配置が
                // 1件でもあれば、無理やり作った不完全な時間割であることが一目で分かるようWarning表示にする。
                RunStatus.Severity = InfoBarSeverity.Warning;
                RunStatus.Title = $"未配置が{result.UnassignedLessons}件残ったまま作成しました（要確認）";
                var reasons = new List<string>();
                if (result.UnassignedDueToRegularTeacherPriority > 0)
                    reasons.Add($"うち{result.UnassignedDueToRegularTeacherPriority}件は、担当講師優先度が「5（固定）」に設定されている生徒です。優先度5は通常担当講師・第1〜3希望講師以外を絶対に使わないため、これらの講師の空きコマ不足が原因です。「①設定」の「通常授業担当設定」タブで優先度や、「講師指導可能科目」タブ・アンケート回答で講師の出勤可否を見直してください。");
                if (result.UnassignedWithNoQualifiedTeacher > 0)
                    reasons.Add($"うち{result.UnassignedWithNoQualifiedTeacher}件は、対応できる講師の候補コマが構造的に見つかりませんでした（講師の資格・出勤可否をご確認ください）。時間をかけても解決しません。");
                var reasonNote = reasons.Count > 0 ? " " + string.Join(" ", reasons) : "";
                RunStatus.Message = baseMessage + reasonNote;
            }
            else
            {
                RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "時間割を作成しました";
                RunStatus.Message = baseMessage;
            }
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

    private void GoToOutput_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OutputPage));

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
