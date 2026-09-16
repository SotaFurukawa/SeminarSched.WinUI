using System.ComponentModel;
using System.Runtime.CompilerServices;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched_WinUI.ViewModels;

public sealed class OptimizationQualityViewModel : INotifyPropertyChanged
{
    private OptimizationProfile _profile = OptimizationProfileCatalog.Get(OptimizationProfileCatalog.DefaultLevel);

    public event PropertyChangedEventHandler? PropertyChanged;

    public OptimizationQualityLevel Level => _profile.Level;

    public double SliderValue => (int)_profile.Level;

    public string LevelText => $"品質レベル {(int)_profile.Level} / 5";

    public string DisplayName => _profile.DisplayName;

    public string EstimatedDuration => $"推定所要時間：{_profile.EstimatedDuration}";

    public string StrategySummary => $"探索戦略：{_profile.StrategyCount}種類程度";

    public string Description => _profile.UserDescription;

    public void Select(double sliderValue)
    {
        var snapped = Math.Clamp((int)Math.Round(sliderValue, MidpointRounding.AwayFromZero), 1, 5);
        var selected = OptimizationProfileCatalog.Get((OptimizationQualityLevel)snapped);
        if (selected.Level == _profile.Level)
        {
            return;
        }

        _profile = selected;
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
