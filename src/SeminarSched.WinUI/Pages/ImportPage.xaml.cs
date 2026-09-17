using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.Importing;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class ImportPage : WorkflowPageBase
{
    private ResponseImportPreview? _preview;
    public ImportPage() => InitializeComponent();
    private void Page_Loaded(object sender, RoutedEventArgs e) => SelectButton.IsEnabled=EnsureProject(ProjectRequired);
    private async void Select_Click(object sender,RoutedEventArgs e)
    {
        var student=await PickResponseAsync();if(student is null)return;var teacher=await PickResponseAsync();if(teacher is null)return;
        try
        {
            IsEnabled=false;
            _preview=await App.ResponseImport.PreviewAsync(App.ProjectService.Current!.Path,student,teacher);
            Issues.ItemsSource=_preview.Issues.Select(x=>$"{(x.IsError?"[エラー]":"[警告]")} 行{x.Row} [{x.Field}] {x.Message}").ToArray();
            ApplyButton.IsEnabled=_preview.CanApply;
            Status.Severity=_preview.CanApply?InfoBarSeverity.Success:InfoBarSeverity.Error;
            Status.Title=_preview.CanApply?$"検証成功（生徒{_preview.StudentRowCount}行・講師{_preview.TeacherRowCount}行）":"検証エラーがあります";
            Status.IsOpen=true;
            RenderDiff(_preview.Diff);
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException){ShowError(ex.Message);}finally{IsEnabled=true;}
    }

    private void RenderDiff(SeminarSched.Application.Importing.ResponseImportDiff diff)
    {
        DiffSummary.Visibility=Visibility.Visible;
        DiffSummary.Text=$"生徒: 追加{diff.StudentAdded}件・変更{diff.StudentChanged}件・変更なし{diff.StudentUnchanged}件 / 講師: 追加{diff.TeacherAdded}件・変更{diff.TeacherChanged}件・変更なし{diff.TeacherUnchanged}件";
        RemovalPanel.Visibility=diff.HasRemovalCandidates?Visibility.Visible:Visibility.Collapsed;
        RemoveUnlisted.IsChecked=false;
        RemovalList.ItemsSource=diff.StudentRemovalCandidates.Select(x=>$"生徒 {x}").Concat(diff.TeacherRemovalCandidates.Select(x=>$"講師 {x}")).ToArray();
    }

    private async void Apply_Click(object sender,RoutedEventArgs e){if(_preview is null)return;try{IsEnabled=false;await App.ResponseImport.ApplyAsync(App.ProjectService.Current!.Path,_preview,RemoveUnlisted.IsChecked==true);Status.Severity=InfoBarSeverity.Success;Status.Title="回答を反映しました";Status.IsOpen=true;ApplyButton.IsEnabled=false;}catch(Exception ex)when(ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(ex.Message);}finally{IsEnabled=true;}}
    private async Task<string?> PickResponseAsync(){var p=new FileOpenPicker{SuggestedStartLocation=PickerLocationId.DocumentsLibrary};p.FileTypeFilter.Add(".csv");p.FileTypeFilter.Add(".xlsx");InitializeWithWindow.Initialize(p,WindowNative.GetWindowHandle(App.MainWindow!));return (await p.PickSingleFileAsync())?.Path;}
    private void ShowError(string message){Status.Severity=InfoBarSeverity.Error;Status.Title="処理できませんでした";Status.Message=message;Status.IsOpen=true;}
}
