using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Application.Scheduling;
using SeminarSched.Domain.Scheduling;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;
using SeminarSched_WinUI.ViewModels;
using Windows.UI;

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
            KeepExistingPlacementsCheckBox.IsChecked = settings.KeepExistingPlacements;
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

    // ユーザー要望（checkpoint155）「『この回だけ探索の方針を変更する』は設定と競合する場合がある
    // ため削除し、変更できないようにして設定で決められた優先度を表示するようにする」への対応。
    // 以前はこの画面だけの一時上書き（OptimizationPolicyOverrideState）を保持していたが、その
    // 仕組みごと廃止し、「1 プロジェクト設定」の「スケジュール設定」タブに保存されている内容を
    // そのまま読み取り専用で表示するだけにした。
    private async Task LoadRunPolicyAsync(string projectPath)
    {
        var policy = await App.SchedulingPolicy.GetAsync(projectPath);
        _runPolicyController.Load(policy);
        var seatsText = policy.MaxConcurrentSeats > 0 ? $"{policy.MaxConcurrentSeats}席" : "無制限";
        var continueText = policy.ContinueBeyondNominalTimeIfIncomplete
            ? "そのまま完成するまで（または中断するまで）継続する"
            : "そこで打ち切る";
        RunPolicySummaryText.Text =
            $"一人の講師が同時に担当できる生徒数: {policy.MaxStudentsPerTeacher}人 / " +
            $"同時に使える座席数上限: {seatsText} / " +
            $"既定の時間（名目時間の2倍）になっても未配置が残っている場合: {continueText}";
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        OptimizationRunState.Changed -= OnRunStateChanged;
    }

    private void OnRunStateChanged() => DispatcherQueue.TryEnqueue(RefreshRunUi);

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = App.ProjectService.Current!.Path;
            var profile = OptimizationProfileCatalog.Get(ViewModel.Level);
            ClearSwapSuggestions();
            await OptimizationRunState.StartAsync(path, profile, UnrestrictedResourceUsageCheckBox.IsChecked == true, null, KeepExistingPlacementsCheckBox.IsChecked == true);
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
            // ユーザー要望（checkpoint154）「少し時間を要していますの画面は、所要時間の想定の最大値の
            // 66%が経過したときに、進捗が66%を超えていないときに表示してください。これ以降は、所要
            // 時間と進捗割合を比較して、進捗割合が所要時間を超えていない場合に表示するように」への
            // 対応。従来はソルバー側のIsExtending（名目時間を100%使い切った後の延長フェーズ中か）
            // だけを見ていたため、警告が出るのがかなり遅かった。経過時間の想定最大値
            // （OptimizationRunState.MaximumDuration、品質プロファイルの名目上限）に対する経過割合
            // （elapsedRatio）を計算し、これが66%に達して以降、現在の進捗割合（percent/100）が
            // その時点のelapsedRatioを下回っている間ずっと表示し続ける（elapsedRatioは時間が進むに
            // つれ66%から連続的に増えていくため、「66%時点での固定比較」と「それ以降の動的な比較」を
            // 同じ1つの式でまかなえる）。
            var elapsedRatio = OptimizationRunState.MaximumDuration > TimeSpan.Zero
                ? elapsed.TotalSeconds / OptimizationRunState.MaximumDuration.TotalSeconds
                : 0.0;
            ExtendingInfoBar.IsOpen = running && elapsedRatio >= 0.66 && percent / 100.0 < elapsedRatio;
        }

        var outcome = OptimizationRunState.ConsumeLastOutcome();
        if (outcome is null) return;
        if (outcome is { Success: true, Summary: { } result })
        {
            var extendedNote = result.WasExtended ? "（指定した時間内には完成しなかったため延長して探索しました）" : "";
            var baseMessage = $"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒（採用戦略: {StrategyDisplayName(result.StrategyLabel)}）{extendedNote}。4 時間割編集で変更内容を確認できます。";
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
                    reasons.Add($"うち{result.UnassignedDueToRegularTeacherPriority}件は、担当講師優先度が「5（固定）」に設定されている生徒です。優先度5は通常担当講師・第1〜3希望講師以外を絶対に使わないため、これらの講師の空きコマ不足が原因です。「1 プロジェクト設定」の「通常授業担当設定」タブで優先度や、「講師指導可能科目」タブ・アンケート回答で講師の出勤可否を見直してください。");
                if (result.UnassignedWithNoQualifiedTeacher > 0)
                    reasons.Add($"うち{result.UnassignedWithNoQualifiedTeacher}件は、対応できる講師の候補コマが構造的に見つかりませんでした（講師の資格・出勤可否をご確認ください）。時間をかけても解決しません。");
                var reasonNote = reasons.Count > 0 ? " " + string.Join(" ", reasons) : "";
                RunStatus.Message = baseMessage + reasonNote;
                // ユーザー要望（checkpoint152）。「既に配置済みの授業は動かさない」設定で未配置が残った
                // ときだけ、既存配置を1件動かせば配置できる候補を探す（通常の自動作成では、ソルバーが
                // 既存配置も含めて全体最適化するため、この種の「動かせば置ける」状況はそもそも起きない）。
                if (KeepExistingPlacementsCheckBox.IsChecked == true && App.ProjectService.Current is { } project)
                    _ = LoadSwapSuggestionsAsync(project.Path);
            }
            else
            {
                RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "時間割を作成しました";
                RunStatus.Message = baseMessage;
                ClearSwapSuggestions();
            }
            RunStatus.IsOpen = true;
        }
        else
        {
            RunStatus.Severity = InfoBarSeverity.Error; RunStatus.Title = "時間割を作成できませんでした"; RunStatus.Message = outcome.ErrorMessage ?? ""; RunStatus.IsOpen = true;
        }
    }

    // ユーザー要望（checkpoint152）「入れ替えて配置する／配置せずそのままにする」の実体。
    // FindSwapSuggestionsAsyncは最大1秒程度かかりうるため、RefreshRunUi（同期・高頻度）からは
    // 呼ばずfire-and-forgetする。失敗しても自動作成そのものの結果表示は妨げない。
    private async Task LoadSwapSuggestionsAsync(string path)
    {
        try
        {
            var suggestions = await App.ScheduleEditor.FindSwapSuggestionsAsync(path);
            DispatcherQueue.TryEnqueue(() => RenderSwapSuggestions(path, suggestions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
        }
    }

    private void ClearSwapSuggestions()
    {
        SwapSuggestionsList.Children.Clear();
        SwapSuggestionsPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderSwapSuggestions(string path, IReadOnlyList<SwapSuggestion> suggestions)
    {
        SwapSuggestionsList.Children.Clear();
        if (suggestions.Count == 0)
        {
            SwapSuggestionsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var suggestion in suggestions) SwapSuggestionsList.Children.Add(BuildSwapSuggestionCard(path, suggestion));
        SwapSuggestionsPanel.Visibility = Visibility.Visible;
    }

    private Border BuildSwapSuggestionCard(string path, SwapSuggestion suggestion)
    {
        var applyButton = new Button { Content = "入れ替えて配置する" };
        var dismissButton = new Button { Content = "配置せずそのままにする" };
        var card = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = ResourceBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(255, 235, 235, 235)),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = suggestion.Message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { applyButton, dismissButton } },
                },
            },
        };

        applyButton.Click += async (_, _) =>
        {
            applyButton.IsEnabled = false; dismissButton.IsEnabled = false;
            try
            {
                await App.ScheduleEditor.ApplySwapSuggestionAsync(path, suggestion);
                RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "入れ替えて配置しました"; RunStatus.Message = suggestion.Message; RunStatus.IsOpen = true;
            }
            catch (InvalidOperationException ex)
            {
                RunStatus.Severity = InfoBarSeverity.Error; RunStatus.Title = "入れ替えを適用できませんでした"; RunStatus.Message = ex.Message; RunStatus.IsOpen = true;
            }
            SwapSuggestionsList.Children.Remove(card);
            if (SwapSuggestionsList.Children.Count == 0) SwapSuggestionsPanel.Visibility = Visibility.Collapsed;
        };
        dismissButton.Click += (_, _) =>
        {
            SwapSuggestionsList.Children.Remove(card);
            if (SwapSuggestionsList.Children.Count == 0) SwapSuggestionsPanel.Visibility = Visibility.Collapsed;
        };

        return card;
    }

    private static Brush ResourceBrush(string key, Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(fallback);

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

    private async void KeepExistingPlacementsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            await App.SettingsStore.SaveAsync(settings with { KeepExistingPlacements = KeepExistingPlacementsCheckBox.IsChecked == true });
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
