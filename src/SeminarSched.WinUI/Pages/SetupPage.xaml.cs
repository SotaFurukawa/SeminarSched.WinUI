using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Domain.MasterData;

namespace SeminarSched_WinUI.Pages;

public sealed partial class SetupPage : WorkflowPageBase
{
    public SetupPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var current = App.ProjectService.Current;
        if (!EnsureProject(ProjectRequired) || current is null) { Tabs.IsEnabled = false; return; }
        ProjectTitle.Text = current.Title;
        ProjectPeriod.Text = $"{current.StartDate:yyyy年M月d日} ～ {current.EndDate:yyyy年M月d日}";
        await ReloadAsync();
    }

    private async void AddStudent_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveStudentAsync(path, new Student(0, StudentId.Text, StudentName.Text, StudentGrade.Text));
        StudentId.Text = StudentName.Text = StudentGrade.Text = "";
    }, "生徒を追加しました");

    private async void AddTeacher_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveTeacherAsync(path, new Teacher(0, TeacherId.Text, TeacherName.Text));
        TeacherId.Text = TeacherName.Text = "";
    }, "講師を追加しました");

    private async void AddSubject_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveSubjectAsync(path, new Subject(0, SubjectCode.Text, SubjectName.Text, SubjectShort.Text, SubjectLevel.Text, checked((int)SubjectOrder.Value)));
        SubjectCode.Text = SubjectName.Text = SubjectShort.Text = SubjectLevel.Text = "";
        SubjectOrder.Value++;
    }, "科目を追加しました");

    private async Task ExecuteAsync(Func<string, Task> action, string success)
    {
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await action(path); await ReloadAsync();
            Show(InfoBarSeverity.Success, success, "");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or SqliteException)
        {
            Show(InfoBarSeverity.Error, "保存できませんでした", exception is SqliteException { SqliteErrorCode: 19 } ? "IDまたはコードが重複しています。" : exception.Message);
        }
        finally { IsEnabled = true; }
    }

    private async Task ReloadAsync()
    {
        var path = App.ProjectService.Current!.Path;
        Students.ItemsSource = (await App.MasterData.GetStudentsAsync(path)).Select(x => $"{x.ExternalId}　{x.Name}　{x.Grade}").ToArray();
        Teachers.ItemsSource = (await App.MasterData.GetTeachersAsync(path)).Select(x => $"{x.ExternalId}　{x.Name}").ToArray();
        Subjects.ItemsSource = (await App.MasterData.GetSubjectsAsync(path)).Select(x => $"{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}").ToArray();
    }

    private void Show(InfoBarSeverity severity, string title, string message) { Status.Severity = severity; Status.Title = title; Status.Message = message; Status.IsOpen = true; }
}
