using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SeminarSched.Application.Settings;
using SeminarSched_WinUI.ViewModels;

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
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を作成できませんでした";RunStatus.Message=ex.Message;RunStatus.IsOpen=true;}
        finally{RunProgress.IsActive=false;RunButton.IsEnabled=App.ProjectService.Current is not null;}
    }

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
