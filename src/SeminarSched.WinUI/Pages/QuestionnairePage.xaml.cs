using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace SeminarSched_WinUI.Pages;
public sealed partial class QuestionnairePage : WorkflowPageBase
{
    public QuestionnairePage() => InitializeComponent();
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        GenerateButton.IsEnabled = EnsureProject(ProjectRequired);
        if (!GenerateButton.IsEnabled) return;
        var current = App.ProjectService.Current!;
        if (string.IsNullOrEmpty(StudentTitleBox.Text)) StudentTitleBox.Text = $"{current.Title} 個別指導受講申込";
        if (string.IsNullOrEmpty(TeacherTitleBox.Text)) TeacherTitleBox.Text = $"{current.Title} 非常勤勤務アンケート";
        DeadlinePicker.Date ??= new DateTimeOffset(current.StartDate.AddDays(-14).ToDateTime(TimeOnly.MinValue));
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var studentTitle = StudentTitleBox.Text?.Trim() ?? "";
        var teacherTitle = TeacherTitleBox.Text?.Trim() ?? "";
        var contact = ContactBox.Text?.Trim() ?? "";
        if (studentTitle.Length == 0 || teacherTitle.Length == 0 || contact.Length == 0 || DeadlinePicker.Date is null)
        {
            Status.Severity = InfoBarSeverity.Error; Status.Title = "入力を確認してください"; Status.Message = "生徒用・講師用フォーム名、回答締切、問い合わせ先をすべて入力してください。"; Status.IsOpen = true;
            return;
        }
        try
        {
            IsEnabled = false;
            var current = App.ProjectService.Current!;
            var deadline = DeadlinePicker.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = await App.QuestionnaireKit.GenerateAsync(current.Path, WorkspacePaths.Forms, current.Title, studentTitle, teacherTitle, deadline, contact);
            Status.Severity=InfoBarSeverity.Success; Status.Title="作成キットを保存しました"; Status.Message=path; Status.IsOpen=true;
        }
        catch(Exception exception) when(exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { Status.Severity=InfoBarSeverity.Error; Status.Title="作成できませんでした"; Status.Message=exception.Message; Status.IsOpen=true; }
        finally { IsEnabled=true; }
    }
}
