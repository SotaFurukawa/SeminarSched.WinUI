using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SeminarSched.Application.Settings;
using SeminarSched_WinUI.ViewModels;
using SeminarSched.Application.Scheduling;

namespace SeminarSched_WinUI.Pages;

public sealed partial class OptimizationPage : Page
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
            RunButton.IsEnabled = App.ProjectService.Current is not null;
            if (RunButton.IsEnabled) await ReloadEditorAsync();
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
        try
        {
            RunButton.IsEnabled=false;RunProgress.IsActive=true;RunStatus.IsOpen=false;
            var profile=SeminarSched.Optimization.Profiles.OptimizationProfileCatalog.Get(ViewModel.Level);
            var result=await App.ScheduleRun.RunAsync(App.ProjectService.Current!.Path,profile.MaximumDuration);
            RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title="時間割を作成しました";RunStatus.Message=$"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒";RunStatus.IsOpen=true;
            await ReloadEditorAsync();
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を作成できませんでした";RunStatus.Message=ex.Message;RunStatus.IsOpen=true;}
        finally{RunProgress.IsActive=false;RunButton.IsEnabled=App.ProjectService.Current is not null;}
    }

    private async Task ReloadEditorAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        ManualRequest.ItemsSource=await App.FixedLessons.GetRequestsAsync(path);ManualTeacher.ItemsSource=await App.FixedLessons.GetTeachersAsync(path);ManualSlot.ItemsSource=await App.FixedLessons.GetSlotsAsync(path);Assignments.ItemsSource=await App.ScheduleEditor.GetAssignmentsAsync(path);
    }

    private async void AddManual_Click(object sender,RoutedEventArgs e)
    {
        if(ManualRequest.SelectedItem is not LessonRequestOption request||ManualTeacher.SelectedItem is not TeacherOption teacher||ManualSlot.SelectedItem is not ScheduleSlotOption slot){ShowEditorError("受講希望・講師・日時を選択してください。");return;}
        await ExecuteEditorAsync(async()=>await App.ScheduleEditor.AddManualAsync(App.ProjectService.Current!.Path,request.Id,teacher.Id,slot.OpenDateId,slot.TimeSlotId,ManualLocked.IsChecked==true),"手動配置を追加しました");
    }
    private async void RemoveManual_Click(object sender,RoutedEventArgs e)
    {
        if(Assignments.SelectedItem is not ScheduleAssignmentItem assignment||!assignment.IsManual){ShowEditorError("削除する手動配置を選択してください。自動配置はリセットを使用します。");return;}await ExecuteEditorAsync(async()=>await App.ScheduleEditor.RemoveManualAsync(App.ProjectService.Current!.Path,assignment.Id),"手動配置を削除しました");
    }
    private async void ToggleLock_Click(object sender,RoutedEventArgs e)
    {
        if(Assignments.SelectedItem is not ScheduleAssignmentItem assignment){ShowEditorError("配置を選択してください。");return;}await ExecuteEditorAsync(async()=>await App.ScheduleEditor.SetLockedAsync(App.ProjectService.Current!.Path,assignment.Id,!assignment.IsLocked),assignment.IsLocked?"ロックを解除しました":"ロックしました");
    }
    private async void ResetAutomatic_Click(object sender,RoutedEventArgs e)=>await ExecuteEditorAsync(async()=>await App.ScheduleEditor.ResetAutomaticAsync(App.ProjectService.Current!.Path),"自動配置をリセットしました");
    private async Task ExecuteEditorAsync(Func<Task> action,string success)
    {
        try{IsEnabled=false;await action();await ReloadEditorAsync();RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title=success;RunStatus.Message="";RunStatus.IsOpen=true;}catch(Exception exception)when(exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){ShowEditorError(exception.Message);}finally{IsEnabled=true;}
    }
    private void ShowEditorError(string message){RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を編集できませんでした";RunStatus.Message=message;RunStatus.IsOpen=true;}

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
