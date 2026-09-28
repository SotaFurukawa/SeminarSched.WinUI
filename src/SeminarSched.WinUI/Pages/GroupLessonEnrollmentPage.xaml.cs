using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.GroupLessons;
using SeminarSched.Domain.GroupLessons;

namespace SeminarSched_WinUI.Pages;

public sealed partial class GroupLessonEnrollmentPage : WorkflowPageBase
{
    private long? _selectedClassId;
    private IReadOnlyList<GroupLessonEnrollmentCandidate> _candidates = [];

    public GroupLessonEnrollmentPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var ready = EnsureGroupLessonsEnabled();
        ContentPanel.IsEnabled = ready;
        if (ready) await ReloadClassesAsync();
    }

    private bool EnsureGroupLessonsEnabled()
    {
        var ready = EnsureProject(ProjectRequired);
        if (!ready) return false;
        if (App.ProjectService.Current?.ConsiderGroupLessons != true)
        {
            ProjectRequired.IsOpen = true;
            ProjectRequired.Severity = InfoBarSeverity.Warning;
            ProjectRequired.Title = "集団授業機能が無効です";
            ProjectRequired.Message = "このプロジェクトは作成時に「集団授業の日程を考慮する」がオフになっています。集団授業を扱うには、この設定を有効にした新しいプロジェクトを作成してください。";
            return false;
        }
        return true;
    }

    private sealed record GroupClassRow(GroupLessonClass Value, string Display) { public override string ToString() => Display; }

    private async Task ReloadClassesAsync()
    {
        var path = App.ProjectService.Current?.Path; if (path is null) return;
        var previousClassId = (EnrollmentClass.SelectedItem as GroupClassRow)?.Value.Id;
        var classes = await App.GroupLessons.GetClassesAsync(path);
        var rows = classes.Select(c => new GroupClassRow(c, $"{c.Name}　{c.Subject}　（{c.Grade}）{(c.AllowOtherGrades ? "　他学年可" : "")}{(c.Active ? "" : "　[停止]")}")).ToArray();
        EnrollmentClass.ItemsSource = rows;
        EnrollmentClass.SelectedItem = rows.FirstOrDefault(r => r.Value.Id == previousClassId) ?? rows.FirstOrDefault();
    }

    private async void EnrollmentClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnrollmentClass.SelectedItem is not GroupClassRow row)
        {
            _selectedClassId = null; EnrollmentStudents.ItemsSource = null; EnrollmentClassInfo.Text = "";
            return;
        }
        _selectedClassId = row.Value.Id;
        EnrollmentClassInfo.Text = $"科目: {row.Value.Subject}　対象学年: {row.Value.Grade}{(row.Value.AllowOtherGrades ? "（他学年の受講も許可）" : "")}";
        await ReloadCandidatesAsync();
    }

    private async Task ReloadCandidatesAsync()
    {
        if (_selectedClassId is not { } classId) { EnrollmentStudents.ItemsSource = null; return; }
        var path = App.ProjectService.Current?.Path; if (path is null) return;
        _candidates = await App.GroupLessons.GetEnrollmentCandidatesAsync(path, classId);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = EnrollmentSearch.Text?.Trim() ?? "";
        EnrollmentStudents.ItemsSource = (search.Length == 0 ? _candidates : _candidates.Where(c => c.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }

    private void EnrollmentSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void EnrollmentCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: GroupLessonEnrollmentCandidate candidate } checkBox || _selectedClassId is not { } classId) return;
        try
        {
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.GroupLessons.SetEnrollmentAsync(path, classId, candidate.StudentId, checkBox.IsChecked == true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Status.Severity = InfoBarSeverity.Error; Status.Title = "更新できませんでした"; Status.Message = exception.Message; Status.IsOpen = true;
        }
    }
}
