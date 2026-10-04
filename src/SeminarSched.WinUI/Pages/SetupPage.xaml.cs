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
    // ユーザー要望（checkpoint128）「順序はもう矢印で設定できるので表示しておく必要はない」への
    // 対応。コマ設定フォームから「順序」NumberBoxを削除し、代わりにこのフィールドで内部管理する
    // （挙動は従来のNumberBox.Valueベースの実装と同一: 新規追加のたびに+1、既存コマ編集時は
    // その順序を保持、▲▼ボタンでの並び替えは別経路でDB側を直接更新する）。
    private int _slotEditOrder = 1;
    private bool _loading;
    private MasterItem<Student>[] _studentItems = [];
    private MasterItem<Teacher>[] _teacherItems = [];
    private MasterItem<Subject>[] _subjectItems = [];
    private MasterItem<Teacher?>[] _nullableTeacherItems = [];
    private Dictionary<(long TeacherId,long SubjectId),TeacherQualification> _qualifications = new();
    private readonly ObservableCollection<TimeSlotItem> _timeSlotItems = new();
    private CourseDay[] _courseDays = [];
    private readonly HashSet<DateOnly> _selectedDates = new();

    // ユーザー要望（checkpoint123）「探索方針の優先度を変えられるようにしたい」への対応。
    // 並び替えUIのロジック自体はSchedulingPolicyRowsControllerへ切り出し、OptimizationPageの
    // 一時上書き（checkpoint126）と共有している。
    public ObservableCollection<SchedulingPolicyRowViewModel> PolicyRows { get; } = new();

    private readonly SchedulingPolicyRowsController _policyController;

    public SetupPage()
    {
        InitializeComponent();
        TimeSlots.ItemsSource = _timeSlotItems;
        SlotStartTime.ItemsSource = TimeOfDayOptions.Values;
        SlotEndTime.ItemsSource = TimeOfDayOptions.Values;
        RenderCalendarWeekdayHeader();
        _policyController = new SchedulingPolicyRowsController(PolicyRows, PolicyRowsList);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // ユーザー要望（checkpoint122）「左側のタブにもこれらを選択できるようにしておきたい」
        // への対応。ナビゲーションペインの子項目（1.1〜1.8）から遷移してきた場合は、先に
        // 希望タブへ切り替えてからSetupPageNavState.Activate()で左ナビへ現在のタブを知らせる。
        // プロジェクト未選択でTabs.IsEnabled=falseになる場合でも、左ナビの子項目自体は表示する。
        if (SetupPageNavState.RequestedTabIndex is { } requestedTabIndex)
        {
            SetupPageNavState.RequestedTabIndex = null;
            Tabs.SelectedIndex = requestedTabIndex;
        }
        SetupPageNavState.Activate(Tabs.SelectedIndex);

        var current = App.ProjectService.Current;
        if (!EnsureProject(ProjectRequired) || current is null) { Tabs.IsEnabled = false; return; }
        // 不具合修正: このPageはNavigationCacheMode="Required"で使い回されるため、プロジェクトが
        // 無い状態で一度でもここへ来てTabs.IsEnabled=falseになると、以降プロジェクトを開いて
        // 再訪しても誰も戻していなかった（ユーザー報告「設定だけ全部グレー表示で入力できない。
        // 再起動しても治らない」）。成功時は必ず再度trueへ戻す。
        Tabs.IsEnabled = true;
        CourseDayPeriodLabel.Text = $"{current.StartDate:yyyy年M月d日} ～ {current.EndDate:yyyy年M月d日}（変更はすぐに保存されます）";
        await ReloadAsync();
        ResetStudent();
        ResetTeacher();
        await LoadOutputSettingsAsync(current.Path);
        await LoadSchedulingPolicyAsync(current.Path);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => SetupPageNavState.Deactivate();

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetupPageNavState.SetSelectedTabIndex(Tabs.SelectedIndex);

    // MainWindowがナビゲーションペインの子項目（1.1〜1.8）をクリックしたとき、既にSetupPageが
    // 開かれている場合はFrame.Navigateを経由せずここを直接呼ぶ（同じPage型へのFrame.Navigateは
    // 何も起きないため、RequestedTabIndex経由のPage_Loaded消費では届かない）。
    public void SelectTab(int index) => Tabs.SelectedIndex = index;

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
        _policyController.Load(policy);
        PolicyMaxConcurrentSeats.Value = policy.MaxConcurrentSeats;
        PolicyContinueBeyondNominalTimeYes.IsChecked = policy.ContinueBeyondNominalTimeIfIncomplete;
        PolicyContinueBeyondNominalTimeNo.IsChecked = !policy.ContinueBeyondNominalTimeIfIncomplete;
    }

    private void PolicyOption_Loaded(object sender, RoutedEventArgs e) => _policyController.OptionLoaded(sender, e);

    private void PolicyOption_Checked(object sender, RoutedEventArgs e) => _policyController.OptionChecked(sender, e);

    private void MovePolicyRowUp_Click(object sender, RoutedEventArgs e) => _policyController.MoveUp(sender, e);

    private void MovePolicyRowDown_Click(object sender, RoutedEventArgs e) => _policyController.MoveDown(sender, e);

    private void PolicyRowsList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e) => _policyController.DragItemsCompleted(sender, e);

    private void PolicyRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SchedulingPolicyRowsController.RowPointerEntered(sender, e);

    private void PolicyRow_PointerExited(object sender, PointerRoutedEventArgs e) => SchedulingPolicyRowsController.RowPointerExited(sender, e);

    private SchedulingPolicy BuildSchedulingPolicyFromForm() => SchedulingPolicyRowViewModel.BuildPolicy(
        PolicyRows,
        checked((int)PolicyMaxStudentsPerTeacher.Value),
        checked((int)PolicyMaxConcurrentSeats.Value),
        PolicyContinueBeyondNominalTimeNo.IsChecked != true);

    private async void SaveSchedulingPolicy_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.SchedulingPolicy.SaveAsync(path, BuildSchedulingPolicyFromForm());
    }, "スケジュール設定を保存しました");

    private async void AddStudent_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveStudentAsync(path, new Student(_studentEditId, StudentId.Text, StudentName.Text, StudentGrade.Text, checked((int)StudentMaximum.Value), StudentAllowGap.IsChecked == true, StudentNote.Text, StudentActive.IsChecked == true));
    }, "生徒を保存しました", ResetStudent);

    private async void AddTeacher_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        await App.MasterData.SaveTeacherAsync(path, new Teacher(_teacherEditId, TeacherId.Text, TeacherName.Text, TeacherAllowGap.IsChecked == true, TeacherNote.Text, TeacherActive.IsChecked == true));
    }, "講師を保存しました", ResetTeacher);

    private async void AddSubject_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        var nextOrder = SubjectOrder.Value + 1;
        await App.MasterData.SaveSubjectAsync(path, new Subject(_subjectEditId, SubjectCode.Text, SubjectName.Text, SubjectShort.Text, SubjectLevel.Text, checked((int)SubjectOrder.Value), SubjectActive.IsChecked == true));
        ResetSubject(); SubjectOrder.Value = nextOrder;
    }, "科目を保存しました");

    private async void AddSlot_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async path =>
    {
        var nextOrder = _slotEditOrder + 1;
        await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(_slotEditId, SlotCode.Text, SlotName.Text,
            TimeOfDayOptions.Parse(SlotStartTime.Text), TimeOfDayOptions.Parse(SlotEndTime.Text), _slotEditOrder, SlotActive.IsChecked == true));
        ResetSlot(); _slotEditOrder = nextOrder;
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
            TimeOfDayOptions.Parse(SlotStartTime.Text), TimeOfDayOptions.Parse(SlotEndTime.Text), _slotEditOrder, SlotActive.IsChecked == true)), "有効状態を更新しました");
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
        var value=selected.Value;_slotEditId=value.Id;SlotCode.Text=value.Code;SlotName.Text=value.DisplayName;SlotStartTime.Text=TimeOfDayOptions.Format(value.StartTime);SlotEndTime.Text=TimeOfDayOptions.Format(value.EndTime);_slotEditOrder=value.SortOrder;SlotActive.IsChecked=value.Active;
    }
    private void NewStudent_Click(object sender,RoutedEventArgs e)=>ResetStudent();
    private void NewTeacher_Click(object sender,RoutedEventArgs e)=>ResetTeacher();

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
    // ユーザー要望「生徒IDは操作者には変更させず、氏名・学年のみを入力させて保存することで、
    // 自動的にIDが割り振られるようにしてください」への対応。生徒ID・講師IDは基本的に小さい順に
    // 追加されていくため、「新規入力」の時点で次に採番されるIDをあらかじめ表示しておく
    // （StudentId/TeacherIdはIsEnabled=falseで直接編集不可。従来の「IDを自動採番」ボタンは廃止）。
    private void ResetStudent(){_studentEditId=0;Students.SelectedItem=null;StudentId.Text=NextExternalId(_studentItems.Select(x=>x.Value.ExternalId),"S-");StudentName.Text=StudentGrade.Text=StudentNote.Text="";StudentMaximum.Value=2;StudentAllowGap.IsChecked=false;StudentActive.IsChecked=true;}
    private void ResetTeacher(){_teacherEditId=0;Teachers.SelectedItem=null;TeacherId.Text=NextExternalId(_teacherItems.Select(x=>x.Value.ExternalId),"T-");TeacherName.Text=TeacherNote.Text="";TeacherAllowGap.IsChecked=false;TeacherActive.IsChecked=true;}
    private void ResetSubject(){_subjectEditId=0;Subjects.SelectedItem=null;SubjectCode.Text=SubjectName.Text=SubjectShort.Text=SubjectLevel.Text="";SubjectOrder.Value=1;SubjectActive.IsChecked=true;}
    private void ResetSlot(){_slotEditId=0;TimeSlots.SelectedItem=null;SlotCode.Text=SlotName.Text="";SlotStartTime.Text="09:00";SlotEndTime.Text="10:00";_slotEditOrder=1;SlotActive.IsChecked=true;}

    // ユーザー要望（checkpoint142）「講師指導可能科目について、表以外は消す。表だけで十分です」への
    // 対応で入力フォームを削除したため、備考だけは表の○セルを右クリック/長押し（RightTapped）した
    // ときに出す小さなダイアログへ移した（指導可否自体はCanTeachをそのまま維持し、備考のみ更新）。
    private async void QualificationCell_RightTapped(object sender,Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if(sender is not Button{Tag:(long teacherId,long subjectId)})return;
        var existing=_qualifications.GetValueOrDefault((teacherId,subjectId));
        var noteBox=new TextBox{Text=existing?.Note??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=80};
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="備考を編集",Content=noteBox,PrimaryButtonText="保存",CloseButtonText="キャンセル",DefaultButton=ContentDialogButton.Primary};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        await ExecuteAsync(async path=>
        {
            await App.MasterData.SaveQualificationAsync(path,new TeacherQualification(teacherId,subjectId,existing?.CanTeach??false,noteBox.Text));
        },"備考を保存しました");
    }

    // ユーザー要望「小学校、中学校、高校の並びになるようにしてください。これは他の部分でも同じで、
    // 順番が変わっているところは、小中高の順番で」への対応。SchoolLevelは自由入力の文字列
    // （"小学校"/"中学校"/"高校"/"高等学校"等）のため、そのままOrderByすると文字コード順
    // （例: "中学校"<"小学校"<"高等学校"）になってしまい、小中高の順にならない。先頭文字で
    // 明示的に並べ替える（ExcelScheduleReportRenderer/PdfScheduleReportRendererの学年表記判定と
    // 同じ「先頭1文字」方式）。
    private static int SchoolLevelSortKey(string schoolLevel) =>
        schoolLevel.Length == 0 ? 3 : schoolLevel[0] switch { '小' => 0, '中' => 1, '高' => 2, _ => 3 };

    // ユーザー要望「科目名に『高校・英語』などの『高校・』はつけないでください。『英語』のみで
    // 大丈夫です」への対応。校種は表の列見出し（校種ごとのグループ見出し行）で既に表示しているため、
    // 科目名側の重複した接頭辞は表示上だけ取り除く（保存されている科目名そのものは変更しない）。
    private static string StripSchoolLevelPrefix(string displayName)
    {
        foreach (var prefix in new[] { "小学校・", "中学校・", "高等学校・", "高校・" })
            if (displayName.StartsWith(prefix, StringComparison.Ordinal)) return displayName[prefix.Length..];
        return displayName;
    }

    private const double QualificationHeaderRow0Height = 32;
    private const double QualificationHeaderRow1MinHeight = 56;
    private const double QualificationDataRowHeight = 40;
    private const double QualificationColumnWidth = 70;
    private const double QualificationHeaderCellPadding = 8; // Border Padding=Thickness(4)の左右・上下合計

    // ユーザー指摘「教科によってははみでてしまう。教科の文字数に応じて、縦の長さを変更してください」
    // への対応。見出し2段目（科目名）の高さは固定値だったため、列幅70pxで折り返したときに2行を
    // 超える長い科目名（例: 「算数（中学受験）」）が高さに収まらず上下が欠けて表示されていた。
    // 科目名ラベルを実際のセルと同じ幅・折り返し設定で仮測定し、最も高さを要する科目に合わせて
    // 見出し行の高さを動的に決める（短い科目名しかない場合は従来どおりの最小値のまま）。
    private static double MeasureWrappedTextHeight(string text, double maxWidth, Windows.UI.Text.FontWeight weight)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        probe.Measure(new Windows.Foundation.Size(maxWidth, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }

    // ユーザー要望（checkpoint128）「横にスライドして動かしても、講師ID、講師氏名は左側に固定して
    // ほしい。縦にスクロールした場合に、科目が動かないのも同様に」への対応。1つの大きなGridを
    // 単一のScrollViewerで両方向にスクロールする従来方式から、Excelのウィンドウ枠固定と同じ
    // 4分割構成（角=QualificationMatrixCorner・見出し=QualificationMatrixHeader（横だけ連動
    // スクロール）・左列=QualificationMatrixLeft（縦だけ連動スクロール）・本体=
    // QualificationMatrixBody（両方向スクロール、操作の起点））へ変更した。行の高さを全ペインで
    // 固定値に揃えることで、ペイン間のズレを防いでいる（Autoサイズだとボタンとラベルで実測の
    // 高さが微妙に異なりズレる可能性があるため）。見出し・左列のScrollViewerはIsHitTestVisible=
    // Falseにして直接操作できないようにし、本体のスクロールにだけ追従する一方通行の同期にした。
    private void RenderQualificationMatrix()
    {
        QualificationMatrixCorner.Children.Clear(); QualificationMatrixCorner.RowDefinitions.Clear(); QualificationMatrixCorner.ColumnDefinitions.Clear();
        QualificationMatrixHeader.Children.Clear(); QualificationMatrixHeader.RowDefinitions.Clear(); QualificationMatrixHeader.ColumnDefinitions.Clear();
        QualificationMatrixLeft.Children.Clear(); QualificationMatrixLeft.RowDefinitions.Clear(); QualificationMatrixLeft.ColumnDefinitions.Clear();
        QualificationMatrixBody.Children.Clear(); QualificationMatrixBody.RowDefinitions.Clear(); QualificationMatrixBody.ColumnDefinitions.Clear();

        var teachers=_teacherItems.Where(t=>t.Value.Active).OrderBy(t=>t.Value.ExternalId).ToArray();
        var subjects=_subjectItems.Where(s=>s.Value.Active).OrderBy(s=>SchoolLevelSortKey(s.Value.SchoolLevel)).ThenBy(s=>s.Value.SortOrder).ToArray();
        if(teachers.Length==0||subjects.Length==0)
        {
            QualificationMatrixBody.Children.Add(new TextBlock{Text="有効な講師・科目がありません。",Margin=new Thickness(8)});
            return;
        }

        QualificationMatrixCorner.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
        QualificationMatrixCorner.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(140)});
        QualificationMatrixLeft.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
        QualificationMatrixLeft.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(140)});
        foreach(var _ in subjects)
        {
            QualificationMatrixHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(QualificationColumnWidth)});
            QualificationMatrixBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(QualificationColumnWidth)});
        }

        var headerRow1Height=QualificationHeaderRow1MinHeight;
        foreach(var s in subjects)
        {
            var label=StripSchoolLevelPrefix(s.Value.DisplayName);
            var measured=MeasureWrappedTextHeight(label,QualificationColumnWidth-QualificationHeaderCellPadding,Microsoft.UI.Text.FontWeights.SemiBold)+QualificationHeaderCellPadding;
            if(measured>headerRow1Height)headerRow1Height=measured;
        }

        QualificationMatrixCorner.RowDefinitions.Add(new RowDefinition{Height=new GridLength(QualificationHeaderRow0Height)});
        QualificationMatrixCorner.RowDefinitions.Add(new RowDefinition{Height=new GridLength(headerRow1Height)});
        QualificationMatrixHeader.RowDefinitions.Add(new RowDefinition{Height=new GridLength(QualificationHeaderRow0Height)});
        QualificationMatrixHeader.RowDefinitions.Add(new RowDefinition{Height=new GridLength(headerRow1Height)});
        foreach(var _ in teachers)
        {
            QualificationMatrixLeft.RowDefinitions.Add(new RowDefinition{Height=new GridLength(QualificationDataRowHeight)});
            QualificationMatrixBody.RowDefinitions.Add(new RowDefinition{Height=new GridLength(QualificationDataRowHeight)});
        }

        static void Place(Grid grid,FrameworkElement element,int row,int column,int columnSpan=1)
        {
            Grid.SetRow(element,row);Grid.SetColumn(element,column);Grid.SetColumnSpan(element,columnSpan);
            grid.Children.Add(element);
        }

        // ユーザー指摘（checkpoint127）「枠線を少し濃くしてください」への対応。
        var cellBorderBrush=new SolidColorBrush(Windows.UI.Color.FromArgb(255,150,158,171));
        var headerBackground=ResourceBrush("CardBackgroundFillColorSecondaryBrush",Windows.UI.Color.FromArgb(255,242,244,247));
        Border HeaderCell(string text)=>new(){Background=headerBackground,BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1),Padding=new Thickness(4),Child=new TextBlock{Text=text,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};

        // 角（左上、常に固定）: 講師ID・講師氏名の見出し。
        Place(QualificationMatrixCorner,new Border{Background=headerBackground,BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1)},0,0,2);
        Place(QualificationMatrixCorner,HeaderCell("講師ID"),1,0);
        Place(QualificationMatrixCorner,HeaderCell("講師氏名"),1,1);

        // 見出し（右上、横だけ本体に連動してスクロール）: 校種グループ＋科目名。
        var columnIndex=0;
        foreach(var group in subjects.GroupBy(s=>s.Value.SchoolLevel))
        {
            var count=group.Count();
            Place(QualificationMatrixHeader,HeaderCell(group.Key),0,columnIndex,count);
            columnIndex+=count;
        }
        for(var c=0;c<subjects.Length;c++)Place(QualificationMatrixHeader,HeaderCell(StripSchoolLevelPrefix(subjects[c].Value.DisplayName)),1,c);

        for(var r=0;r<teachers.Length;r++)
        {
            var teacher=teachers[r].Value;
            // 左列（縦だけ本体に連動してスクロール）: 講師ID・講師氏名。
            Place(QualificationMatrixLeft,new Border{BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1),Padding=new Thickness(4),Child=new TextBlock{Text=teacher.ExternalId,VerticalAlignment=VerticalAlignment.Center}},r,0);
            Place(QualificationMatrixLeft,new Border{BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1),Padding=new Thickness(4),Child=new TextBlock{Text=teacher.Name,VerticalAlignment=VerticalAlignment.Center}},r,1);
            for(var c=0;c<subjects.Length;c++)
            {
                var subject=subjects[c].Value;
                var canTeach=_qualifications.TryGetValue((teacher.Id,subject.Id),out var q)&&q.CanTeach;
                var cell=new Button{Content=canTeach?"○":"",Tag=(teacher.Id,subject.Id),BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(0)};
                cell.Click+=QualificationCell_Click;
                cell.RightTapped+=QualificationCell_RightTapped;
                Place(QualificationMatrixBody,cell,r,c);
            }
        }
    }

    private void QualificationMatrixBodyScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        QualificationMatrixHeaderScroll.ChangeView(QualificationMatrixBodyScroll.HorizontalOffset, null, null, true);
        QualificationMatrixLeftScroll.ChangeView(null, QualificationMatrixBodyScroll.VerticalOffset, null, true);
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

    // onReloaded: 保存が成功しReloadAsync()で最新の一覧（_studentItems等）が反映された「後」にだけ
    // 呼ばれる（保存前や失敗時には呼ばれない）。生徒/講師の次回採番ID（NextExternalId）は直近の
    // 保存結果を踏まえて計算する必要があるため、ResetStudent/ResetTeacherをここへ渡す。
    private async Task ExecuteAsync(Func<string, Task> action, string success, Action? onReloaded = null)
    {
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await action(path); await ReloadAsync();
            onReloaded?.Invoke();
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
            var studentItems=studentValues.Select(x => new MasterItem<Student>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.Name}　{x.Grade}",x.Active,"在籍中","卒業・無効")).ToArray();
            var teacherItems=teacherValues.Select(x => new MasterItem<Teacher>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.Name}",x.Active,"在籍中","卒業・無効")).ToArray();
            var subjectItems=subjectValues.Select(x => new MasterItem<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
            _studentItems=studentItems;_teacherItems=teacherItems;_subjectItems=subjectItems;
            ApplyStudentFilter();ApplyTeacherFilter();ApplySubjectFilter();
            RegularStudent.ItemsSource=studentItems;RegularSubject.ItemsSource=subjectItems;
            _nullableTeacherItems=new[]{new MasterItem<Teacher?>(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new MasterItem<Teacher?>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.Name}"))).ToArray();
            RegularTeacher.ItemsSource=_nullableTeacherItems;if(RegularTeacher.SelectedIndex<0)RegularTeacher.SelectedIndex=0;
            var qualifications=await App.MasterData.GetQualificationsAsync(path);_qualifications=qualifications.ToDictionary(value=>(value.TeacherId,value.SubjectId));RenderQualificationMatrix();
            // ユーザー要望「生徒IDや講師IDは基本的に用いず、内部の処理にのみ使いたいので、ここでの
            // 表示は生徒氏名、講師氏名のみとしてください」への対応。IDは内部処理（保存・照合）だけに
            // 使い、一覧表示は氏名のみにする。
            var regularLessons=await App.MasterData.GetRegularLessonsAsync(path);RegularLessons.ItemsSource=regularLessons.Select(value=>new RegularLessonItem(
                studentValues.Single(x=>x.Id==value.StudentId).Name,
                subjectValues.Single(x=>x.Id==value.SubjectId).DisplayName,
                value.RegularTeacherId is long id?teacherValues.Single(x=>x.Id==id).Name:"指定なし",
                value.RegularTeacherPriority,
                value.OneToOneRequired?"1対1":"通常")).ToArray();
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

    // ユーザー要望「保存などの成功通知を、固定位置のページ内表示ではなくスライドイン式の
    // 通知にしたい」への対応。Success（単発の操作結果を知らせるだけのもの）はトーストへ、
    // Warning/Error（ユーザーが対処すべき状態が残るもの）は従来どおりページ内のStatusへ表示する。
    private void Show(InfoBarSeverity severity, string title, string message)
    {
        if (severity == InfoBarSeverity.Success) { ToastNotificationState.ShowSuccess(title); return; }
        Status.Severity = severity; Status.Title = title; Status.Message = message; Status.IsOpen = true;
    }

    // ユーザー指示「状態を『有効』『無効』ではなくて、『在籍中』『卒業・無効』にしてください」への対応。
    // 生徒・講師は人（在籍/卒業という概念が成り立つ）だが、科目・コマは物なので「在籍中」は意味が
    // 通らない。ActiveLabel/InactiveLabelを生徒・講師の構築箇所だけ上書きし、科目・コマは既定の
    // 「有効」「停止」のまま維持する。
    private sealed record MasterItem<T>(T Value,string Display,bool Active=true,string ActiveLabel="有効",string InactiveLabel="停止")
    {
        public string StatusText=>Active?ActiveLabel:InactiveLabel;
        public override string ToString()=>Display;
    }

    private sealed record RegularLessonItem(string StudentName,string SubjectName,string TeacherName,int Priority,string OneToOneText);

    private sealed record TimeSlotItem(TimeSlot Value,string StartText,string EndText)
    {
        public string StatusText=>Value.Active?"有効":"停止";
        public override string ToString()=>$"{(Value.Active?"":"[停止] ")}{Value.SortOrder}　{Value.Code}　{Value.DisplayName}　{StartText}～{EndText}";
    }
}
