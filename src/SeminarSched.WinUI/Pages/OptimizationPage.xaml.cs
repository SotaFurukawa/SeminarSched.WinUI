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
            await App.SettingsStore.SaveAsync(new AppSettings(ViewModel.Level), cancellationToken);
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
