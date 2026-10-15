using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Domain.Projects;
using Windows.Storage;
using Windows.System;
namespace SeminarSched_WinUI.Pages;
public sealed partial class QuestionnairePage : WorkflowPageBase
{
    private string? _lastOutputDirectory;
    public QuestionnairePage() => InitializeComponent();
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        GenerateButton.IsEnabled = EnsureProject(ProjectRequired);
        if (!GenerateButton.IsEnabled) return;
        var current = App.ProjectService.Current!;
        if (string.IsNullOrEmpty(StudentTitleBox.Text)) StudentTitleBox.Text = $"{current.Title} 個別指導受講申し込み";
        if (string.IsNullOrEmpty(TeacherTitleBox.Text)) TeacherTitleBox.Text = $"{current.Title} 非常勤勤務アンケート";
        var defaultDeadline = new DateTimeOffset(current.StartDate.AddDays(-14).ToDateTime(TimeOnly.MinValue));
        StudentDeadlinePicker.Date ??= defaultDeadline;
        TeacherDeadlinePicker.Date ??= defaultDeadline;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var studentTitle = StudentTitleBox.Text?.Trim() ?? "";
        var teacherTitle = TeacherTitleBox.Text?.Trim() ?? "";
        var contact = ContactBox.Text?.Trim() ?? "";
        if (studentTitle.Length == 0 || teacherTitle.Length == 0 || contact.Length == 0 || StudentDeadlinePicker.Date is null || TeacherDeadlinePicker.Date is null)
        {
            Status.Severity = InfoBarSeverity.Error; Status.Title = "入力を確認してください"; Status.Message = "生徒用・講師用フォーム名、それぞれの回答締切、問い合わせ先をすべて入力してください。"; Status.IsOpen = true;
            return;
        }
        try
        {
            IsEnabled = false;
            var current = App.ProjectService.Current!;
            var studentDeadline = StudentDeadlinePicker.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var teacherDeadline = TeacherDeadlinePicker.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var askEntranceExamQuestion = AskEntranceExamQuestionCheckBox.IsChecked ?? false;
            // ユーザー要望「最後の質問は冬期講習なら冬期などになるように」への対応。Otherは
            // CourseProjectDefinition.CustomSeasonNameが正本だがProjectSummaryには含まれないため、
            // Titleの「{年度}年度」接頭辞を取り除いた残りをその代わりとして使う。
            var seasonLabel = current.Season == CourseSeason.Other
                ? current.Title.Replace($"{current.AcademicYear}年度", "", StringComparison.Ordinal)
                : current.Season.ToShortJapaneseName();
            var path = await App.QuestionnaireKit.GenerateAsync(current.Path, WorkspacePaths.Forms, current.Title, studentTitle, teacherTitle, studentDeadline, teacherDeadline, contact, askEntranceExamQuestion, seasonLabel);
            _lastOutputDirectory = path;
            ResultPanel.Visibility = Visibility.Visible;
            ToastNotificationState.ShowSuccess("作成キットを保存しました");
        }
        catch(Exception exception) when(exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { ResultPanel.Visibility = Visibility.Collapsed; Status.Severity=InfoBarSeverity.Error; Status.Title="作成できませんでした"; Status.Message=exception.Message; Status.IsOpen=true; }
        finally { IsEnabled=true; }
    }

    private async void Guide_Click(object sender, RoutedEventArgs e) => await GoogleFormsGuide.ShowAsync(XamlRoot);

    // ユーザー報告「2 アンケート作成で保存先を開こうとしてもエクスプローラーが起動しない
    // （クライアント環境、Windows 11）」への対応。Process.Start(UseShellExecute: true)は
    // Win32のShellExecuteEx経由で、MSIXパッケージ化されたアプリのAppContainerから呼び出した
    // ときの挙動が実行環境（Windowsのバージョン・ビルド、ポリシー設定）によって不安定になりうる。
    // OutputPage.OpenOutputFolder_Clickが既に使っているWindows.System.Launcher（WinRTの
    // ランチャーAPI。パッケージ化アプリ向けに設計されており、こちらの方が確実に動く）へ統一した。
    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutputDirectory is null) return;
        try
        {
            if (!Directory.Exists(_lastOutputDirectory))
            {
                Status.Severity = InfoBarSeverity.Error; Status.Title = "保存先を開けませんでした"; Status.Message = $"フォルダーが見つかりません: {_lastOutputDirectory}"; Status.IsOpen = true;
                return;
            }
            var folder = await StorageFolder.GetFolderFromPathAsync(_lastOutputDirectory);
            var opened = await Launcher.LaunchFolderAsync(folder);
            if (!opened)
            { Status.Severity = InfoBarSeverity.Error; Status.Title = "保存先を開けませんでした"; Status.Message = $"エクスプローラーを起動できませんでした。手動で次のフォルダーを開いてください: {_lastOutputDirectory}"; Status.IsOpen = true; }
        }
        catch (Exception exception) when (exception is FileNotFoundException or UnauthorizedAccessException)
        { Status.Severity = InfoBarSeverity.Error; Status.Title = "保存先を開けませんでした"; Status.Message = exception.Message; Status.IsOpen = true; }
    }
}
