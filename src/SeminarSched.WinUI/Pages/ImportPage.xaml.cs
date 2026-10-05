using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Application.Importing;
using SeminarSched.Domain.MasterData;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class ImportPage : WorkflowPageBase
{
    private CourseSurveyPreview? _surveyPreview;
    private bool _loaded;
    private string? _surveyStudentPath;
    private string? _surveyTeacherPath;
    private List<LessonRequestRowViewModel> _lessonRequestRows = [];
    // 保存に成功した行は、ReloadAsync直後のRebuildで「編集中だった行」として誤って再び編集状態へ
    // 戻してしまわないよう、保存中の行を記録しておく（SetupPage.xaml.csと同じ仕組み）。
    private LessonRequestRowViewModel? _lessonRequestRowBeingSaved;
    public ImportPage() => InitializeComponent();
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded=true;
        var ready=EnsureProject(ProjectRequired);
        SurveySelectStudentButton.IsEnabled=ready;SurveySelectTeacherButton.IsEnabled=ready;
        // ユーザー指摘（checkpoint122）「選択していないときに空白になるのは違和感がある」への対応。
        // ファイル未選択時も「未選択」のプレースホルダー文言を最初から表示し、空行にならないようにする。
        UpdateSurveySelectedFilesText();
        if(ready){await ReloadMatrixAsync();await ReloadLessonRequestsAsync();}
    }

    private async void SurveySelectStudent_Click(object sender,RoutedEventArgs e)
    {
        var path=await PickResponseAsync();if(path is null)return;
        _surveyStudentPath=path;_surveyPreview=null;SurveyApplyButton.IsEnabled=false;
        UpdateSurveySelectedFilesText();
    }
    private async void SurveySelectTeacher_Click(object sender,RoutedEventArgs e)
    {
        var path=await PickResponseAsync();if(path is null)return;
        _surveyTeacherPath=path;_surveyPreview=null;SurveyApplyButton.IsEnabled=false;
        UpdateSurveySelectedFilesText();
    }
    private void UpdateSurveySelectedFilesText()
    {
        SurveySelectedFilesText.Text=$"生徒の生回答: {(_surveyStudentPath is null?"未選択":Path.GetFileName(_surveyStudentPath))}　/　講師の生回答: {(_surveyTeacherPath is null?"未選択":Path.GetFileName(_surveyTeacherPath))}";
        SurveyVerifyButton.IsEnabled=_surveyStudentPath is not null&&_surveyTeacherPath is not null;
    }

    private async void SurveyVerify_Click(object sender,RoutedEventArgs e)
    {
        if(_surveyStudentPath is not{}student||_surveyTeacherPath is not{}teacher)return;
        try
        {
            IsEnabled=false;
            _surveyPreview=await App.CourseSurveyImport.PreviewAsync(App.ProjectService.Current!.Path,student,teacher);
            SurveyIssues.ItemsSource=_surveyPreview.Issues.Select(x=>$"{(x.Severity==CourseSurveyIssueSeverity.Error?"[エラー]":"[警告]")} {x.Source} 行{x.Row} {x.PersonName}: {x.Message}（{x.Resolution}）").ToArray();
            SurveyApplyButton.IsEnabled=!_surveyPreview.HasErrors;
            SurveySummary.Visibility=Visibility.Visible;
            SurveySummary.Text=$"生徒{_surveyPreview.StudentCount}件・講師{_surveyPreview.TeacherCount}件・受講希望{_surveyPreview.RequestCount}件（エラー{_surveyPreview.Issues.Count(x=>x.Severity==CourseSurveyIssueSeverity.Error)}件・警告{_surveyPreview.Issues.Count(x=>x.Severity==CourseSurveyIssueSeverity.Warning)}件）";
            Status.Severity=_surveyPreview.HasErrors?InfoBarSeverity.Error:InfoBarSeverity.Success;
            Status.Title=_surveyPreview.HasErrors?"検証エラーがあります":"検証成功";
            Status.Message="";
            Status.IsOpen=true;
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException){ShowError(ex.Message);}finally{IsEnabled=true;}
    }

    private async void SurveyApply_Click(object sender,RoutedEventArgs e)
    {
        if(_surveyPreview is null)return;
        try
        {
            IsEnabled=false;
            var result=await App.CourseSurveyImport.ApplyAsync(App.ProjectService.Current!.Path,_surveyPreview);
            // ユーザー報告「アンケート取込時にこの優先度などの引継ぎが行われていない...受講希望から
            // 生徒の優先度を見てみると変わっていない」の調査で発覚：反映処理自体は「通常授業担当設定」
            // の優先度を正しくLessonRequestへ書き込んでいた（DBは正しい）が、この画面の「受講希望」
            // 一覧（LessonRequests、SaveLessonRequest_Click等では保存後に呼んでいる
            // ReloadLessonRequestsAsync）をここでは呼んでいなかったため、反映直後に同じ画面上で
            // 見ると古い表示のまま（別画面へ移動して戻れば正しい値が見える）だった。
            await ReloadLessonRequestsAsync();
            Status.Severity=InfoBarSeverity.Success;
            Status.Title="アンケート回答を反映しました";
            Status.Message=$"生徒{result.Students}件（体験生{result.TrialStudents}件を新規登録）・講師{result.Teachers}件・受講希望{result.LessonRequests}件";
            Status.IsOpen=true;
            SurveyApplyButton.IsEnabled=false;
        }
        catch(Exception ex)when(ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(ex.Message);}finally{IsEnabled=true;}
    }
    private async Task<string?> PickResponseAsync(){var p=new FileOpenPicker{SuggestedStartLocation=PickerLocationId.DocumentsLibrary};p.FileTypeFilter.Add(".csv");p.FileTypeFilter.Add(".xlsx");InitializeWithWindow.Initialize(p,WindowNative.GetWindowHandle(App.MainWindow!));return (await p.PickSingleFileAsync())?.Path;}
    private void ShowError(string message){Status.Severity=InfoBarSeverity.Error;Status.Title="処理できませんでした";Status.Message=message;Status.IsOpen=true;}

    private IReadOnlyList<NamedOption<Student>> _studentOptions = [];
    private IReadOnlyList<NamedOption<Subject>> _subjectOptions = [];
    private IReadOnlyList<NamedOption<Teacher?>> _teacherOptions = [];

    private async Task ReloadLessonRequestsAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        var studentValues=await App.MasterData.GetStudentsAsync(path);var teacherValues=await App.MasterData.GetTeachersAsync(path);var subjectValues=await App.MasterData.GetSubjectsAsync(path);
        var studentName=(Student x)=>$"{TrialLabel(x.ExternalId)}{x.FullName}";
        _studentOptions=studentValues.Select(x=>new NamedOption<Student>(x,$"{(x.Active?"":"[卒業・無効] ")}{studentName(x)}　{x.Grade}")).ToArray();
        _subjectOptions=subjectValues.Select(x=>new NamedOption<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
        _teacherOptions=new NamedOption<Teacher?>[]{new(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new NamedOption<Teacher?>(x,$"{(x.Active?"":"[卒業・無効] ")}{x.FullName}"))).ToArray();

        var lessonRequests=await App.MasterData.GetLessonRequestsAsync(path);
        // ユーザー要望（checkpoint148）「通常担当講師、第一希望から第三希望は講師名を苗字のみ表示。
        // これにより列幅を短く設定できるはず」への対応。生徒氏名（StudentName、下記studentName）は
        // 引き続きフルネームのまま、担当講師系の列だけ苗字（FamilyName）のみにする。
        string TeacherName(long? id)=>id is long tid?teacherValues.Single(x=>x.Id==tid).FamilyName:"指定なし";
        var previouslyEditing=_lessonRequestRows.FirstOrDefault(r=>r.IsEditing&&r!=_lessonRequestRowBeingSaved);
        _lessonRequestRowBeingSaved=null;
        _lessonRequestRows=lessonRequests.Select(value=>
        {
            var s=studentValues.Single(x=>x.Id==value.StudentId);
            return LessonRequestRowViewModel.ForExisting(value,
                $"{studentName(s)}　{s.Grade}",
                subjectValues.Single(x=>x.Id==value.SubjectId).DisplayName,
                TeacherName(value.RegularTeacherId), TeacherName(value.PreferredTeacher1Id), TeacherName(value.PreferredTeacher2Id), TeacherName(value.PreferredTeacher3Id),
                s.DefaultMaxConsecutiveSlots, s.AllowGap,
                _studentOptions, _subjectOptions, _teacherOptions);
        }).ToList();
        if(previouslyEditing is{IsNew:true})
        {
            _lessonRequestRows.Add(previouslyEditing);
        }
        else if(previouslyEditing is not null&&_lessonRequestRows.FirstOrDefault(r=>r.Value!.Id==previouslyEditing.Value!.Id) is{} match)
        {
            match.IsEditing=true;
            match.CopyDraftFrom(previouslyEditing);
        }
        ApplyLessonRequestFilter();
    }

    private void ApplyLessonRequestFilter() => LessonRequests.ItemsSource = _lessonRequestRows.Where(r => r.IsEditing || r.Matches(LessonRequestSearch.Text)).ToArray();
    private void LessonRequestSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyLessonRequestFilter();

    // ユーザー要望（checkpoint111）「体験生の項目...アンケート取込でそれが見られるようにしておいて
    // ほしい」への対応。CourseSurveyImportServiceは体験生をExternalId="TRIAL-####"で登録するため
    // （InsertTrialStudent参照）、その命名規則をそのまま可視化に流用する。
    private static string TrialLabel(string externalId)=>externalId.StartsWith("TRIAL-",StringComparison.OrdinalIgnoreCase)?"[体験生] ":"";

    // ユーザー要望（checkpoint142）「アンケート取込後の一覧についても、設定の生徒、講師、科目と
    // 同様に追加できるようにする」への対応。生徒・講師・科目タブ（SetupPage.xaml.cs）と同じ
    // 行内編集（追加・変更・保存）パターン。LessonRequestは外部キーが多いため、選択肢
    // （StudentOptions等）を行オブジェクト自身に持たせる方式（ImportPageRowViewModels.cs参照）。
    private void AddLessonRequestRow_Click(object sender, RoutedEventArgs e)
    {
        CancelLessonRequestEditing();
        LessonRequestSearch.Text = "";
        var row = LessonRequestRowViewModel.CreateNew(_studentOptions, _subjectOptions, _teacherOptions);
        _lessonRequestRows.Add(row);
        ApplyLessonRequestFilter();
        LessonRequests.UpdateLayout();
        LessonRequests.ScrollIntoView(row);
    }

    private void LessonRequestRow_Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not LessonRequestRowViewModel row) return;
        CancelLessonRequestEditing();
        row.IsEditing = true;
        if (VisualTreeHelpers.FindAncestor<ListViewItem>((DependencyObject)sender) is { } container && VisualTreeHelpers.FindDescendant<ComboBox>(container) is { } studentBox)
            studentBox.Focus(FocusState.Programmatic);
    }

    private void LessonRequestStudentBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { DataContext: LessonRequestRowViewModel { IsNew: true, IsEditing: true } } box)
            box.Focus(FocusState.Programmatic);
    }

    private async void SaveLessonRequestRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not LessonRequestRowViewModel row) return;
        try
        {
            if (row.DraftStudent is null || row.DraftSubject is null) throw new ArgumentException("生徒と科目を選択してください。");
            var priority = checked((int)row.DraftRegularPriority);
            if (priority == 5 && row.DraftRegularTeacher?.Value is null) throw new ArgumentException("担当講師優先度5では通常担当講師の指定が必須です。");
            var maxOverride = row.DraftMaxConsecutiveOverride <= 0 ? (int?)null : checked((int)row.DraftMaxConsecutiveOverride);
            var gapOverride = row.DraftAllowGapOverrideIndex switch { 1 => true, 2 => false, _ => (bool?)null };
            _lessonRequestRowBeingSaved = row;
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.MasterData.SaveLessonRequestAsync(path, new LessonRequest(row.IsNew ? 0 : row.Value!.Id, row.DraftStudent.Value.Id, row.DraftSubject.Value.Id, checked((int)row.DraftRequiredSessions),
                row.DraftRegularTeacher?.Value?.Id, priority, row.DraftPreferred1?.Value?.Id, row.DraftPreferred2?.Value?.Id, row.DraftPreferred3?.Value?.Id,
                row.DraftOneToOneIndex == 1, maxOverride, gapOverride, row.DraftNote));
            await ReloadLessonRequestsAsync();
            ToastNotificationState.ShowSuccess("受講希望を保存しました");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or OverflowException)
        {
            ShowError(exception.Message);
        }
        finally { IsEnabled = true; }
    }

    private void CancelLessonRequestEditing()
    {
        var editing = _lessonRequestRows.FirstOrDefault(r => r.IsEditing);
        if (editing is null) return;
        if (editing.IsNew) _lessonRequestRows.Remove(editing); else editing.IsEditing = false;
        ApplyLessonRequestFilter();
    }

    private void LessonRequestRow_PointerEntered(object sender, PointerRoutedEventArgs e) => VisualTreeHelpers.SetNamedChildVisible((Grid)sender, "ChangeDeletePanel", true);
    private void LessonRequestRow_PointerExited(object sender, PointerRoutedEventArgs e) => VisualTreeHelpers.SetNamedChildVisible((Grid)sender, "ChangeDeletePanel", false);

    // ユーザー要望（checkpoint142）「受講希望一覧の『選択した行を削除』は、行にカーソルを合わせたら
    // 『変更』の隣に『削除』も表示」への対応（ユーザー確認済み）。削除は取り消せないため、
    // DeleteSlot_Click（SetupPage.xaml.cs）と同じ確認ダイアログを経由する。
    private async void LessonRequestRow_Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not LessonRequestRowViewModel { Value: not null } row) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "この受講希望を削除しますか？", PrimaryButtonText = "削除", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            IsEnabled = false;
            var path = App.ProjectService.Current?.Path ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.MasterData.DeleteLessonRequestAsync(path, row.Value.StudentId, row.Value.SubjectId);
            await ReloadLessonRequestsAsync();
            ToastNotificationState.ShowSuccess("受講希望を削除しました");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            ShowError(exception.Message);
        }
        finally { IsEnabled = true; }
    }

    // ユーザー要望（checkpoint111）「可用性の手動編集について、これをカレンダーで変更することは
    // できないか。...複数日付・複数コマへ一括適用というのは撤廃し、ここは一人一人入力していく形で。
    // 集団授業のカレンダーにあるようなカレンダーを置いておき、その日ごとにコマのチェックボックスの
    // ようなものを用意しておく。イメージでいうと、TimeTreeのように横長の長方形があって、
    // 『チェックボックス』『コマ名』の並び。参加可能だったら緑、参加不可だったら赤...保存ボタンは
    // なしで、即座に反映されるようにしてほしい」への対応。複数選択・一括適用のUIを廃止し、対象は
    // 常に1名だけ選び、講習期間内の全開講日をカレンダー表示、日ごとにコマの帯（チェックボックス＋
    // コマ名、緑=参加可能／赤=参加不可）を並べる。チェックボックスの切り替えのたびに即座にDBへ反映し、
    // 保存ボタンは置かない。
    private static readonly string[] AvailabilityWeekdayHeaders = ["日", "月", "火", "水", "木", "金", "土"];
    private static readonly SolidColorBrush AvailabilityAvailableBrush = new(Color.FromArgb(255, 210, 240, 210));
    private static readonly SolidColorBrush AvailabilityUnavailableBrush = new(Color.FromArgb(255, 246, 210, 210));

    private AvailabilityEntityKind CurrentMatrixKind => MatrixTeacherKind.IsChecked==true ? AvailabilityEntityKind.Teacher : AvailabilityEntityKind.Student;

    private async Task ReloadMatrixAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        var previousId=(MatrixEntity.SelectedItem as AvailabilityEntityOption)?.Id;
        var entities=await App.AvailabilityMatrix.GetEntitiesAsync(path,CurrentMatrixKind);
        MatrixEntity.ItemsSource=entities;
        MatrixEntity.SelectedItem=entities.Count==0?null:entities.FirstOrDefault(x=>x.Id==previousId)??entities[0];
        await RefreshAvailabilityCalendarAsync();
    }

    // MatrixStudentKindはXAMLでIsChecked="True"を指定しており、WinUIはこのプロパティ設定を
    // InitializeComponent実行中に同期的なCheckedイベントとして発火させる。その時点ではXAML中で
    // 後に宣言された兄弟コントロール（MatrixTeacherKindやMatrixEntity等）がまだnullのため、
    // Page_Loaded以前の呼び出しは無視する（OptimizationPage._isLoadedと同じ対策パターン）。
    private async void MatrixKind_Changed(object sender,RoutedEventArgs e){if(!_loaded)return;await ReloadMatrixAsync();}

    private async void MatrixEntity_SelectionChanged(object sender,SelectionChangedEventArgs e)=>await RefreshAvailabilityCalendarAsync();

    private async Task RefreshAvailabilityCalendarAsync()
    {
        AvailabilityCalendarWeekdayHeader.Children.Clear();AvailabilityCalendarWeekdayHeader.ColumnDefinitions.Clear();
        AvailabilityCalendarGrid.Children.Clear();AvailabilityCalendarGrid.RowDefinitions.Clear();AvailabilityCalendarGrid.ColumnDefinitions.Clear();
        var path=App.ProjectService.Current?.Path;
        if(path is null||MatrixEntity.SelectedItem is not AvailabilityEntityOption entity)
        {AvailabilityCalendarGrid.Children.Add(new TextBlock{Text="対象を選択してください。",Margin=new Thickness(4)});return;}

        var calendar=await App.AvailabilityMatrix.GetCalendarAsync(path,CurrentMatrixKind,entity.Id);
        if(calendar.Days.Count==0){AvailabilityCalendarGrid.Children.Add(new TextBlock{Text="開講日が設定されていません。",Margin=new Thickness(4)});return;}

        for(var i=0;i<AvailabilityWeekdayHeaders.Length;i++)AvailabilityCalendarWeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition());
        for(var i=0;i<AvailabilityWeekdayHeaders.Length;i++)
        {
            var text=new TextBlock{Text=AvailabilityWeekdayHeaders[i],FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(4)};
            Grid.SetColumn(text,i);AvailabilityCalendarWeekdayHeader.Children.Add(text);
        }

        for(var i=0;i<7;i++)AvailabilityCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var leading=(int)calendar.Days[0].Date.DayOfWeek;
        var rowCount=(int)Math.Ceiling((leading+calendar.Days.Count)/7.0);
        for(var i=0;i<rowCount;i++)AvailabilityCalendarGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        for(var i=0;i<calendar.Days.Count;i++)
        {
            var day=calendar.Days[i];
            var cellIndex=leading+i;
            var cell=BuildAvailabilityDayCell(entity.Id,day);
            Grid.SetRow(cell,cellIndex/7);Grid.SetColumn(cell,cellIndex%7);
            AvailabilityCalendarGrid.Children.Add(cell);
        }
    }

    private FrameworkElement BuildAvailabilityDayCell(long entityId,AvailabilityCalendarDay day)
    {
        var stack=new StackPanel{Spacing=2,MinHeight=90};
        stack.Children.Add(new TextBlock{Text=day.Date.ToString("M/d(ddd)",CultureInfo.GetCultureInfo("ja-JP")),FontWeight=FontWeights.SemiBold,FontSize=11});

        foreach(var slot in day.Slots)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};
            var checkBox=new CheckBox{IsChecked=slot.Level>0,MinWidth=0,Padding=new Thickness(0),Tag=new AvailabilitySlotTag(entityId,day.OpenDateId,slot.TimeSlotId)};
            checkBox.Checked+=AvailabilitySlotCheckBox_Changed;checkBox.Unchecked+=AvailabilitySlotCheckBox_Changed;
            row.Children.Add(checkBox);
            row.Children.Add(new TextBlock{Text=slot.Label,FontSize=10,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap});
            var bar=new Border{CornerRadius=new CornerRadius(3),Padding=new Thickness(4,2,4,2),Background=slot.Level>0?AvailabilityAvailableBrush:AvailabilityUnavailableBrush,Child=row};
            stack.Children.Add(bar);
        }

        return new Border{Padding=new Thickness(4),CornerRadius=new CornerRadius(4),BorderBrush=new SolidColorBrush(Color.FromArgb(255,220,226,234)),BorderThickness=new Thickness(1),Child=stack};
    }

    private async void AvailabilitySlotCheckBox_Changed(object sender,RoutedEventArgs e)
    {
        if(sender is not CheckBox{Tag:AvailabilitySlotTag tag} checkBox)return;
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        var level=checkBox.IsChecked==true?1:0;
        try{await App.AvailabilityMatrix.SetLevelAsync(path,CurrentMatrixKind,tag.EntityId,tag.OpenDateId,tag.TimeSlotId,level);}
        catch(Exception exception)when(exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(exception.Message);}
        // 保存ボタンは無く即座に反映する仕様のため、成功・失敗どちらでもDBの実際の値をそのまま
        // 表示へ反映し直す（失敗時はチェックを元へ戻す、隣接するコマの色ズレも同時に防ぐ）。
        await RefreshAvailabilityCalendarAsync();
    }

    private sealed record AvailabilitySlotTag(long EntityId, long OpenDateId, long TimeSlotId);
}
