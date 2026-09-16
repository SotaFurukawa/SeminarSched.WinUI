using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class QuestionnairePage : WorkflowPageBase
{
    public QuestionnairePage() => InitializeComponent();
    private void Page_Loaded(object sender, RoutedEventArgs e) => GenerateButton.IsEnabled = EnsureProject(ProjectRequired);
    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
            IsEnabled = false;
            var path = await App.QuestionnaireKit.GenerateAsync(App.ProjectService.Current!.Path, folder.Path);
            Status.Severity=InfoBarSeverity.Success; Status.Title="作成キットを保存しました"; Status.Message=path; Status.IsOpen=true;
        }
        catch(Exception exception) when(exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { Status.Severity=InfoBarSeverity.Error; Status.Title="作成できませんでした"; Status.Message=exception.Message; Status.IsOpen=true; }
        finally { IsEnabled=true; }
    }
}
