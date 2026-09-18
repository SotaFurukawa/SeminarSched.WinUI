using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace SeminarSched_WinUI.Pages;
public sealed partial class QuestionnairePage : WorkflowPageBase
{
    public QuestionnairePage() => InitializeComponent();
    private void Page_Loaded(object sender, RoutedEventArgs e) => GenerateButton.IsEnabled = EnsureProject(ProjectRequired);
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IsEnabled = false;
            var path = await App.QuestionnaireKit.GenerateAsync(App.ProjectService.Current!.Path, WorkspacePaths.Forms);
            Status.Severity=InfoBarSeverity.Success; Status.Title="作成キットを保存しました"; Status.Message=path; Status.IsOpen=true;
        }
        catch(Exception exception) when(exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { Status.Severity=InfoBarSeverity.Error; Status.Title="作成できませんでした"; Status.Message=exception.Message; Status.IsOpen=true; }
        finally { IsEnabled=true; }
    }
}
