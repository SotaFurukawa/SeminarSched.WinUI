using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SeminarSched_WinUI.Pages;

public abstract class WorkflowPageBase : Page
{
    protected bool EnsureProject(InfoBar infoBar)
    {
        var available = App.ProjectService.Current is not null;
        infoBar.IsOpen = !available;
        infoBar.Severity = InfoBarSeverity.Warning;
        infoBar.Title = "プロジェクトを開いてください";
        infoBar.Message = "ホームでプロジェクトを作成または開いてから、この段階を操作してください。";
        return available;
    }

    protected static Visibility When(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
