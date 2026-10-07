using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Application.GroupLessons;
using SeminarSched.Domain.GroupLessons;
using SeminarSched.Domain.MasterData;
using Windows.UI;

namespace SeminarSched_WinUI.Pages;

public sealed partial class GroupLessonClassPage : WorkflowPageBase
{
    private static readonly string[] WeekdayHeaders = ["日", "月", "火", "水", "木", "金", "土"];

    private GroupLessonClass? _selectedGroupClass;
    private IReadOnlyList<GroupLessonCalendarDate> _calendarDates = [];
    private IReadOnlyList<GroupLessonSessionOption> _allSessions = [];
    private readonly HashSet<long> _selectedDateIds = [];

    public GroupLessonClassPage()
    {
        InitializeComponent();
        SessionStartTime.ItemsSource = TimeOfDayOptions.Values; SessionStartTime.Text = "17:10";
        SessionEndTime.ItemsSource = TimeOfDayOptions.Values; SessionEndTime.Text = "18:30";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var ready = EnsureGroupLessonsEnabled();
        ContentPanel.IsEnabled = ready;
        if (!ready) return;
        RenderWeekdayHeader();
        await ReloadClassesAsync();
        await ReloadCalendarDataAsync();
        RenderCalendar();
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

    private void ShowError(string message) { Status.Severity = InfoBarSeverity.Error; Status.Title = "処理できませんでした"; Status.Message = message; Status.IsOpen = true; }
    // ユーザー要望「保存などの成功通知を、固定位置のページ内表示ではなくスライドイン式の
    // 通知にしたい」への対応。Successはトーストへ、Warning/Errorは従来どおりページ内へ表示する。
    private void Show(InfoBarSeverity severity, string title)
    {
        if (severity == InfoBarSeverity.Success) { ToastNotificationState.ShowSuccess(title); return; }
        Status.Severity = severity; Status.Title = title; Status.Message = ""; Status.IsOpen = true;
    }

    private sealed record GroupClassRow(GroupLessonClass Value, string Display) { public override string ToString() => Display; }
    private sealed record TeacherOption(long Id, string Label) { public override string ToString() => Label; }

    private async Task ReloadClassesAsync()
    {
        var path = App.ProjectService.Current?.Path; if (path is null) return;
        var previousSessionClassId = (SessionClassBox.SelectedItem as GroupClassRow)?.Value.Id;
        var classes = await App.GroupLessons.GetClassesAsync(path);
        var rows = classes.Select(c => new GroupClassRow(c, $"{c.Name}　{c.Subject}　（{c.Grade}）{(c.AllowOtherGrades ? "　他学年可" : "")}{(c.Active ? "" : "　[停止]")}")).ToArray();
        GroupClasses.ItemsSource = rows;
        SessionClassBox.ItemsSource = rows;
        SessionClassBox.SelectedItem = rows.FirstOrDefault(r => r.Value.Id == previousSessionClassId) ?? rows.FirstOrDefault();
        var gradeValues = (await App.MasterData.GetStudentsAsync(path)).Select(s => s.Grade).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
        GroupClassGrade.ItemsSource = gradeValues;
        var previousTeacherId = (GroupClassTeacher.SelectedItem as TeacherOption)?.Id;
        var teacherOptions = (await App.MasterData.GetTeachersAsync(path)).Where(t => t.Active).Select(t => new TeacherOption(t.Id, t.FullName)).ToArray();
        GroupClassTeacher.ItemsSource = teacherOptions;
        GroupClassTeacher.SelectedItem = teacherOptions.FirstOrDefault(t => t.Id == previousTeacherId);
    }

    private void GroupClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupClasses.SelectedItem is not GroupClassRow row)
        {
            _selectedGroupClass = null;
            return;
        }
        _selectedGroupClass = row.Value;
        GroupClassName.Text = row.Value.Name; GroupClassSubject.Text = row.Value.Subject; GroupClassGrade.Text = row.Value.Grade; GroupClassAllowOtherGrades.IsChecked = row.Value.AllowOtherGrades;
        GroupClassHasTeacher.IsChecked = row.Value.TeacherId is not null;
        GroupClassTeacher.SelectedItem = (GroupClassTeacher.ItemsSource as IEnumerable<TeacherOption>)?.FirstOrDefault(t => t.Id == row.Value.TeacherId);
        GroupClassMinGapMinutes.Value = row.Value.MinGapMinutes;
    }

    private void GroupClassHasTeacher_Changed(object sender, RoutedEventArgs e)
    {
        var hasTeacher = GroupClassHasTeacher.IsChecked == true;
        GroupClassTeacher.Visibility = hasTeacher ? Visibility.Visible : Visibility.Collapsed;
        GroupClassTeacherHint.Visibility = hasTeacher ? Visibility.Visible : Visibility.Collapsed;
        GroupClassMinGapMinutes.Visibility = hasTeacher ? Visibility.Visible : Visibility.Collapsed;
        GroupClassMinGapHint.Visibility = hasTeacher ? Visibility.Visible : Visibility.Collapsed;
        if (!hasTeacher) GroupClassTeacher.SelectedItem = null;
    }

    private async void SaveGroupClass_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupClassName.Text?.Trim() ?? "";
        var subject = GroupClassSubject.Text?.Trim() ?? "";
        var grade = (GroupClassGrade.Text ?? "").Trim();
        if (name.Length == 0 || subject.Length == 0 || grade.Length == 0) { ShowError("クラス名・科目・対象学年を入力してください。"); return; }
        long? teacherId = null;
        if (GroupClassHasTeacher.IsChecked == true)
        {
            if (GroupClassTeacher.SelectedItem is not TeacherOption teacher) { ShowError("担当講師を選択するか、チェックを外してください。"); return; }
            teacherId = teacher.Id;
        }
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            var id = _selectedGroupClass?.Id ?? 0;
            var minGapMinutes = teacherId is null ? 0 : checked((int)GroupClassMinGapMinutes.Value);
            var saved = await App.GroupLessons.SaveClassAsync(path, new GroupLessonClass(id, name, grade, subject, GroupClassAllowOtherGrades.IsChecked == true, teacherId: teacherId, minGapMinutes: minGapMinutes));
            _selectedGroupClass = saved;
            await ReloadClassesAsync();
            await ReloadCalendarDataAsync();
            RenderCalendar();
            Show(InfoBarSeverity.Success, "集団授業クラスを保存しました");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { ShowError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private void NewGroupClass_Click(object sender, RoutedEventArgs e)
    {
        GroupClasses.SelectedItem = null; _selectedGroupClass = null;
        GroupClassName.Text = ""; GroupClassSubject.Text = ""; GroupClassGrade.Text = ""; GroupClassAllowOtherGrades.IsChecked = false;
        GroupClassHasTeacher.IsChecked = false; GroupClassTeacher.SelectedItem = null; GroupClassMinGapMinutes.Value = 0;
    }

    private async void DeleteGroupClass_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroupClass is not { } cls) { Show(InfoBarSeverity.Warning, "一覧から削除するクラスを選択してください"); return; }
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.GroupLessons.DeleteClassAsync(path, cls.Id);
            NewGroupClass_Click(sender, e);
            await ReloadClassesAsync();
            await ReloadCalendarDataAsync();
            RenderCalendar();
            Show(InfoBarSeverity.Success, "集団授業クラスを削除しました");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { ShowError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private async Task ReloadCalendarDataAsync()
    {
        var path = App.ProjectService.Current?.Path; if (path is null) return;
        _calendarDates = await App.GroupLessons.GetCalendarDatesAsync(path);
        _allSessions = await App.GroupLessons.GetAllSessionsAsync(path);
    }

    private void RenderWeekdayHeader()
    {
        CalendarWeekdayHeader.Children.Clear(); CalendarWeekdayHeader.ColumnDefinitions.Clear();
        foreach (var _ in WeekdayHeaders) CalendarWeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < WeekdayHeaders.Length; i++)
        {
            var text = new TextBlock { Text = WeekdayHeaders[i], FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4) };
            Grid.SetColumn(text, i); CalendarWeekdayHeader.Children.Add(text);
        }
    }

    private void RenderCalendar()
    {
        SessionCalendar.Children.Clear(); SessionCalendar.RowDefinitions.Clear(); SessionCalendar.ColumnDefinitions.Clear();
        UpdateCalendarSelectionCount();
        for (var i = 0; i < 7; i++) SessionCalendar.ColumnDefinitions.Add(new ColumnDefinition());
        if (_calendarDates.Count == 0) return;

        var leading = (int)_calendarDates[0].Date.DayOfWeek;
        var totalCells = leading + _calendarDates.Count;
        var rows = (int)Math.Ceiling(totalCells / 7.0);
        for (var i = 0; i < rows; i++) SessionCalendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < _calendarDates.Count; i++)
        {
            var date = _calendarDates[i];
            var cellIndex = leading + i;
            var cell = BuildDayCell(date);
            Grid.SetRow(cell, cellIndex / 7); Grid.SetColumn(cell, cellIndex % 7);
            SessionCalendar.Children.Add(cell);
        }
    }

    private FrameworkElement BuildDayCell(GroupLessonCalendarDate date)
    {
        var selected = _selectedDateIds.Contains(date.OpenDateId);
        var stack = new StackPanel { Spacing = 2, MinHeight = 90 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var checkBox = new CheckBox { IsChecked = selected, Tag = date.OpenDateId, MinWidth = 0, Padding = new Thickness(0) };
        checkBox.Checked += DateCheckBox_Changed; checkBox.Unchecked += DateCheckBox_Changed;
        header.Children.Add(checkBox);
        header.Children.Add(new TextBlock { Text = date.Date.ToString("M/d(ddd)", CultureInfo.GetCultureInfo("ja-JP")), FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(header);

        foreach (var session in _allSessions.Where(s => s.OpenDateId == date.OpenDateId))
        {
            var row = new Grid { ColumnSpacing = 2 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // ユーザー指示「クラス名＋科目と時刻は別の行にしておく」への対応。
            var text = new TextBlock { Text = $"{session.ClassName} {session.ClassSubject}\n{session.TimeRangeLabel}", FontSize = 10, TextWrapping = TextWrapping.Wrap };
            var deleteButton = new Button { Content = "×", FontSize = 10, Padding = new Thickness(4, 0, 4, 0), Tag = session.Id };
            deleteButton.Click += DeleteSession_Click;
            Grid.SetColumn(deleteButton, 1);
            row.Children.Add(text); row.Children.Add(deleteButton);
            stack.Children.Add(row);
        }

        return new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(selected ? Color.FromArgb(255, 224, 236, 255) : Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 39, 103, 197) : Color.FromArgb(255, 220, 226, 234)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Child = stack,
        };
    }

    private void DateCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: long openDateId } checkBox) return;
        if (checkBox.IsChecked == true) _selectedDateIds.Add(openDateId); else _selectedDateIds.Remove(openDateId);
        UpdateCalendarSelectionCount();
    }

    private void UpdateCalendarSelectionCount() => CalendarSelectionCount.Text = $"{_selectedDateIds.Count}日選択中";

    private void ClearCalendarSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectedDateIds.Clear();
        RenderCalendar();
    }

    private async void AddSessionsToSelectedDates_Click(object sender, RoutedEventArgs e)
    {
        if (SessionClassBox.SelectedItem is not GroupClassRow row) { ShowError("クラスを選択してください。"); return; }
        if (_selectedDateIds.Count == 0) { ShowError("カレンダーで日付を1件以上選択してください。"); return; }
        var startTime = TimeOfDayOptions.Parse(SessionStartTime.Text);
        var endTime = TimeOfDayOptions.Parse(SessionEndTime.Text);
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.GroupLessons.AddSessionsAsync(path, row.Value.Id, _selectedDateIds.ToArray(), startTime, endTime);
            var count = _selectedDateIds.Count;
            _selectedDateIds.Clear();
            await ReloadCalendarDataAsync();
            RenderCalendar();
            Show(InfoBarSeverity.Success, $"{count}日へ「{row.Value.Name} {startTime:HH\\:mm}～{endTime:HH\\:mm}」を追加しました");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or FormatException)
        { ShowError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long sessionId }) return;
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.GroupLessons.RemoveSessionAsync(path, sessionId);
            await ReloadCalendarDataAsync();
            RenderCalendar();
            Show(InfoBarSeverity.Success, "開講日程を削除しました");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { ShowError(exception.Message); }
        finally { IsEnabled = true; }
    }
}
