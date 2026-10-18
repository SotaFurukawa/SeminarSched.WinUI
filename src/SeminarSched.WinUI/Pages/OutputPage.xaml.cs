using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;
namespace SeminarSched_WinUI.Pages;
public sealed partial class OutputPage : WorkflowPageBase
{
    private string? _outputDirectory;
    public OutputPage()=>InitializeComponent();
    private async void Page_Loaded(object sender,RoutedEventArgs e)
    {
        GenerateButton.IsEnabled=EnsureProject(ProjectRequired);
        if(GenerateButton.IsEnabled) await LoadLatestScoresAsync();
    }

    // ユーザー要望「様々なスコアもあるはずなので、それを表示できるようにしてみたい」への対応。
    // 直近に完了した自動作成の評価スコア（ScheduleEvaluationCalculatorが既に戦略同士の比較に
    // 使っている内訳）を読み出して表示する。自動作成を一度も実行していないプロジェクトでは
    // nullが返るため、パネルごと非表示のままにする。
    private async Task LoadLatestScoresAsync()
    {
        var evaluation = await App.ScheduleRun.GetLatestRunEvaluationAsync(App.ProjectService.Current!.Path);
        if(evaluation is null){ScorePanel.Visibility=Visibility.Collapsed;return;}
        ScoreUnassigned.Text=evaluation.UnassignedLessons.ToString();
        ScoreImportantPreferenceViolations.Text=evaluation.ImportantPreferenceViolations.ToString();
        ScoreMajorPenalty.Text=evaluation.MajorPenalty.ToString();
        ScoreDistributionPenalty.Text=evaluation.DistributionPenalty.ToString();
        ScoreSpacingPenalty.Text=evaluation.SpacingPenalty.ToString();
        ScoreOtherSoftPenalty.Text=evaluation.OtherSoftPenalty.ToString();
        ScoreObjectiveValue.Text=evaluation.ObjectiveValue.ToString();
        ScorePanel.Visibility=Visibility.Visible;
    }
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        try{IsEnabled=false;Progress.IsActive=true;ProgressText.Visibility=Visibility.Visible;var result=await App.OutputPackage.GenerateAsync(App.ProjectService.Current!.Path,WorkspacePaths.Output);ToastNotificationState.ShowSuccess($"出力しました（授業{result.AssignmentCount}件・未配置{result.UnassignedCount}件）");App.Logger.Info($"Output generated: assignments={result.AssignmentCount} unassigned={result.UnassignedCount}");
            _outputDirectory=result.DirectoryPath;
            var files=new List<OutputFileItem>{
                new(result.OverallExcelPath,Path.GetFileName(result.OverallExcelPath)),new(result.OverallPdfPath,Path.GetFileName(result.OverallPdfPath)),
                new(result.StudentHandoutsExcelPath,Path.GetFileName(result.StudentHandoutsExcelPath)),new(result.StudentHandoutsPdfPath,Path.GetFileName(result.StudentHandoutsPdfPath)),
                new(result.TeacherHandoutsExcelPath,Path.GetFileName(result.TeacherHandoutsExcelPath)),new(result.TeacherHandoutsPdfPath,Path.GetFileName(result.TeacherHandoutsPdfPath)),
                new(result.IssuesExcelPath,Path.GetFileName(result.IssuesExcelPath)),new(result.IssuesPdfPath,Path.GetFileName(result.IssuesPdfPath)),
                new(result.CombinedTeacherPacketExcelPath,Path.GetFileName(result.CombinedTeacherPacketExcelPath)),new(result.CombinedTeacherPacketPdfPath,Path.GetFileName(result.CombinedTeacherPacketPdfPath)),
            };
            if(Directory.Exists(result.TeacherPacketDirectory))
                files.AddRange(Directory.GetFiles(result.TeacherPacketDirectory).OrderBy(x=>x,StringComparer.Ordinal).Select(f=>new OutputFileItem(f,$"講師配布用講師別時間割/{Path.GetFileName(f)}")));
            GeneratedFiles.ItemsSource=files;OutputFilesPanel.Visibility=Visibility.Visible;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException){Status.Severity=InfoBarSeverity.Error;Status.Title="出力できませんでした";Status.Message=ex.Message;Status.IsOpen=true;App.Logger.Error("Output generation failed",ex);}finally{Progress.IsActive=false;ProgressText.Visibility=Visibility.Collapsed;IsEnabled=true;}
    }

    private async void OpenSelectedFile_Click(object sender,RoutedEventArgs e)
    {
        if(GeneratedFiles.SelectedItem is not OutputFileItem item){Status.Severity=InfoBarSeverity.Warning;Status.Title="ファイルを選択してください";Status.Message="";Status.IsOpen=true;return;}
        try{var file=await StorageFile.GetFileFromPathAsync(item.Path);await Launcher.LaunchFileAsync(file);}
        catch(Exception ex)when(ex is FileNotFoundException or UnauthorizedAccessException){Status.Severity=InfoBarSeverity.Error;Status.Title="ファイルを開けませんでした";Status.Message=ex.Message;Status.IsOpen=true;}
    }

    private async void OpenOutputFolder_Click(object sender,RoutedEventArgs e)
    {
        if(_outputDirectory is null)return;
        try{var folder=await StorageFolder.GetFolderFromPathAsync(_outputDirectory);await Launcher.LaunchFolderAsync(folder);}
        catch(Exception ex)when(ex is FileNotFoundException or UnauthorizedAccessException){Status.Severity=InfoBarSeverity.Error;Status.Title="フォルダーを開けませんでした";Status.Message=ex.Message;Status.IsOpen=true;}
    }

    private sealed record OutputFileItem(string Path,string Display){public override string ToString()=>Display;}
}
