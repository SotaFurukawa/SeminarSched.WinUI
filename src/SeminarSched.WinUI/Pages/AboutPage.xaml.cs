using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application;
using SeminarSched.Optimization.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeminarSched_WinUI.Pages;

public sealed partial class AboutPage : Page
{
    public string VersionText { get; } = ApplicationVersion
        .FromAssembly(typeof(App).Assembly)
        .DisplayVersion;

    public string MinimumSpecText { get; } =
        $"論理プロセッサ数{SystemRequirements.MinimumLogicalProcessors}個以上・" +
        $"CPUベースクロック{SystemRequirements.MinimumBaseClockGhz:F1}GHz以上・" +
        $"メモリ{SystemRequirements.MinimumMemoryGb}GB以上を目安としています" +
        "（およそ10年前の一般的なCPU、ノートPCを含む、を基準にした下限の目安です）。";

    public string DetectedSpecText { get; }

    public AboutPage()
    {
        var check = SystemRequirements.Evaluate();
        var processor = check.ProcessorName ?? "取得できませんでした";
        var memory = check.InstalledMemoryGb is { } gb ? $"約{gb}GB" : "取得できませんでした";
        DetectedSpecText = $"お使いのパソコン: {processor}（論理プロセッサ数 {check.LogicalProcessors}個・メモリ {memory}）";

        InitializeComponent();

        if (!check.MeetsMinimum)
        {
            SystemRequirementsWarning.Message = string.Join(" ", check.Reasons) + " 動作が遅くなる、または一部の機能（特に5 時間割自動作成）で時間がかかる場合があります。";
            SystemRequirementsWarning.IsOpen = true;
        }
    }

    // ユーザー要望（checkpoint108）「品質プロファイルのリバランス」への対応。⑤時間割自動作成の初回
    // 実行時に一度だけ計測される性能tier（HardwareTierTextBlock）を、計測済みならここで表示する
    // （AppSettingsの読み込みが非同期のため、コンストラクタではなくLoadedで行う）。
    private static readonly IReadOnlyDictionary<HardwareTier, string> TierLabels = new Dictionary<HardwareTier, string>
    {
        [HardwareTier.VeryLow] = "非常に控えめ",
        [HardwareTier.Low] = "控えめ",
        [HardwareTier.Standard] = "標準",
        [HardwareTier.High] = "高め",
        [HardwareTier.VeryHigh] = "非常に高め",
    };

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshHardwareTierTextAsync();
        await RefreshAppNameTextAsync();
    }

    private async Task RefreshAppNameTextAsync()
    {
        var settings = await App.SettingsStore.LoadAsync();
        AppNameTextBlock.Text = settings.ProductKeyLicenseLabel is { } suffix ? $"ShikiWari {suffix}" : "ShikiWari";
    }

    private async Task RefreshHardwareTierTextAsync()
    {
        var settings = await App.SettingsStore.LoadAsync();
        HardwareTierTextBlock.Text = settings.HardwareTier is { } tier && Enum.IsDefined(tier)
            ? $"このパソコンで一度だけ測定した結果、探索の並列度は「{TierLabels[tier]}」設定で実行されます" +
              (settings.HardwareBenchmarkElapsedSeconds is { } seconds ? $"（測定値: {seconds:F1}秒）。" : "。") +
              "パソコンを買い替えた場合など、下のボタンから再測定できます。"
            : "まだ測定していません。5 時間割自動作成を初めて実行するときに、一度だけ自動で測定されます。";
    }

    private async void Remeasure_Click(object sender, RoutedEventArgs e)
    {
        RemeasureButton.IsEnabled = false;
        HardwareTierTextBlock.Text = "測定しています…（最大20秒程度）";
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            var result = await HardwareBenchmark.RunAsync();
            SeminarSched.Optimization.Core.CpSatScheduleSolver.HardwareTier = result.Tier;
            await App.SettingsStore.SaveAsync(settings with
            {
                HardwareTier = result.Tier,
                HardwareBenchmarkElapsedSeconds = result.Elapsed.TotalSeconds,
            });
            await RefreshHardwareTierTextAsync();
        }
        finally
        {
            RemeasureButton.IsEnabled = true;
        }
    }
}
