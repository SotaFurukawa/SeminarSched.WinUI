using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class OutputPage : WorkflowPageBase
{
    public OutputPage()=>InitializeComponent();
    private void Page_Loaded(object sender,RoutedEventArgs e)=>GenerateButton.IsEnabled=EnsureProject(ProjectRequired);
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        try{var picker=new FolderPicker{SuggestedStartLocation=PickerLocationId.DocumentsLibrary};picker.FileTypeFilter.Add("*");InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(App.MainWindow!));var folder=await picker.PickSingleFolderAsync();if(folder is null)return;IsEnabled=false;Progress.IsActive=true;var result=await App.OutputPackage.GenerateAsync(App.ProjectService.Current!.Path,folder.Path);Status.Severity=InfoBarSeverity.Success;Status.Title="出力しました";Status.Message=$"{result.DirectoryPath}（授業{result.AssignmentCount}件・未配置{result.UnassignedCount}件）";Status.IsOpen=true;App.Logger.Info($"Output generated: assignments={result.AssignmentCount} unassigned={result.UnassignedCount}");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){Status.Severity=InfoBarSeverity.Error;Status.Title="出力できませんでした";Status.Message=ex.Message;Status.IsOpen=true;App.Logger.Error("Output generation failed",ex);}finally{Progress.IsActive=false;IsEnabled=true;}
    }
}
