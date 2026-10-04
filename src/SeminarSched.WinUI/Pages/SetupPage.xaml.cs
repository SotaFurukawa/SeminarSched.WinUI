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
    private MasterItem<Student>[] _studentItems = [];
    private MasterItem<Teacher>[] _teacherItems = [];
    private MasterItem<Subject>[] _subjectItems = [];
    // ユーザー要望（checkpoint142）「生徒・講師・科目について、上側で入力させる形式ではないように
    // したい」への対応。一覧行そのものが編集状態を持つ（SetupPageRowViewModels.cs参照）ため、
    // MasterItem<T>配列とは別に、行の編集状態を保持する可変のビューモデル一覧を持つ。
    private List<StudentRowViewModel> _studentRows = [];
    private List<TeacherRowViewModel> _teacherRows = [];
    private List<SubjectRowViewModel> _subjectRows = [];
    // 保存に成功した行は、ReloadAsync直後のRebuildXxxRowsで「編集中だった行」として誤って再び
    // 編集状態へ戻してしまわないよう、保存中の行を記録しておき、その回のRebuildだけ除外する
    // （保存が失敗した場合はReloadAsync自体が呼ばれないため、編集中の入力はそのまま残る）。
    private StudentRowViewModel? _studentRowBeingSaved;
    private TeacherRowViewModel? _teacherRowBeingSaved;
    private SubjectRowViewModel? _subjectRowBeingSaved;
    // ユーザー要望（checkpoint145/146で承認されたPlanのStage 3）「通常授業担当設定とコマについても、
    // 生徒・講師ページと同様の仕様にしてほしい」への対応。ImportPageのLessonRequestRowViewModelと
    // 同じ考え方（行自身がComboBoxの選択肢を持つ）。
    private List<RegularLessonRowViewModel> _regularLessonRows = [];
    private RegularLessonRowViewModel? _regularLessonRowBeingSaved;
    private TimeSlotRowViewModel? _timeSlotRowBeingSaved;
    private IReadOnlyList<NamedOption<Student>> _regularLessonStudentOptions = [];
    private IReadOnlyList<NamedOption<Subject>> _regularLessonSubjectOptions = [];
    private IReadOnlyList<NamedOption<Teacher?>> _regularLessonTeacherOptions = [];
    private Dictionary<(long TeacherId,long SubjectId),TeacherQualification> _qualifications = new();
    private readonly ObservableCollection<TimeSlotRowViewModel> _timeSlotItems = new();
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

    // ユーザー要望（checkpoint142）「生徒・講師・科目について、上側で入力させる形式ではないように
    // したい」への対応。生徒・講師・科目タブの行内編集（追加・変更・保存）。一度に編集状態になれる
    // 行は1つだけ（新規追加・変更どちらを押しても、既存の編集中行は閉じる＝新規なら取り除く）。
    private void AddStudentRow_Click(object sender, RoutedEventArgs e)
    {
        CancelStudentEditing();
        StudentSearch.Text = "";
        var row = StudentRowViewModel.CreateNew(NextExternalId(_studentItems.Select(x => x.Value.ExternalId), "S-"));
        _studentRows.Add(row);
        ApplyStudentFilter();
        ScrollListToBottom(Students);
    }

    private void StudentRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not StudentRowViewModel row) return;
        CancelStudentEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<TextBox>(container) is { } nameBox)
            nameBox.Focus(FocusState.Programmatic);
    }

    private void StudentRowNameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: StudentRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not StudentRowViewModel row) return;
        _studentRowBeingSaved = row;
        await ExecuteAsync(async path =>
        {
            var id = row.IsNew ? 0 : row.Value!.Id;
            var externalId = row.IsNew ? row.PreviewExternalId : row.Value!.ExternalId;
            await App.MasterData.SaveStudentAsync(path, new Student(id, externalId, row.DraftFamilyName, row.DraftGivenName, row.DraftGrade,
                checked((int)row.DraftMaxConsecutiveSlots), row.DraftAllowGap, row.DraftNote, row.DraftActive));
        }, "生徒を保存しました");
    }

    private void CancelStudentEditing()
    {
        var editing = _studentRows.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _studentRows.Remove(editing); else editing.IsEditing = false;
        ApplyStudentFilter();
    }

    private void StudentRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, true);
    private void StudentRow_PointerExited(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, false);

    private void AddTeacherRow_Click(object sender, RoutedEventArgs e)
    {
        CancelTeacherEditing();
        TeacherSearch.Text = "";
        var row = TeacherRowViewModel.CreateNew(NextExternalId(_teacherItems.Select(x => x.Value.ExternalId), "T-"));
        _teacherRows.Add(row);
        ApplyTeacherFilter();
        ScrollListToBottom(Teachers);
    }

    private void TeacherRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TeacherRowViewModel row) return;
        CancelTeacherEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<TextBox>(container) is { } nameBox)
            nameBox.Focus(FocusState.Programmatic);
    }

    private void TeacherRowNameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TeacherRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveTeacherRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TeacherRowViewModel row) return;
        _teacherRowBeingSaved = row;
        await ExecuteAsync(async path =>
        {
            var id = row.IsNew ? 0 : row.Value!.Id;
            var externalId = row.IsNew ? row.PreviewExternalId : row.Value!.ExternalId;
            await App.MasterData.SaveTeacherAsync(path, new Teacher(id, externalId, row.DraftFamilyName, row.DraftGivenName, row.DraftAllowGap, row.DraftNote, row.DraftActive));
        }, "講師を保存しました");
    }

    private void CancelTeacherEditing()
    {
        var editing = _teacherRows.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _teacherRows.Remove(editing); else editing.IsEditing = false;
        ApplyTeacherFilter();
    }

    private void TeacherRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, true);
    private void TeacherRow_PointerExited(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, false);

    private void AddSubjectRow_Click(object sender, RoutedEventArgs e)
    {
        CancelSubjectEditing();
        SubjectSearch.Text = "";
        var nextOrder = _subjectItems.Length == 0 ? 1 : _subjectItems.Max(x => x.Value.SortOrder) + 1;
        var row = SubjectRowViewModel.CreateNew(nextOrder);
        _subjectRows.Add(row);
        ApplySubjectFilter();
        ScrollListToBottom(Subjects);
    }

    private void SubjectRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SubjectRowViewModel row) return;
        CancelSubjectEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<TextBox>(container) is { } codeBox)
            codeBox.Focus(FocusState.Programmatic);
    }

    private void SubjectRowCodeBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: SubjectRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveSubjectRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SubjectRowViewModel row) return;
        _subjectRowBeingSaved = row;
        await ExecuteAsync(async path =>
        {
            var id = row.IsNew ? 0 : row.Value!.Id;
            await App.MasterData.SaveSubjectAsync(path, new Subject(id, row.DraftCode, row.DraftName, row.DraftShortName,
                row.DraftSchoolLevel, checked((int)row.DraftSortOrder), row.DraftActive));
        }, "科目を保存しました");
    }

    private void CancelSubjectEditing()
    {
        var editing = _subjectRows.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _subjectRows.Remove(editing); else editing.IsEditing = false;
        ApplySubjectFilter();
    }

    private void SubjectRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, true);
    private void SubjectRow_PointerExited(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, false);

    private static void SetChangeButtonVisible(Grid displayRow, bool visible) => VisualTreeHelpers.SetNamedChildVisible(displayRow, "ChangeButton", visible);

    // ユーザー要望（checkpoint142）「それを押すと、生徒一覧の最下部に移動する」への対応。この
    // ページのListView（生徒・講師・科目）は、外側のページ全体を包むScrollViewer内のStackPanelに
    // そのまま置かれているため、自身の高さをコンテンツに合わせて伸ばすだけで、スクロールは常に
    // 外側のScrollViewerが担う。ListView.ScrollIntoView()はListView自身のビューポート内でしか
    // 動かないため、新規行の追加時はこの外側のScrollViewerを直接最下部までスクロールする。
    private static void ScrollListToBottom(ListView list)
    {
        list.UpdateLayout();
        if (VisualTreeHelpers.FindAncestor<ScrollViewer>(list) is not { } scrollViewer) return;
        scrollViewer.UpdateLayout();
        scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null);
    }

    private void AddTimeSlotRow_Click(object sender, RoutedEventArgs e)
    {
        CancelTimeSlotEditing();
        var nextOrder = _timeSlotItems.Count == 0 ? 1 : _timeSlotItems.Max(x => x.EffectiveSortOrder) + 1;
        var row = TimeSlotRowViewModel.CreateNew(nextOrder);
        _timeSlotItems.Add(row);
        ScrollListToBottom(TimeSlots);
    }

    private void TimeSlotRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TimeSlotRowViewModel row) return;
        CancelTimeSlotEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<TextBox>(container) is { } codeBox)
            codeBox.Focus(FocusState.Programmatic);
    }

    private void TimeSlotCodeBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TimeSlotRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveTimeSlotRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not TimeSlotRowViewModel row) return;
        _timeSlotRowBeingSaved = row;
        await ExecuteAsync(async path =>
        {
            await App.CourseSettings.SaveTimeSlotAsync(path, new TimeSlot(row.IsNew ? 0 : row.Value!.Id, row.DraftCode, row.DraftDisplayName,
                TimeOfDayOptions.Parse(row.DraftStartText), TimeOfDayOptions.Parse(row.DraftEndText), row.EffectiveSortOrder, row.DraftActive));
        }, "コマを保存しました");
    }

    private void CancelTimeSlotEditing()
    {
        var editing = _timeSlotItems.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _timeSlotItems.Remove(editing); else editing.IsEditing = false;
    }

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
            Place(QualificationMatrixLeft,new Border{BorderBrush=cellBorderBrush,BorderThickness=new Thickness(1),Padding=new Thickness(4),Child=new TextBlock{Text=teacher.FullName,VerticalAlignment=VerticalAlignment.Center}},r,1);
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

    private void ApplyRegularLessonFilter() => RegularLessons.ItemsSource = _regularLessonRows.Where(r => r.IsEditing || r.Matches(RegularLessonSearch.Text)).ToArray();
    private void RegularLessonSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyRegularLessonFilter();

    private void AddRegularLessonRow_Click(object sender, RoutedEventArgs e)
    {
        CancelRegularLessonEditing();
        RegularLessonSearch.Text = "";
        var row = RegularLessonRowViewModel.CreateNew(_regularLessonStudentOptions, _regularLessonSubjectOptions, _regularLessonTeacherOptions);
        _regularLessonRows.Add(row);
        ApplyRegularLessonFilter();
        ScrollListToBottom(RegularLessons);
    }

    private void RegularLessonRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not RegularLessonRowViewModel row) return;
        CancelRegularLessonEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<ComboBox>(container) is { } studentBox)
            studentBox.Focus(FocusState.Programmatic);
    }

    private void RegularLessonStudentBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { DataContext: RegularLessonRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveRegularLessonRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not RegularLessonRowViewModel row) return;
        _regularLessonRowBeingSaved = row;
        await ExecuteAsync(async path =>
        {
            if (row.DraftStudent is null || row.DraftSubject is null) throw new ArgumentException("生徒と科目を選択してください。");
            await App.MasterData.SaveRegularLessonAsync(path, new RegularLessonProfile(0, row.DraftStudent.Value.Id, row.DraftSubject.Value.Id,
                row.DraftTeacher?.Value?.Id, checked((int)row.DraftPriority), row.DraftOneToOneIndex == 1, row.DraftNote));
        }, "通常授業の担当設定を保存しました");
    }

    private void CancelRegularLessonEditing()
    {
        var editing = _regularLessonRows.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _regularLessonRows.Remove(editing); else editing.IsEditing = false;
        ApplyRegularLessonFilter();
    }

    private void RegularLessonRow_PointerEntered(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, true);
    private void RegularLessonRow_PointerExited(object sender, PointerRoutedEventArgs e) => SetChangeButtonVisible((Grid)sender, false);

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
            var slotSummary = string.Join("・", day.EnabledTimeSlotIds.Select(id => _timeSlotItems.FirstOrDefault(x => x.Value?.Id == id)?.Value?.Code).Where(code => code is not null));
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
        foreach (var item in _timeSlotItems.Where(x => x.Value is { Active: true }))
        {
            var slot = item.Value!;
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
        var allSlotIds = _timeSlotItems.Where(x => x.Value is { Active: true }).Select(x => x.Value!.Id).ToArray();
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
        var allSlotIds = _timeSlotItems.Where(x => x.Value is { Active: true }).Select(x => x.Value!.Id).ToArray();
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
        var items = _timeSlotItems.Where(item => item.Value is not null).Select(item => item.Value!).ToList();
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
        {
            var studentValues=await App.MasterData.GetStudentsAsync(path);var teacherValues=await App.MasterData.GetTeachersAsync(path);var subjectValues=await App.MasterData.GetSubjectsAsync(path);
            var studentItems=studentValues.Select(x => new MasterItem<Student>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.FullName}　{x.Grade}",x.Active,"在籍中","卒業・無効")).ToArray();
            var teacherItems=teacherValues.Select(x => new MasterItem<Teacher>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.FullName}",x.Active,"在籍中","卒業・無効")).ToArray();
            var subjectItems=subjectValues.Select(x => new MasterItem<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
            _studentItems=studentItems;_teacherItems=teacherItems;_subjectItems=subjectItems;
            RebuildStudentRows(studentValues);RebuildTeacherRows(teacherValues);RebuildSubjectRows(subjectValues);
            var qualifications=await App.MasterData.GetQualificationsAsync(path);_qualifications=qualifications.ToDictionary(value=>(value.TeacherId,value.SubjectId));RenderQualificationMatrix();
            // ユーザー要望「生徒IDや講師IDは基本的に用いず、内部の処理にのみ使いたいので、ここでの
            // 表示は生徒氏名、講師氏名のみとしてください」への対応。IDは内部処理（保存・照合）だけに
            // 使い、一覧表示は氏名のみにする。
            _regularLessonStudentOptions=studentValues.Select(x=>new NamedOption<Student>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.ExternalId}　{x.FullName}　{x.Grade}")).ToArray();
            _regularLessonSubjectOptions=subjectValues.Select(x=>new NamedOption<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
            _regularLessonTeacherOptions=new NamedOption<Teacher?>[]{new(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new NamedOption<Teacher?>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.FullName}"))).ToArray();
            var regularLessons=await App.MasterData.GetRegularLessonsAsync(path);
            var previouslyEditingRegularLesson=_regularLessonRows.FirstOrDefault(r=>r.IsEditing&&r!=_regularLessonRowBeingSaved);
            _regularLessonRowBeingSaved=null;
            _regularLessonRows=regularLessons.Select(value=>RegularLessonRowViewModel.ForExisting(value,
                studentValues.Single(x=>x.Id==value.StudentId).FullName,
                subjectValues.Single(x=>x.Id==value.SubjectId).DisplayName,
                value.RegularTeacherId is long id?teacherValues.Single(x=>x.Id==id).FullName:"指定なし",
                _regularLessonStudentOptions,_regularLessonSubjectOptions,_regularLessonTeacherOptions)).ToList();
            if(previouslyEditingRegularLesson is{IsNew:true})
            {
                _regularLessonRows.Add(previouslyEditingRegularLesson);
            }
            else if(previouslyEditingRegularLesson is not null&&_regularLessonRows.FirstOrDefault(r=>r.Value!.Id==previouslyEditingRegularLesson.Value!.Id) is{} regularMatch)
            {
                regularMatch.IsEditing=true;
                regularMatch.CopyDraftFrom(previouslyEditingRegularLesson);
            }
            ApplyRegularLessonFilter();
            var slots = await App.CourseSettings.GetTimeSlotsAsync(path);
            var previouslyEditingSlot=_timeSlotItems.FirstOrDefault(r=>r.IsEditing&&r!=_timeSlotRowBeingSaved);
            _timeSlotRowBeingSaved=null;
            _timeSlotItems.Clear();
            foreach (var item in slots.OrderBy(x => x.SortOrder).Select(TimeSlotRowViewModel.ForExisting)) _timeSlotItems.Add(item);
            if(previouslyEditingSlot is{IsNew:true})
            {
                _timeSlotItems.Add(previouslyEditingSlot);
            }
            else if(previouslyEditingSlot is not null&&_timeSlotItems.FirstOrDefault(r=>r.Value!.Id==previouslyEditingSlot.Value!.Id) is{} slotMatch)
            {
                slotMatch.IsEditing=true;
                slotMatch.CopyDraftFrom(previouslyEditingSlot);
            }
            _courseDays = (await App.CourseSettings.GetCourseDaysAsync(path)).OrderBy(x => x.Date).ToArray();
            _selectedDates.IntersectWith(_courseDays.Select(x => x.Date));
            RenderCourseDayCalendar(); RenderCalendarSlotToggles(); UpdateCalendarSelectionCount();
        }
    }

    private void StudentSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyStudentFilter();
    private void TeacherSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyTeacherFilter();
    private void SubjectSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySubjectFilter();

    // ユーザー要望（checkpoint142）「生徒・講師・科目について、上側で入力させる形式ではないように
    // したい」への対応。フィルタは既に保持している行ビューモデル（_studentRows等）を絞り込むだけに
    // し、新しいインスタンスを作り直さない（作り直すと編集中の行のDraft値が検索のたびに消えて
    // しまう）。編集中の行は、検索語に一致しなくても一覧から消えないよう常に含める。
    private void ApplyStudentFilter() => Students.ItemsSource = _studentRows.Where(r => r.IsEditing || r.Matches(StudentSearch.Text)).ToArray();
    private void ApplyTeacherFilter() => Teachers.ItemsSource = _teacherRows.Where(r => r.IsEditing || r.Matches(TeacherSearch.Text)).ToArray();
    private void ApplySubjectFilter() => Subjects.ItemsSource = _subjectRows.Where(r => r.IsEditing || r.Matches(SubjectSearch.Text)).ToArray();

    // ReloadAsync後に呼ばれる。編集中の行があれば、同じレコードIDに対応する新しいインスタンスへ
    // Draft値を引き継ぐ（保存以外の操作、例えば他のタブでの保存によるReloadAsyncで、編集中の
    // 入力途中のテキストが消えないようにするため）。
    private void RebuildStudentRows(IReadOnlyList<Student> values)
    {
        var previouslyEditing = _studentRows.FirstOrDefault(r => r.IsEditing && r != _studentRowBeingSaved);
        _studentRowBeingSaved = null;
        _studentRows = values.Select(StudentRowViewModel.ForExisting).ToList();
        if (previouslyEditing is { IsNew: true })
        {
            _studentRows.Add(previouslyEditing); // 未保存の新規行はそのまま編集状態で引き継ぐ
        }
        else if (previouslyEditing is not null && _studentRows.FirstOrDefault(r => r.Value!.Id == previouslyEditing.Value!.Id) is { } match)
        {
            match.IsEditing = true;
            match.CopyDraftFrom(previouslyEditing);
        }
        ApplyStudentFilter();
    }

    private void RebuildTeacherRows(IReadOnlyList<Teacher> values)
    {
        var previouslyEditing = _teacherRows.FirstOrDefault(r => r.IsEditing && r != _teacherRowBeingSaved);
        _teacherRowBeingSaved = null;
        _teacherRows = values.Select(TeacherRowViewModel.ForExisting).ToList();
        if (previouslyEditing is { IsNew: true })
        {
            _teacherRows.Add(previouslyEditing);
        }
        else if (previouslyEditing is not null && _teacherRows.FirstOrDefault(r => r.Value!.Id == previouslyEditing.Value!.Id) is { } match)
        {
            match.IsEditing = true;
            match.CopyDraftFrom(previouslyEditing);
        }
        ApplyTeacherFilter();
    }

    private void RebuildSubjectRows(IReadOnlyList<Subject> values)
    {
        var previouslyEditing = _subjectRows.FirstOrDefault(r => r.IsEditing && r != _subjectRowBeingSaved);
        _subjectRowBeingSaved = null;
        _subjectRows = values.Select(SubjectRowViewModel.ForExisting).ToList();
        if (previouslyEditing is { IsNew: true })
        {
            _subjectRows.Add(previouslyEditing);
        }
        else if (previouslyEditing is not null && _subjectRows.FirstOrDefault(r => r.Value!.Id == previouslyEditing.Value!.Id) is { } match)
        {
            match.IsEditing = true;
            match.CopyDraftFrom(previouslyEditing);
        }
        ApplySubjectFilter();
    }

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

}
