using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.CourseSettings;

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

    private async void AddSlot_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(0, SlotCode.Text, SlotName.Text,
            TimeOnly.FromTimeSpan(SlotStart.Time), TimeOnly.FromTimeSpan(SlotEnd.Time), checked((int)SlotOrder.Value)));
        SlotCode.Text = SlotName.Text = ""; SlotOrder.Value++;
    }, "コマを追加しました");

    private async void SetOpenDay_Click(object sender, RoutedEventArgs e) => await SaveSelectedDayAsync(true);
    private async void SetClosedDay_Click(object sender, RoutedEventArgs e) => await SaveSelectedDayAsync(false);

    private async Task SaveSelectedDayAsync(bool isOpen)
    {
        if (CourseDays.SelectedItem is not CourseDayItem selected) { Show(InfoBarSeverity.Warning, "日付を選択してください", ""); return; }
        await ExecuteAsync(async path =>
        {
            var slots = await App.CourseSettings.GetTimeSlotsAsync(path);
            await App.CourseSettings.SaveCourseDayAsync(path, new CourseDay(selected.Date, isOpen, isOpen ? "" : "休校", isOpen ? slots.Where(x => x.Active).Select(x => x.Id).ToArray() : []));
        }, isOpen ? "開校日に設定しました" : "休校日に設定しました");
    }

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
        var slots = await App.CourseSettings.GetTimeSlotsAsync(path);
        TimeSlots.ItemsSource = slots.Select(x => $"{x.SortOrder}　{x.Code}　{x.DisplayName}　{x.StartTime:HH\\:mm}～{x.EndTime:HH\\:mm}").ToArray();
        CourseDays.ItemsSource = (await App.CourseSettings.GetCourseDaysAsync(path)).Select(x => new CourseDayItem(x.Date, x.IsOpen ? "開校" : "休校", x.IsOpen ? $"{x.EnabledTimeSlotIds.Count}コマ" : "-")).ToArray();
    }

    private void Show(InfoBarSeverity severity, string title, string message) { Status.Severity = severity; Status.Title = title; Status.Message = message; Status.IsOpen = true; }

    private sealed record CourseDayItem(DateOnly Date, string StatusLabel, string SlotSummary);
}
