using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Application.MasterData;
using Windows.Storage.Pickers;
using WinRT.Interop;

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

    private async void ExportMasterWorkbook_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "共通基本情報" };
            picker.FileTypeChoices.Add("Excelブック", [".xlsx"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            IsEnabled = false; await App.MasterDataWorkbook.ExportAsync(App.ProjectService.Current!.Path, file.Path);
            Show(InfoBarSeverity.Success, "共通基本情報を出力しました", file.Path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or SqliteException)
        {
            Show(InfoBarSeverity.Error, "Excelを出力できませんでした", exception.Message);
        }
        finally { IsEnabled = true; }
    }

    private async void ImportMasterWorkbook_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".xlsx"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            IsEnabled = false; var preview = await App.MasterDataWorkbook.PreviewAsync(App.ProjectService.Current!.Path, file.Path);
            var summary = BuildPreviewSummary(preview);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = preview.HasErrors ? "取込エラーがあります" : "共通基本情報を反映しますか？",
                Content = new ScrollViewer { MaxHeight = 520, Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                CloseButtonText = preview.HasErrors ? "閉じる" : "キャンセル",
                PrimaryButtonText = preview.HasErrors ? null : "反映する",
                DefaultButton = preview.HasErrors ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            IsEnabled = true;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            IsEnabled = false; var result = await App.MasterDataWorkbook.ApplyAsync(App.ProjectService.Current!.Path, preview); await ReloadAsync();
            Show(InfoBarSeverity.Success, "共通基本情報を反映しました", $"{result.ImportedRows}行（警告{result.WarningCount}件）");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or SqliteException)
        {
            Show(InfoBarSeverity.Error, "Excelを取り込めませんでした", exception.Message);
        }
        finally { IsEnabled = true; }
    }

    private static string BuildPreviewSummary(MasterWorkbookPreview preview)
    {
        var lines = new List<string> { "シート                         新規  更新" };
        foreach (var name in new[] { "生徒", "講師", "科目", "講師対応科目", "受講希望" }) lines.Add($"{name,-14} {preview.NewCounts.GetValueOrDefault(name),4} {preview.UpdateCounts.GetValueOrDefault(name),5}");
        if (preview.Issues.Count != 0)
        {
            lines.Add(""); lines.Add($"検証結果（エラー{preview.Issues.Count(issue => issue.Severity == MasterWorkbookIssueSeverity.Error)}件・警告{preview.WarningCount}件）");
            lines.AddRange(preview.Issues.Take(100).Select(issue => $"{issue.SheetName} {(issue.RowNumber is null ? "" : $"{issue.RowNumber}行 ")}{issue.ColumnName}: {issue.Message}"));
            if (preview.Issues.Count > 100) lines.Add($"ほか{preview.Issues.Count - 100}件");
        }
        return string.Join(Environment.NewLine, lines);
    }

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
