using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SeminarSched_WinUI.ViewModels;

namespace SeminarSched_WinUI.Pages;

public sealed partial class OptimizationPage : WorkflowPageBase
{
    private CancellationTokenSource? _saveDebounce;
    private bool _isLoaded;

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
        try
        {
            RunButton.IsEnabled=false;RunProgress.IsActive=true;RunStatus.IsOpen=false;
            var beforeRun=await App.ScheduleEditor.CaptureSnapshotAsync(path);
            ScheduleUndoState.Push(beforeRun);
            ScheduleUndoState.ReoptimizationBaseline=beforeRun;
            var profile=SeminarSched.Optimization.Profiles.OptimizationProfileCatalog.Get(ViewModel.Level);
            var result=await App.ScheduleRun.RunAsync(path,profile.MaximumDuration);
            RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title="時間割を作成しました";RunStatus.Message=$"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒。④時間割編集で変更内容を確認できます。";RunStatus.IsOpen=true;
            App.Logger.Info($"Schedule run completed: placed={result.PlacedLessons} unassigned={result.UnassignedLessons} elapsedSec={result.Elapsed.TotalSeconds:F1} level={ViewModel.Level}");
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if(ScheduleUndoState.UndoStack.Count>0)ScheduleUndoState.UndoStack.Pop();
            ScheduleUndoState.ReoptimizationBaseline=null;
            RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を作成できませんでした";RunStatus.Message=ex.Message;RunStatus.IsOpen=true;
            App.Logger.Error("Schedule run failed",ex);
        }
        finally{RunProgress.IsActive=false;RunButton.IsEnabled=App.ProjectService.Current is not null;}
    }

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
