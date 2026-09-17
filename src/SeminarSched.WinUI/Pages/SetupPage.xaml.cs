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
    private long _studentEditId;
    private long _teacherEditId;
    private long _subjectEditId;
    private long _slotEditId;
    private bool _loading;
    private MasterItem<Student>[] _studentItems = [];
    private MasterItem<Teacher>[] _teacherItems = [];
    private MasterItem<Subject>[] _subjectItems = [];

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
        await App.MasterData.SaveStudentAsync(path, new Student(_studentEditId, StudentId.Text, StudentName.Text, StudentGrade.Text, checked((int)StudentMaximum.Value), StudentAllowGap.IsChecked == true, StudentNote.Text, StudentActive.IsChecked == true));
        ResetStudent();
    }, "生徒を保存しました");

    private async void AddTeacher_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveTeacherAsync(path, new Teacher(_teacherEditId, TeacherId.Text, TeacherName.Text, TeacherAllowGap.IsChecked == true, TeacherNote.Text, TeacherActive.IsChecked == true));
        ResetTeacher();
    }, "講師を保存しました");

    private async void AddSubject_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        var nextOrder = SubjectOrder.Value + 1;
        await App.MasterData.SaveSubjectAsync(path, new Subject(_subjectEditId, SubjectCode.Text, SubjectName.Text, SubjectShort.Text, SubjectLevel.Text, checked((int)SubjectOrder.Value), SubjectActive.IsChecked == true));
        ResetSubject(); SubjectOrder.Value = nextOrder;
    }, "科目を保存しました");

    private async void AddSlot_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        var nextOrder = SlotOrder.Value + 1;
        await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(_slotEditId, SlotCode.Text, SlotName.Text,
            TimeOnly.FromTimeSpan(SlotStart.Time), TimeOnly.FromTimeSpan(SlotEnd.Time), checked((int)SlotOrder.Value), SlotActive.IsChecked == true));
        ResetSlot(); SlotOrder.Value = nextOrder;
    }, "コマを保存しました");

    private void Students_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Students.SelectedItem is not MasterItem<Student> selected) return;
        var value=selected.Value;_studentEditId=value.Id;StudentId.Text=value.ExternalId;StudentName.Text=value.Name;StudentGrade.Text=value.Grade;StudentMaximum.Value=value.DefaultMaxConsecutiveSlots;StudentAllowGap.IsChecked=value.AllowGap;StudentNote.Text=value.Note;StudentActive.IsChecked=value.Active;
    }
    private void Teachers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Teachers.SelectedItem is not MasterItem<Teacher> selected) return;
        var value=selected.Value;_teacherEditId=value.Id;TeacherId.Text=value.ExternalId;TeacherName.Text=value.Name;TeacherAllowGap.IsChecked=value.AllowGap;TeacherNote.Text=value.Note;TeacherActive.IsChecked=value.Active;
    }
    private void Subjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Subjects.SelectedItem is not MasterItem<Subject> selected) return;
        var value=selected.Value;_subjectEditId=value.Id;SubjectCode.Text=value.Code;SubjectName.Text=value.DisplayName;SubjectShort.Text=value.ShortName;SubjectLevel.Text=value.SchoolLevel;SubjectOrder.Value=value.SortOrder;SubjectActive.IsChecked=value.Active;
    }
    private void TimeSlots_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TimeSlots.SelectedItem is not MasterItem<TimeSlot> selected) return;
        var value=selected.Value;_slotEditId=value.Id;SlotCode.Text=value.Code;SlotName.Text=value.DisplayName;SlotStart.Time=value.StartTime.ToTimeSpan();SlotEnd.Time=value.EndTime.ToTimeSpan();SlotOrder.Value=value.SortOrder;SlotActive.IsChecked=value.Active;
    }
    private void NewStudent_Click(object sender,RoutedEventArgs e)=>ResetStudent();
    private void NewTeacher_Click(object sender,RoutedEventArgs e)=>ResetTeacher();
    private void NewSubject_Click(object sender,RoutedEventArgs e)=>ResetSubject();
    private void NewSlot_Click(object sender,RoutedEventArgs e)=>ResetSlot();
    private void ResetStudent(){_studentEditId=0;Students.SelectedItem=null;StudentId.Text=StudentName.Text=StudentGrade.Text=StudentNote.Text="";StudentMaximum.Value=2;StudentAllowGap.IsChecked=false;StudentActive.IsChecked=true;}
    private void ResetTeacher(){_teacherEditId=0;Teachers.SelectedItem=null;TeacherId.Text=TeacherName.Text=TeacherNote.Text="";TeacherAllowGap.IsChecked=false;TeacherActive.IsChecked=true;}
    private void ResetSubject(){_subjectEditId=0;Subjects.SelectedItem=null;SubjectCode.Text=SubjectName.Text=SubjectShort.Text=SubjectLevel.Text="";SubjectOrder.Value=1;SubjectActive.IsChecked=true;}
    private void ResetSlot(){_slotEditId=0;TimeSlots.SelectedItem=null;SlotCode.Text=SlotName.Text="";SlotStart.Time=new TimeSpan(9,0,0);SlotEnd.Time=new TimeSpan(10,0,0);SlotOrder.Value=1;SlotActive.IsChecked=true;}

    private async void SaveQualification_Click(object sender,RoutedEventArgs e)=>await ExecuteAsync(async path=>
    {
        if(QualificationTeacher.SelectedItem is not MasterItem<Teacher> teacher||QualificationSubject.SelectedItem is not MasterItem<Subject> subject)throw new ArgumentException("講師と科目を選択してください。");
        await App.MasterData.SaveQualificationAsync(path,new TeacherQualification(teacher.Value.Id,subject.Value.Id,QualificationCanTeach.IsChecked==true,QualificationNote.Text));
    },"講師対応科目を保存しました");

    private async void SaveBulkQualifications_Click(object sender,RoutedEventArgs e)=>await ExecuteAsync(async path=>
    {
        var teachers=BulkQualificationTeachers.SelectedItems.Cast<MasterItem<Teacher>>().ToArray();
        var subjects=BulkQualificationSubjects.SelectedItems.Cast<MasterItem<Subject>>().ToArray();
        if(teachers.Length==0||subjects.Length==0)throw new ArgumentException("講師と科目をそれぞれ1件以上選択してください。");
        foreach(var teacher in teachers)
            foreach(var subject in subjects)
                await App.MasterData.SaveQualificationAsync(path,new TeacherQualification(teacher.Value.Id,subject.Value.Id,BulkQualificationCanTeach.IsChecked==true));
    },"講師対応科目を一括設定しました");

    private async void SaveRegularLesson_Click(object sender,RoutedEventArgs e)=>await ExecuteAsync(async path=>
    {
        if(RegularStudent.SelectedItem is not MasterItem<Student> student||RegularSubject.SelectedItem is not MasterItem<Subject> subject)throw new ArgumentException("生徒と科目を選択してください。");
        var teacher=(RegularTeacher.SelectedItem as MasterItem<Teacher?>)?.Value;
        await App.MasterData.SaveRegularLessonAsync(path,new RegularLessonProfile(0,student.Value.Id,subject.Value.Id,teacher?.Id,checked((int)RegularPriority.Value),RegularOneToOne.IsChecked==true,RegularNote.Text));
    },"通常授業の担当設定を保存しました");

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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or SqliteException or OverflowException)
        {
            Show(InfoBarSeverity.Error, "保存できませんでした", exception is SqliteException { SqliteErrorCode: 19 } ? "IDまたはコードが重複しています。" : exception.Message);
        }
        finally { IsEnabled = true; }
    }

    private async Task ReloadAsync()
    {
        var path = App.ProjectService.Current!.Path;
        _loading=true;
        try
        {
            var studentValues=await App.MasterData.GetStudentsAsync(path);var teacherValues=await App.MasterData.GetTeachersAsync(path);var subjectValues=await App.MasterData.GetSubjectsAsync(path);
            var studentItems=studentValues.Select(x => new MasterItem<Student>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}　{x.Grade}")).ToArray();
            var teacherItems=teacherValues.Select(x => new MasterItem<Teacher>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}")).ToArray();
            var subjectItems=subjectValues.Select(x => new MasterItem<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
            _studentItems=studentItems;_teacherItems=teacherItems;_subjectItems=subjectItems;
            ApplyStudentFilter();ApplyTeacherFilter();ApplySubjectFilter();
            QualificationTeacher.ItemsSource=teacherItems;QualificationSubject.ItemsSource=subjectItems;RegularStudent.ItemsSource=studentItems;RegularSubject.ItemsSource=subjectItems;
            BulkQualificationTeachers.ItemsSource=teacherItems;BulkQualificationSubjects.ItemsSource=subjectItems;
            RegularTeacher.ItemsSource=new[]{new MasterItem<Teacher?>(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new MasterItem<Teacher?>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}"))).ToArray();if(RegularTeacher.SelectedIndex<0)RegularTeacher.SelectedIndex=0;
            var qualifications=await App.MasterData.GetQualificationsAsync(path);Qualifications.ItemsSource=qualifications.Select(value=>$"{teacherValues.Single(x=>x.Id==value.TeacherId).ExternalId}　{subjectValues.Single(x=>x.Id==value.SubjectId).Code}　{(value.CanTeach?"指導可能":"不可")}　{value.Note}").ToArray();
            var regularLessons=await App.MasterData.GetRegularLessonsAsync(path);RegularLessons.ItemsSource=regularLessons.Select(value=>$"{studentValues.Single(x=>x.Id==value.StudentId).ExternalId}　{subjectValues.Single(x=>x.Id==value.SubjectId).Code}　通常担当: {(value.RegularTeacherId is long id?teacherValues.Single(x=>x.Id==id).ExternalId:"指定なし")}　優先度{value.RegularTeacherPriority}　{(value.OneToOneRequired?"1対1":"通常")}").ToArray();
            var slots = await App.CourseSettings.GetTimeSlotsAsync(path);
            TimeSlots.ItemsSource = slots.Select(x => new MasterItem<TimeSlot>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}　{x.StartTime:HH\\:mm}～{x.EndTime:HH\\:mm}")).ToArray();
            CourseDays.ItemsSource = (await App.CourseSettings.GetCourseDaysAsync(path)).Select(x => new CourseDayItem(x.Date, x.IsOpen ? "開校" : "休校", x.IsOpen ? $"{x.EnabledTimeSlotIds.Count}コマ" : "-")).ToArray();
        }
        finally{_loading=false;}
    }

    private void StudentSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyStudentFilter();
    private void TeacherSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyTeacherFilter();
    private void SubjectSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySubjectFilter();

    private void ApplyStudentFilter() => Students.ItemsSource = Filter(_studentItems, StudentSearch.Text);
    private void ApplyTeacherFilter() => Teachers.ItemsSource = Filter(_teacherItems, TeacherSearch.Text);
    private void ApplySubjectFilter() => Subjects.ItemsSource = Filter(_subjectItems, SubjectSearch.Text);

    private static MasterItem<T>[] Filter<T>(MasterItem<T>[] items, string query) =>
        string.IsNullOrWhiteSpace(query) ? items : items.Where(item => item.Display.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();

    private void Show(InfoBarSeverity severity, string title, string message) { Status.Severity = severity; Status.Title = title; Status.Message = message; Status.IsOpen = true; }

    private sealed record CourseDayItem(DateOnly Date, string StatusLabel, string SlotSummary);
    private sealed record MasterItem<T>(T Value,string Display){public override string ToString()=>Display;}
}
