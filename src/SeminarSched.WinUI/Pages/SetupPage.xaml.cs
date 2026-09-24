using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.Output;
using SeminarSched.Domain.Scheduling;
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
    private MasterItem<Teacher?>[] _nullableTeacherItems = [];
    private Dictionary<(long TeacherId,long SubjectId),TeacherQualification> _qualifications = new();
    private readonly ObservableCollection<TimeSlotItem> _timeSlotItems = new();
    private CourseDay[] _courseDays = [];
    private readonly HashSet<DateOnly> _selectedDates = new();

    public SetupPage() { InitializeComponent(); TimeSlots.ItemsSource = _timeSlotItems; SlotStartTime.ItemsSource = TimeOfDayOptions.Values; SlotEndTime.ItemsSource = TimeOfDayOptions.Values; RenderCalendarWeekdayHeader(); }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var current = App.ProjectService.Current;
        if (!EnsureProject(ProjectRequired) || current is null) { Tabs.IsEnabled = false; return; }
        CourseDayPeriodLabel.Text = $"{current.StartDate:yyyy年M月d日} ～ {current.EndDate:yyyy年M月d日}（変更はすぐに保存されます）";
        await ReloadAsync();
        await LoadOutputSettingsAsync(current.Path);
        await LoadSchedulingPolicyAsync(current.Path);
    }

    private async Task LoadOutputSettingsAsync(string path)
    {
        var settings = await App.OutputSettings.GetAsync(path);
        OutputPaperSize.SelectedItem = OutputPaperSize.Items.Cast<ComboBoxItem>().First(i => (string)i.Content == settings.PaperSize);
        OutputOrientation.SelectedItem = OutputOrientation.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == settings.Orientation);
        OutputMarginMm.Value = settings.MarginMm;
        OutputFileNamePattern.Text = settings.FileNamePattern;
        OutputClosedColor.Text = settings.ClosedFillHex;
        OutputUnavailableColor.Text = settings.UnavailableFillHex;
        OutputGroupColor.Text = settings.GroupFillHex;
    }

    private async void SaveOutputSettings_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        if (OutputPaperSize.SelectedItem is not ComboBoxItem paperSizeItem || OutputOrientation.SelectedItem is not ComboBoxItem orientationItem)
            throw new ArgumentException("用紙サイズと向きを選択してください。");
        var settings = new OutputSettings(
            (string)paperSizeItem.Content, (string)orientationItem.Tag, OutputMarginMm.Value, OutputFileNamePattern.Text,
            OutputClosedColor.Text, OutputUnavailableColor.Text, OutputGroupColor.Text);
        await App.OutputSettings.SaveAsync(path, settings);
    }, "出力設定を保存しました");

    // ユーザー要望「担当する生徒の人数・一日当たりの講師人数・講師ごとのコマ数の偏り・生徒の授業日・
    // 1コマあたりの生徒対応人数・時間帯・同時に使える座席数を自由に選択できるようにしたい。後から
    // 設定でも変更できるようにしてほしい」への対応。ここで保存した内容がプロジェクトの既定値になる
    // （⑤時間割自動作成の画面でその回だけ上書きすることもできる）。
    private async Task LoadSchedulingPolicyAsync(string path)
    {
        var policy = await App.SchedulingPolicy.GetAsync(path);
        PolicyMaxStudentsPerTeacher.Value = policy.MaxStudentsPerTeacher;
        SetSchedulingPolicyRadios(policy);
        PolicyMaxConcurrentSeats.Value = policy.MaxConcurrentSeats;
    }

    private void SetSchedulingPolicyRadios(SchedulingPolicy policy)
    {
        PolicyTeacherCountPerDayMinimize.IsChecked = policy.TeacherCountPerDayPreference == TeacherCountPerDayPreference.Minimize;
        PolicyTeacherCountPerDayMaximize.IsChecked = policy.TeacherCountPerDayPreference == TeacherCountPerDayPreference.Maximize;
        PolicyTeacherCountPerDayNone.IsChecked = policy.TeacherCountPerDayPreference == TeacherCountPerDayPreference.None;

        PolicyTeacherLoadBalanceBalance.IsChecked = policy.TeacherLoadBalancePreference == TeacherLoadBalancePreference.Balance;
        PolicyTeacherLoadBalanceNone.IsChecked = policy.TeacherLoadBalancePreference == TeacherLoadBalancePreference.None;

        PolicyStudentAttendanceDaysConcentrate.IsChecked = policy.StudentAttendanceDaysPreference == StudentAttendanceDaysPreference.Concentrate;
        PolicyStudentAttendanceDaysSpread.IsChecked = policy.StudentAttendanceDaysPreference == StudentAttendanceDaysPreference.Spread;
        PolicyStudentAttendanceDaysNone.IsChecked = policy.StudentAttendanceDaysPreference == StudentAttendanceDaysPreference.None;

        PolicyPairingSizeMaximize.IsChecked = policy.PairingSizePreference == PairingSizePreference.Maximize;
        PolicyPairingSizeMinimize.IsChecked = policy.PairingSizePreference == PairingSizePreference.Minimize;
        PolicyPairingSizeNone.IsChecked = policy.PairingSizePreference == PairingSizePreference.None;

        PolicyTimeOfDayLate.IsChecked = policy.TimeOfDayPreference == TimeOfDayPreference.Late;
        PolicyTimeOfDayEarly.IsChecked = policy.TimeOfDayPreference == TimeOfDayPreference.Early;
        PolicyTimeOfDayNone.IsChecked = policy.TimeOfDayPreference == TimeOfDayPreference.None;

        PolicyContinueBeyondNominalTimeYes.IsChecked = policy.ContinueBeyondNominalTimeIfIncomplete;
        PolicyContinueBeyondNominalTimeNo.IsChecked = !policy.ContinueBeyondNominalTimeIfIncomplete;
    }

    private SchedulingPolicy BuildSchedulingPolicyFromForm() => new(
        checked((int)PolicyMaxStudentsPerTeacher.Value),
        PolicyTeacherCountPerDayMinimize.IsChecked == true ? TeacherCountPerDayPreference.Minimize
            : PolicyTeacherCountPerDayMaximize.IsChecked == true ? TeacherCountPerDayPreference.Maximize
            : TeacherCountPerDayPreference.None,
        PolicyTeacherLoadBalanceBalance.IsChecked == true ? TeacherLoadBalancePreference.Balance : TeacherLoadBalancePreference.None,
        PolicyStudentAttendanceDaysConcentrate.IsChecked == true ? StudentAttendanceDaysPreference.Concentrate
            : PolicyStudentAttendanceDaysSpread.IsChecked == true ? StudentAttendanceDaysPreference.Spread
            : StudentAttendanceDaysPreference.None,
        PolicyPairingSizeMaximize.IsChecked == true ? PairingSizePreference.Maximize
            : PolicyPairingSizeMinimize.IsChecked == true ? PairingSizePreference.Minimize
            : PairingSizePreference.None,
        PolicyTimeOfDayLate.IsChecked == true ? TimeOfDayPreference.Late
            : PolicyTimeOfDayEarly.IsChecked == true ? TimeOfDayPreference.Early
            : TimeOfDayPreference.None,
        checked((int)PolicyMaxConcurrentSeats.Value),
        PolicyContinueBeyondNominalTimeNo.IsChecked != true);

    private async void SaveSchedulingPolicy_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.SchedulingPolicy.SaveAsync(path, BuildSchedulingPolicyFromForm());
    }, "スケジュール設定を保存しました");

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
            TimeOfDayOptions.Parse(SlotStartTime.Text), TimeOfDayOptions.Parse(SlotEndTime.Text), checked((int)SlotOrder.Value), SlotActive.IsChecked == true));
        ResetSlot(); SlotOrder.Value = nextOrder;
    }, "コマを保存しました");

    // 「有効」チェックボックスだけは、保存ボタンを押さずにチェックの変更だけでそのまま即座に保存する
    // （既存の項目を選択している場合のみ。新規入力フォームの初期値やResetXxx()での既定値設定でも
    // Checked/Uncheckedは発火するが、その時点では_studentEditId等が0のため何もしない）。
    private async void StudentActive_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _studentEditId == 0) return;
        await ExecuteAsync(async path => await App.MasterData.SaveStudentAsync(path, new Student(_studentEditId, StudentId.Text, StudentName.Text, StudentGrade.Text, checked((int)StudentMaximum.Value), StudentAllowGap.IsChecked == true, StudentNote.Text, StudentActive.IsChecked == true)), "有効状態を更新しました");
    }

    private async void TeacherActive_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _teacherEditId == 0) return;
        await ExecuteAsync(async path => await App.MasterData.SaveTeacherAsync(path, new Teacher(_teacherEditId, TeacherId.Text, TeacherName.Text, TeacherAllowGap.IsChecked == true, TeacherNote.Text, TeacherActive.IsChecked == true)), "有効状態を更新しました");
    }

    private async void SubjectActive_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _subjectEditId == 0) return;
        await ExecuteAsync(async path => await App.MasterData.SaveSubjectAsync(path, new Subject(_subjectEditId, SubjectCode.Text, SubjectName.Text, SubjectShort.Text, SubjectLevel.Text, checked((int)SubjectOrder.Value), SubjectActive.IsChecked == true)), "有効状態を更新しました");
    }

    private async void SlotActive_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _slotEditId == 0) return;
        await ExecuteAsync(async path => await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(_slotEditId, SlotCode.Text, SlotName.Text,
            TimeOfDayOptions.Parse(SlotStartTime.Text), TimeOfDayOptions.Parse(SlotEndTime.Text), checked((int)SlotOrder.Value), SlotActive.IsChecked == true)), "有効状態を更新しました");
    }

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
        if (_loading || TimeSlots.SelectedItem is not TimeSlotItem selected) return;
        var value=selected.Value;_slotEditId=value.Id;SlotCode.Text=value.Code;SlotName.Text=value.DisplayName;SlotStartTime.Text=TimeOfDayOptions.Format(value.StartTime);SlotEndTime.Text=TimeOfDayOptions.Format(value.EndTime);SlotOrder.Value=value.SortOrder;SlotActive.IsChecked=value.Active;
    }
    private void NewStudent_Click(object sender,RoutedEventArgs e)=>ResetStudent();
    private void NewTeacher_Click(object sender,RoutedEventArgs e)=>ResetTeacher();
    private void AutoNumberStudentId_Click(object sender,RoutedEventArgs e)=>StudentId.Text=NextExternalId(_studentItems.Select(x=>x.Value.ExternalId),"S-");
    private void AutoNumberTeacherId_Click(object sender,RoutedEventArgs e)=>TeacherId.Text=NextExternalId(_teacherItems.Select(x=>x.Value.ExternalId),"T-");

    private static string NextExternalId(IEnumerable<string> existingIds, string defaultPrefix)
    {
        var pattern = new Regex(@"^(.*?)(\d+)$");
        var matches = existingIds.Select(id => pattern.Match(id)).Where(m => m.Success).ToArray();
        if (matches.Length == 0) return $"{defaultPrefix}001";
        var group = matches.GroupBy(m => m.Groups[1].Value).OrderByDescending(g => g.Count()).First();
        var width = group.Max(m => m.Groups[2].Value.Length);
        var next = group.Max(m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) + 1;
        return $"{group.Key}{next.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}";
    }
    private void NewSubject_Click(object sender,RoutedEventArgs e)=>ResetSubject();
    private void NewSlot_Click(object sender,RoutedEventArgs e)=>ResetSlot();

    private async void DeleteSlot_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not FrameworkElement{Tag:long slotId})return;
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="このコマを削除しますか？",Content="時間割配置で既に使われているコマは削除できません。",PrimaryButtonText="削除",CloseButtonText="キャンセル",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        try
        {
            IsEnabled=false;
            var path=App.ProjectService.Current?.Path??throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.CourseSettings.DeleteTimeSlotAsync(path,slotId);
            if(_slotEditId==slotId)ResetSlot();
            await ReloadAsync();
            Show(InfoBarSeverity.Success,"コマを削除しました","");
        }
        catch(SqliteException exception)
        {
            Show(InfoBarSeverity.Error,"コマを削除できませんでした",exception.SqliteErrorCode==19?"このコマは時間割配置または出勤可否情報で使われているため削除できません。":exception.Message);
        }
        catch(Exception exception)when(exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Show(InfoBarSeverity.Error,"コマを削除できませんでした",exception.Message);
        }
        finally{IsEnabled=true;}
    }
    private void ResetStudent(){_studentEditId=0;Students.SelectedItem=null;StudentId.Text=StudentName.Text=StudentGrade.Text=StudentNote.Text="";StudentMaximum.Value=2;StudentAllowGap.IsChecked=false;StudentActive.IsChecked=true;}
    private void ResetTeacher(){_teacherEditId=0;Teachers.SelectedItem=null;TeacherId.Text=TeacherName.Text=TeacherNote.Text="";TeacherAllowGap.IsChecked=false;TeacherActive.IsChecked=true;}
    private void ResetSubject(){_subjectEditId=0;Subjects.SelectedItem=null;SubjectCode.Text=SubjectName.Text=SubjectShort.Text=SubjectLevel.Text="";SubjectOrder.Value=1;SubjectActive.IsChecked=true;}
    private void ResetSlot(){_slotEditId=0;TimeSlots.SelectedItem=null;SlotCode.Text=SlotName.Text="";SlotStartTime.Text="09:00";SlotEndTime.Text="10:00";SlotOrder.Value=1;SlotActive.IsChecked=true;}

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
        var note=BulkQualificationNote.Text;
        var existing=note.Length==0?(await App.MasterData.GetQualificationsAsync(path)).ToDictionary(q=>(q.TeacherId,q.SubjectId),q=>q.Note):null;
        foreach(var teacher in teachers)
            foreach(var subject in subjects)
            {
                var effectiveNote=note.Length!=0?note:existing!.GetValueOrDefault((teacher.Value.Id,subject.Value.Id),"");
                await App.MasterData.SaveQualificationAsync(path,new TeacherQualification(teacher.Value.Id,subject.Value.Id,BulkQualificationCanTeach.IsChecked==true,effectiveNote));
            }
    },"講師対応科目を一括設定しました");

    private void RenderQualificationMatrix()
    {
        QualificationMatrix.Children.Clear();QualificationMatrix.RowDefinitions.Clear();QualificationMatrix.ColumnDefinitions.Clear();
        var teachers=_teacherItems.Where(t=>t.Value.Active).OrderBy(t=>t.Value.ExternalId).ToArray();
        var subjects=_subjectItems.Where(s=>s.Value.Active).OrderBy(s=>s.Value.SchoolLevel).ThenBy(s=>s.Value.SortOrder).ToArray();
        if(teachers.Length==0||subjects.Length==0)
        {
            QualificationMatrix.Children.Add(new TextBlock{Text="有効な講師・科目がありません。",Margin=new Thickness(8)});
            return;
        }

        QualificationMatrix.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
        QualificationMatrix.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(140)});
        foreach(var _ in subjects)QualificationMatrix.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(70)});
        QualificationMatrix.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        QualificationMatrix.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        foreach(var _ in teachers)QualificationMatrix.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        void Place(FrameworkElement element,int row,int column,int columnSpan=1)
        {
            Grid.SetRow(element,row);Grid.SetColumn(element,column);Grid.SetColumnSpan(element,columnSpan);
            QualificationMatrix.Children.Add(element);
        }
        var headerBackground=ResourceBrush("CardBackgroundFillColorSecondaryBrush",Windows.UI.Color.FromArgb(255,242,244,247));
        Border HeaderCell(string text)=>new(){Background=headerBackground,Padding=new Thickness(4),Child=new TextBlock{Text=text,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center}};

        var columnIndex=2;
        foreach(var group in subjects.GroupBy(s=>s.Value.SchoolLevel))
        {
            var count=group.Count();
            Place(HeaderCell(group.Key),0,columnIndex,count);
            columnIndex+=count;
        }
        Place(HeaderCell("講師ID"),1,0);
        Place(HeaderCell("講師氏名"),1,1);
        for(var c=0;c<subjects.Length;c++)Place(HeaderCell(subjects[c].Value.DisplayName),1,2+c);

        for(var r=0;r<teachers.Length;r++)
        {
            var teacher=teachers[r].Value;
            var row=2+r;
            Place(new Border{Padding=new Thickness(4),Child=new TextBlock{Text=teacher.ExternalId,VerticalAlignment=VerticalAlignment.Center}},row,0);
            Place(new Border{Padding=new Thickness(4),Child=new TextBlock{Text=teacher.Name,VerticalAlignment=VerticalAlignment.Center}},row,1);
            for(var c=0;c<subjects.Length;c++)
            {
                var subject=subjects[c].Value;
                var canTeach=_qualifications.TryGetValue((teacher.Id,subject.Id),out var q)&&q.CanTeach;
                var cell=new Button{Content=canTeach?"○":"",Tag=(teacher.Id,subject.Id),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new Thickness(0,6,0,6)};
                cell.Click+=QualificationCell_Click;
                Place(cell,row,2+c);
            }
        }
    }

    private static Microsoft.UI.Xaml.Media.Brush ResourceBrush(string key,Windows.UI.Color fallback)
        =>Application.Current.Resources.TryGetValue(key,out var value)&&value is Microsoft.UI.Xaml.Media.Brush brush?brush:new Microsoft.UI.Xaml.Media.SolidColorBrush(fallback);

    private async void QualificationCell_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:(long teacherId,long subjectId)})return;
        var existing=_qualifications.GetValueOrDefault((teacherId,subjectId));
        var newCanTeach=!(existing?.CanTeach??false);
        await ExecuteAsync(async path=>
        {
            await App.MasterData.SaveQualificationAsync(path,new TeacherQualification(teacherId,subjectId,newCanTeach,existing?.Note??""));
        },newCanTeach?"指導可能に設定しました":"指導不可に設定しました");
    }

    private async void SaveRegularLesson_Click(object sender,RoutedEventArgs e)=>await ExecuteAsync(async path=>
    {
        if(RegularStudent.SelectedItem is not MasterItem<Student> student||RegularSubject.SelectedItem is not MasterItem<Subject> subject)throw new ArgumentException("生徒と科目を選択してください。");
        var teacher=(RegularTeacher.SelectedItem as MasterItem<Teacher?>)?.Value;
        await App.MasterData.SaveRegularLessonAsync(path,new RegularLessonProfile(0,student.Value.Id,subject.Value.Id,teacher?.Id,checked((int)RegularPriority.Value),RegularOneToOne.IsChecked==true,RegularNote.Text));
    },"通常授業の担当設定を保存しました");

    private static readonly string[] WeekdayHeaders = ["日", "月", "火", "水", "木", "金", "土"];

    private void RenderCalendarWeekdayHeader()
    {
        CalendarWeekdayHeader.Children.Clear(); CalendarWeekdayHeader.ColumnDefinitions.Clear();
        foreach (var _ in WeekdayHeaders) CalendarWeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < WeekdayHeaders.Length; i++)
        {
            var text = new TextBlock { Text = WeekdayHeaders[i], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4) };
            Grid.SetColumn(text, i); CalendarWeekdayHeader.Children.Add(text);
        }
    }

    private void RenderCourseDayCalendar()
    {
        CourseDayCalendar.Children.Clear(); CourseDayCalendar.RowDefinitions.Clear(); CourseDayCalendar.ColumnDefinitions.Clear();
        for (var i = 0; i < 7; i++) CourseDayCalendar.ColumnDefinitions.Add(new ColumnDefinition());
        if (_courseDays.Length == 0) return;
        var leading = (int)_courseDays[0].Date.DayOfWeek;
        var totalCells = leading + _courseDays.Length;
        var rows = (int)Math.Ceiling(totalCells / 7.0);
        for (var i = 0; i < rows; i++) CourseDayCalendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });

        for (var i = 0; i < _courseDays.Length; i++)
        {
            var day = _courseDays[i];
            var cellIndex = leading + i;
            var selected = _selectedDates.Contains(day.Date);
            var border = new Border
            {
                Padding = new Thickness(6), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(selected ? Windows.UI.Color.FromArgb(255, 224, 236, 255) : day.IsOpen ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 242, 244, 247)),
                BorderBrush = new SolidColorBrush(selected ? Windows.UI.Color.FromArgb(255, 39, 103, 197) : Windows.UI.Color.FromArgb(255, 220, 226, 234)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Tag = day.Date,
            };
            border.Tapped += CalendarDay_Tapped;
            var slotSummary = string.Join("・", day.EnabledTimeSlotIds.Select(id => _timeSlotItems.FirstOrDefault(x => x.Value.Id == id)?.Value.Code).Where(code => code is not null));
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new TextBlock { Text = day.Date.ToString("M/d(ddd)", CultureInfo.GetCultureInfo("ja-JP")), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 11 });
            stack.Children.Add(new TextBlock { Text = day.IsOpen ? "✓ 開校" : "－ 休校", Foreground = new SolidColorBrush(day.IsOpen ? Windows.UI.Color.FromArgb(255, 23, 107, 64) : Windows.UI.Color.FromArgb(255, 102, 112, 133)), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (day.IsOpen) stack.Children.Add(new TextBlock { Text = slotSummary, FontSize = 10, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 103, 197)), TextWrapping = TextWrapping.Wrap });
            border.Child = stack;
            Grid.SetRow(border, cellIndex / 7); Grid.SetColumn(border, cellIndex % 7);
            CourseDayCalendar.Children.Add(border);
        }
    }

    private void CalendarDay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DateOnly date }) return;
        if (!_selectedDates.Remove(date)) _selectedDates.Add(date);
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private void RenderCalendarSlotToggles()
    {
        CalendarSlotTogglePanel.Children.Clear();
        foreach (var item in _timeSlotItems.Where(x => x.Value.Active))
        {
            var slot = item.Value;
            var selectedDays = _courseDays.Where(d => _selectedDates.Contains(d.Date)).ToArray();
            var checkedCount = selectedDays.Count(d => d.EnabledTimeSlotIds.Contains(slot.Id));
            var checkBox = new CheckBox
            {
                Content = slot.Code, Tag = slot.Id, IsThreeState = true,
                IsEnabled = _selectedDates.Count > 0,
                IsChecked = selectedDays.Length == 0 ? false : checkedCount == 0 ? false : checkedCount == selectedDays.Length ? true : null,
            };
            checkBox.Click += CalendarSlotToggle_Click;
            CalendarSlotTogglePanel.Children.Add(checkBox);
        }
    }

    private async void CalendarSlotToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: long slotId } checkBox) return;
        var enable = checkBox.IsChecked == true;
        await ExecuteAsync(async path =>
        {
            foreach (var date in _selectedDates)
            {
                var day = _courseDays.First(d => d.Date == date);
                var slots = enable ? day.EnabledTimeSlotIds.Append(slotId).Distinct().ToArray() : day.EnabledTimeSlotIds.Where(id => id != slotId).ToArray();
                await App.CourseSettings.SaveCourseDayAsync(path, new CourseDay(day.Date, day.IsOpen, day.Note, slots));
            }
        }, "選択日のコマを更新しました");
    }

    private async void CalendarAllSlots_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDates.Count == 0) { Show(InfoBarSeverity.Warning, "日付を選択してください", ""); return; }
        var allSlotIds = _timeSlotItems.Where(x => x.Value.Active).Select(x => x.Value.Id).ToArray();
        await ExecuteAsync(async path =>
        {
            foreach (var date in _selectedDates)
            {
                var day = _courseDays.First(d => d.Date == date);
                await App.CourseSettings.SaveCourseDayAsync(path, new CourseDay(day.Date, day.IsOpen, day.Note, allSlotIds));
            }
        }, "選択日のコマを全コマに設定しました");
    }

    // ユーザー指示により「期間内をすべて開校」「指定曜日を休校」という直接操作ボタンは廃止し、代わりに
    // 「対象を選択→選択日を開校/休校」という2段階の操作へ統一した（既存の一括操作と同じ流儀に揃える）。
    private void CalendarSelectClosedDays_Click(object sender, RoutedEventArgs e)
    {
        _selectedDates.Clear(); foreach (var day in _courseDays.Where(d => !d.IsOpen)) _selectedDates.Add(day.Date);
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private void CalendarSelectOpenDays_Click(object sender, RoutedEventArgs e)
    {
        _selectedDates.Clear(); foreach (var day in _courseDays.Where(d => d.IsOpen)) _selectedDates.Add(day.Date);
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private void CalendarSelectWeekday_Click(object sender, RoutedEventArgs e)
    {
        var weekday = (DayOfWeek)CalendarWeekday.SelectedIndex;
        _selectedDates.Clear(); foreach (var day in _courseDays.Where(d => d.Date.DayOfWeek == weekday)) _selectedDates.Add(day.Date);
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private void CalendarSelectAll_Click(object sender, RoutedEventArgs e)
    {
        _selectedDates.Clear(); foreach (var day in _courseDays) _selectedDates.Add(day.Date);
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private void CalendarClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectedDates.Clear();
        RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
    }

    private async void CalendarOpenSelected_Click(object sender, RoutedEventArgs e) => await SetSelectedDatesOpenAsync(true);
    private async void CalendarCloseSelected_Click(object sender, RoutedEventArgs e) => await SetSelectedDatesOpenAsync(false);

    private async Task SetSelectedDatesOpenAsync(bool isOpen)
    {
        if (_selectedDates.Count == 0) { Show(InfoBarSeverity.Warning, "日付を選択してください", ""); return; }
        var allSlotIds = _timeSlotItems.Where(x => x.Value.Active).Select(x => x.Value.Id).ToArray();
        await ExecuteAsync(async path =>
        {
            foreach (var date in _selectedDates)
            {
                var day = _courseDays.First(d => d.Date == date);
                var slots = isOpen && day.EnabledTimeSlotIds.Count == 0 ? allSlotIds : day.EnabledTimeSlotIds;
                await App.CourseSettings.SaveCourseDayAsync(path, new CourseDay(day.Date, isOpen, isOpen ? day.Note : (string.IsNullOrEmpty(day.Note) ? "休校" : day.Note), slots));
            }
        }, $"選択した{_selectedDates.Count}日を{(isOpen ? "開校日" : "休校日")}に設定しました");
    }

    private void UpdateCalendarSelectionCount() => CalendarSelectionCount.Text = $"{_selectedDates.Count}件選択中";

    // 一覧内ドラッグによる並び替え（ListView.CanReorderItems）は、①設定ページ全体を包む外側の
    // ScrollViewerとドラッグの手のひら操作が競合し、見た目上は動いても保存が反映されない不具合が
    // ユーザー実機で複数回再現したため撤廃した。代わりに、隣接する行とSortOrderを直接入れ替える
    // ▲▼ボタンへ置き換え、環境に依存せず確実に並び替え・即時保存できるようにした。
    private async void MoveSlotUp_Click(object sender, RoutedEventArgs e) => await MoveSlotAsync(sender, -1);
    private async void MoveSlotDown_Click(object sender, RoutedEventArgs e) => await MoveSlotAsync(sender, 1);

    // 隣接2件のSortOrderを入れ替えるだけの実装だと、既存データにSortOrderの重複・欠番（旧バージョンの
    // 不具合や既定データ由来で起こり得る）があった場合、入れ替え後も表示順が視覚的に変わらない／
    // 意図と違う場所に来ることがあった（ユーザー報告: 矢印では変わらないが、順序欄へ大きい数値を
    // 直接入力すると変わる＝重複していないユニークな値にした途端に効く、という症状と一致）。
    // 移動後の一覧全体を1から採番し直すことで、既存データの重複・欠番に関わらず必ず意図通りの
    // 順序へ確実に反映されるようにした（旧ドラッグ並び替え実装と同じ「全件を1..Nへ再採番」方式）。
    private async Task MoveSlotAsync(object sender, int direction)
    {
        if (sender is not FrameworkElement { Tag: long slotId }) return;
        var items = _timeSlotItems.Select(item => item.Value).ToList();
        var index = items.FindIndex(x => x.Id == slotId);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= items.Count) return;
        (items[index], items[targetIndex]) = (items[targetIndex], items[index]);
        await ExecuteAsync(async path =>
        {
            for (var i = 0; i < items.Count; i++)
            {
                var slot = items[i];
                if (slot.SortOrder != i + 1)
                    await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(slot.Id, slot.Code, slot.DisplayName, slot.StartTime, slot.EndTime, i + 1, slot.Active));
            }
        }, "コマの表示順を更新しました");
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or SqliteException or OverflowException or FormatException)
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
            var studentItems=studentValues.Select(x => new MasterItem<Student>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}　{x.Grade}",x.Active)).ToArray();
            var teacherItems=teacherValues.Select(x => new MasterItem<Teacher>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}")).ToArray();
            var subjectItems=subjectValues.Select(x => new MasterItem<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
            _studentItems=studentItems;_teacherItems=teacherItems;_subjectItems=subjectItems;
            ApplyStudentFilter();ApplyTeacherFilter();ApplySubjectFilter();
            QualificationTeacher.ItemsSource=teacherItems;QualificationSubject.ItemsSource=subjectItems;RegularStudent.ItemsSource=studentItems;RegularSubject.ItemsSource=subjectItems;
            BulkQualificationTeachers.ItemsSource=teacherItems;BulkQualificationSubjects.ItemsSource=subjectItems;
            _nullableTeacherItems=new[]{new MasterItem<Teacher?>(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new MasterItem<Teacher?>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}"))).ToArray();
            RegularTeacher.ItemsSource=_nullableTeacherItems;if(RegularTeacher.SelectedIndex<0)RegularTeacher.SelectedIndex=0;
            var qualifications=await App.MasterData.GetQualificationsAsync(path);_qualifications=qualifications.ToDictionary(value=>(value.TeacherId,value.SubjectId));RenderQualificationMatrix();
            var regularLessons=await App.MasterData.GetRegularLessonsAsync(path);RegularLessons.ItemsSource=regularLessons.Select(value=>$"{studentValues.Single(x=>x.Id==value.StudentId).ExternalId}　{subjectValues.Single(x=>x.Id==value.SubjectId).Code}　通常担当: {(value.RegularTeacherId is long id?teacherValues.Single(x=>x.Id==id).ExternalId:"指定なし")}　優先度{value.RegularTeacherPriority}　{(value.OneToOneRequired?"1対1":"通常")}").ToArray();
            var slots = await App.CourseSettings.GetTimeSlotsAsync(path);
            _timeSlotItems.Clear();
            foreach (var item in slots.OrderBy(x => x.SortOrder).Select(x => new TimeSlotItem(x,x.StartTime.ToString("HH:mm",CultureInfo.InvariantCulture),x.EndTime.ToString("HH:mm",CultureInfo.InvariantCulture)))) _timeSlotItems.Add(item);
            _courseDays = (await App.CourseSettings.GetCourseDaysAsync(path)).OrderBy(x => x.Date).ToArray();
            _selectedDates.IntersectWith(_courseDays.Select(x => x.Date));
            RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
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

    private sealed record MasterItem<T>(T Value,string Display,bool Active=true)
    {
        public string StatusText=>Active?"有効":"停止";
        public override string ToString()=>Display;
    }

    private sealed record TimeSlotItem(TimeSlot Value,string StartText,string EndText)
    {
        public string StatusText=>Value.Active?"有効":"停止";
        public override string ToString()=>$"{(Value.Active?"":"[停止] ")}{Value.SortOrder}　{Value.Code}　{Value.DisplayName}　{StartText}～{EndText}";
    }
}
