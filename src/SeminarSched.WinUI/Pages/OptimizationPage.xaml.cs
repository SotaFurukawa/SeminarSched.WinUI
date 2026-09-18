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
    private OptimizationRunControl? _runControl;

    private static readonly Dictionary<OptimizationStageKind, string> StageLabels = new()
    {
        [OptimizationStageKind.InitialExploration] = "初期探索",
        [OptimizationStageKind.CandidateAdvancement] = "候補改善",
        [OptimizationStageKind.NeighborhoodRepair] = "近傍再探索",
        [OptimizationStageKind.FinalPolishing] = "仕上げ探索",
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
        [OptimizationStrategyKind.FinalPolishing] = "最終仕上げ探索",
    };

    public OptimizationPage()
    {
        InitializeComponent();
    }

    public OptimizationQualityViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            ViewModel.Select((int)settings.OptimizationQualityLevel);
            QualitySlider.Value = ViewModel.SliderValue;
            _isLoaded = true;
            var ready = EnsureProject(ProjectRequired);
            ContentPanel.IsEnabled = ready;
            RunButton.IsEnabled = ready;
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

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var path=App.ProjectService.Current!.Path;
        using var control=new OptimizationRunControl();
        _runControl=control;
        try
        {
            RunButton.IsEnabled=false;AcceptBestButton.IsEnabled=true;RunProgress.IsActive=true;RunStatus.IsOpen=false;
            ProgressPanel.Visibility=Visibility.Visible;RunProgressBar.Value=0;RunPercentText.Text="0%";RunEtaText.Text="";RunStageText.Text="準備中…";
            var beforeRun=await App.ScheduleEditor.CaptureSnapshotAsync(path);
            ScheduleUndoState.Push(beforeRun);
            ScheduleUndoState.ReoptimizationBaseline=beforeRun;
            var profile=OptimizationProfileCatalog.Get(ViewModel.Level);
            var progress=new Progress<OptimizationProgress>(UpdateRunProgress);
            var result=await App.ScheduleRun.RunAsync(path,profile,control,progress);
            RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title="時間割を作成しました";RunStatus.Message=$"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒（採用戦略: {StrategyDisplayName(result.StrategyLabel)}）。④時間割編集で変更内容を確認できます。";RunStatus.IsOpen=true;
            App.Logger.Info($"Schedule run completed: placed={result.PlacedLessons} unassigned={result.UnassignedLessons} elapsedSec={result.Elapsed.TotalSeconds:F1} level={ViewModel.Level} strategy={result.StrategyLabel}");
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if(ScheduleUndoState.UndoStack.Count>0)ScheduleUndoState.UndoStack.Pop();
            ScheduleUndoState.ReoptimizationBaseline=null;
            RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を作成できませんでした";RunStatus.Message=ex.Message;RunStatus.IsOpen=true;
            App.Logger.Error("Schedule run failed",ex);
        }
        finally{RunProgress.IsActive=false;RunButton.IsEnabled=App.ProjectService.Current is not null;AcceptBestButton.IsEnabled=false;_runControl=null;}
    }

    private void AcceptBest_Click(object sender, RoutedEventArgs e)
    {
        _runControl?.AcceptCurrentBest();
        AcceptBestButton.IsEnabled=false;
        RunStageText.Text="現在の結果を採用しています…";
    }

    private void UpdateRunProgress(OptimizationProgress progress)
    {
        var percent = progress.MaximumTime.TotalSeconds<=0 ? 0 : Math.Clamp(progress.Elapsed.TotalSeconds/progress.MaximumTime.TotalSeconds*100.0,0,100);
        RunProgressBar.Value=percent;
        RunPercentText.Text=$"{percent:F0}%";
        var remaining=progress.MaximumTime-progress.Elapsed;
        if(remaining<TimeSpan.Zero)remaining=TimeSpan.Zero;
        RunEtaText.Text=$"経過 {FormatDuration(progress.Elapsed)} / 残り目安 {FormatDuration(remaining)}";
        var stageLabel=StageLabels.GetValueOrDefault(progress.Stage,progress.Stage.ToString());
        var strategyLabel=StrategyLabels.GetValueOrDefault(progress.Strategy,progress.Strategy.ToString());
        RunStageText.Text=$"{stageLabel}：{strategyLabel}（{progress.StrategiesCompleted}/{progress.StrategiesTotal}戦略）";
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
}
