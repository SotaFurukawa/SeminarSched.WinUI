using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application;

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
            SystemRequirementsWarning.Message = string.Join(" ", check.Reasons) + " 動作が遅くなる、または一部の機能（特に⑤時間割自動作成）で時間がかかる場合があります。";
            SystemRequirementsWarning.IsOpen = true;
        }
    }
}
