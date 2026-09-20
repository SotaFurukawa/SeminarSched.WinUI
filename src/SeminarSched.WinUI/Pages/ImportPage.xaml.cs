using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.Importing;
using SeminarSched.Domain.MasterData;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class ImportPage : WorkflowPageBase
{
    private CourseSurveyPreview? _surveyPreview;
    private bool _loaded;
    private string? _surveyStudentPath;
    private string? _surveyTeacherPath;
    private MasterItem<Student>[] _studentItems=[];
    private MasterItem<Subject>[] _subjectItems=[];
    private MasterItem<Teacher?>[] _nullableTeacherItems=[];
    public ImportPage() => InitializeComponent();
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded=true;
        var ready=EnsureProject(ProjectRequired);
        SurveySelectStudentButton.IsEnabled=ready;SurveySelectTeacherButton.IsEnabled=ready;
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

    private async Task ReloadLessonRequestsAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        var studentValues=await App.MasterData.GetStudentsAsync(path);var teacherValues=await App.MasterData.GetTeachersAsync(path);var subjectValues=await App.MasterData.GetSubjectsAsync(path);
        var studentItems=studentValues.Select(x=>new MasterItem<Student>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}　{x.Grade}")).ToArray();
        var subjectItems=subjectValues.Select(x=>new MasterItem<Subject>(x,$"{(x.Active?"":"[停止] ")}{x.SortOrder}　{x.Code}　{x.DisplayName}（{x.ShortName}）　{x.SchoolLevel}")).ToArray();
        _studentItems=studentItems;_subjectItems=subjectItems;
        _nullableTeacherItems=new[]{new MasterItem<Teacher?>(null,"（指定なし）")}.Concat(teacherValues.Select(x=>new MasterItem<Teacher?>(x,$"{(x.Active?"":"[停止] ")}{x.ExternalId}　{x.Name}"))).ToArray();
        RequestStudent.ItemsSource=studentItems;RequestSubject.ItemsSource=subjectItems;
        RequestRegularTeacher.ItemsSource=_nullableTeacherItems;RequestPreferred1.ItemsSource=_nullableTeacherItems;RequestPreferred2.ItemsSource=_nullableTeacherItems;RequestPreferred3.ItemsSource=_nullableTeacherItems;
        if(RequestRegularTeacher.SelectedIndex<0)RequestRegularTeacher.SelectedIndex=0;if(RequestPreferred1.SelectedIndex<0)RequestPreferred1.SelectedIndex=0;if(RequestPreferred2.SelectedIndex<0)RequestPreferred2.SelectedIndex=0;if(RequestPreferred3.SelectedIndex<0)RequestPreferred3.SelectedIndex=0;
        var lessonRequests=await App.MasterData.GetLessonRequestsAsync(path);
        LessonRequests.ItemsSource=lessonRequests.Select(value=>new LessonRequestRow(value,
            studentValues.Single(x=>x.Id==value.StudentId).Name,
            subjectValues.Single(x=>x.Id==value.SubjectId).DisplayName,
            value.RegularTeacherId is long rid?teacherValues.Single(x=>x.Id==rid).Name:"指定なし")).ToArray();
    }

    private async void SaveLessonRequest_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(RequestStudent.SelectedItem is not MasterItem<Student> student||RequestSubject.SelectedItem is not MasterItem<Subject> subject)throw new ArgumentException("生徒と科目を選択してください。");
            var regularTeacher=(RequestRegularTeacher.SelectedItem as MasterItem<Teacher?>)?.Value;
            var priority=checked((int)RequestRegularPriority.Value);
            if(priority==5&&regularTeacher is null)throw new ArgumentException("担当講師優先度5では通常担当講師の指定が必須です。");
            var preferred1=(RequestPreferred1.SelectedItem as MasterItem<Teacher?>)?.Value;
            var preferred2=(RequestPreferred2.SelectedItem as MasterItem<Teacher?>)?.Value;
            var preferred3=(RequestPreferred3.SelectedItem as MasterItem<Teacher?>)?.Value;
            var maxOverride=RequestMaxConsecutiveOverride.Value<=0?(int?)null:checked((int)RequestMaxConsecutiveOverride.Value);
            var gapOverride=RequestAllowGapOverride.SelectedIndex switch{1=>true,2=>false,_=>(bool?)null};
            IsEnabled=false;
            var path=App.ProjectService.Current?.Path??throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.MasterData.SaveLessonRequestAsync(path,new LessonRequest(0,student.Value.Id,subject.Value.Id,checked((int)RequestRequiredSessions.Value),
                regularTeacher?.Id,priority,preferred1?.Id,preferred2?.Id,preferred3?.Id,RequestOneToOne.IsChecked==true,maxOverride,gapOverride,RequestNote.Text));
            ResetLessonRequest();
            await ReloadLessonRequestsAsync();
            Status.Severity=InfoBarSeverity.Success;Status.Title="受講希望を保存しました";Status.Message="";Status.IsOpen=true;
        }
        catch(Exception exception)when(exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or OverflowException)
        {
            ShowError(exception.Message);
        }
        finally{IsEnabled=true;}
    }

    private void NewLessonRequest_Click(object sender,RoutedEventArgs e)=>ResetLessonRequest();

    // ユーザー指示: 第1希望講師は普通、通常担当講師と同じになるため、通常担当講師を選ぶと
    // 第1希望講師が未設定（指定なし）のままであれば自動的に同じ講師を初期値として補う。
    // 第1希望講師をすでに選んでいる場合（既存データの編集時含む）は上書きしない。
    private void RequestRegularTeacher_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(RequestPreferred1.SelectedItem is MasterItem<Teacher?> current && current.Value is not null)return;
        RequestPreferred1.SelectedItem=RequestRegularTeacher.SelectedItem;
    }

    private void LessonRequests_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(LessonRequests.SelectedItem is not LessonRequestRow selected)return;
        var value=selected.Value;
        RequestStudent.SelectedItem=_studentItems.FirstOrDefault(item=>item.Value.Id==value.StudentId);
        RequestSubject.SelectedItem=_subjectItems.FirstOrDefault(item=>item.Value.Id==value.SubjectId);
        RequestRequiredSessions.Value=value.RequiredSessions;
        RequestRegularTeacher.SelectedItem=_nullableTeacherItems.FirstOrDefault(item=>item.Value?.Id==value.RegularTeacherId);
        RequestRegularPriority.Value=value.RegularTeacherPriority;
        RequestPreferred1.SelectedItem=_nullableTeacherItems.FirstOrDefault(item=>item.Value?.Id==value.PreferredTeacher1Id);
        RequestPreferred2.SelectedItem=_nullableTeacherItems.FirstOrDefault(item=>item.Value?.Id==value.PreferredTeacher2Id);
        RequestPreferred3.SelectedItem=_nullableTeacherItems.FirstOrDefault(item=>item.Value?.Id==value.PreferredTeacher3Id);
        RequestOneToOne.IsChecked=value.OneToOneRequired;
        RequestMaxConsecutiveOverride.Value=value.MaxConsecutiveSlotsOverride??0;
        RequestAllowGapOverride.SelectedIndex=value.AllowGapOverride switch{true=>1,false=>2,_=>0};
        RequestNote.Text=value.Note;
    }

    private async void DeleteLessonRequest_Click(object sender,RoutedEventArgs e)
    {
        if(LessonRequests.SelectedItem is not LessonRequestRow selected){Show(InfoBarSeverity.Warning,"一覧から削除する行を選択してください");return;}
        try
        {
            IsEnabled=false;
            var path=App.ProjectService.Current?.Path??throw new InvalidOperationException("プロジェクトが開かれていません。");
            await App.MasterData.DeleteLessonRequestAsync(path,selected.Value.StudentId,selected.Value.SubjectId);
            ResetLessonRequest();
            await ReloadLessonRequestsAsync();
            Status.Severity=InfoBarSeverity.Success;Status.Title="受講希望を削除しました";Status.Message="";Status.IsOpen=true;
        }
        catch(Exception exception)when(exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            ShowError(exception.Message);
        }
        finally{IsEnabled=true;}
    }

    private void ResetLessonRequest()
    {
        LessonRequests.SelectedItem=null;
        RequestStudent.SelectedItem=null;RequestSubject.SelectedItem=null;RequestRequiredSessions.Value=1;
        RequestRegularTeacher.SelectedIndex=0;RequestRegularPriority.Value=3;
        RequestPreferred1.SelectedIndex=0;RequestPreferred2.SelectedIndex=0;RequestPreferred3.SelectedIndex=0;
        RequestOneToOne.IsChecked=false;RequestMaxConsecutiveOverride.Value=0;RequestAllowGapOverride.SelectedIndex=0;RequestNote.Text="";
    }

    private void Show(InfoBarSeverity severity,string title){Status.Severity=severity;Status.Title=title;Status.Message="";Status.IsOpen=true;}

    private sealed record MasterItem<T>(T Value,string Display){public override string ToString()=>Display;}

    private sealed record LessonRequestRow(LessonRequest Value,string StudentName,string SubjectName,string RegularTeacherName)
    {
        public string OneToOneText=>Value.OneToOneRequired?"○":"";
        public override string ToString()=>$"{StudentName}　{SubjectName}　必要{Value.RequiredSessions}回　通常担当:{RegularTeacherName}　優先度{Value.RegularTeacherPriority}　{(Value.OneToOneRequired?"1対1":"通常")}";
    }

    private AvailabilityEntityKind CurrentMatrixKind => MatrixTeacherKind.IsChecked==true ? AvailabilityEntityKind.Teacher : AvailabilityEntityKind.Student;

    private async Task ReloadMatrixAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        MatrixEntities.ItemsSource=await App.AvailabilityMatrix.GetEntitiesAsync(path,CurrentMatrixKind);
        var previousDate=(MatrixDate.SelectedItem as AvailabilityDateOption)?.OpenDateId;
        var dates=await App.AvailabilityMatrix.GetOpenDatesAsync(path);
        MatrixDate.ItemsSource=dates;
        MatrixDate.SelectedItem=dates.Count==0?null:dates.FirstOrDefault(d=>d.OpenDateId==previousDate)??dates[0];
        BulkMatrixDates.ItemsSource=dates;
        var slots=await App.CourseSettings.GetTimeSlotsAsync(path);
        BulkMatrixSlots.ItemsSource=slots.Where(x=>x.Active).OrderBy(x=>x.SortOrder).Select(x=>new AvailabilitySlotOption(x.Id,$"{x.DisplayName} {x.StartTime:HH\\:mm}～{x.EndTime:HH\\:mm}")).ToArray();
    }

    // MatrixStudentKindはXAMLでIsChecked="True"を指定しており、WinUIはこのプロパティ設定を
    // InitializeComponent実行中に同期的なCheckedイベントとして発火させる。その時点ではXAML中で
    // 後に宣言された兄弟コントロール（MatrixTeacherKindやMatrixEntities等）がまだnullのため、
    // Page_Loaded以前の呼び出しは無視する（OptimizationPage._isLoadedと同じ対策パターン）。
    private async void MatrixKind_Changed(object sender,RoutedEventArgs e){if(!_loaded)return;await ReloadMatrixAsync();}

    private async void MatrixDate_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        var path=App.ProjectService.Current?.Path;
        if(path is null||MatrixDate.SelectedItem is not AvailabilityDateOption date){MatrixSlot.ItemsSource=null;RenderMatrixGrid([]);return;}
        var previousSlot=(MatrixSlot.SelectedItem as AvailabilitySlotOption)?.TimeSlotId;
        var slots=await App.AvailabilityMatrix.GetSlotsForDateAsync(path,date.OpenDateId);
        MatrixSlot.ItemsSource=slots;
        MatrixSlot.SelectedItem=slots.Count==0?null:slots.FirstOrDefault(s=>s.TimeSlotId==previousSlot)??slots[0];
        await RefreshMatrixGridAsync();
    }

    private async void MatrixEntities_SelectionChanged(object sender,SelectionChangedEventArgs e)=>await RefreshMatrixGridAsync();

    private async Task RefreshMatrixGridAsync()
    {
        var path=App.ProjectService.Current?.Path;
        var selected=MatrixEntities.SelectedItems.Cast<AvailabilityEntityOption>().ToArray();
        var slots=(MatrixSlot.ItemsSource as IReadOnlyList<AvailabilitySlotOption>)??[];
        if(path is null||MatrixDate.SelectedItem is not AvailabilityDateOption date||selected.Length==0){RenderMatrixGrid(slots);return;}
        var rows=await App.AvailabilityMatrix.GetDayMatrixAsync(path,CurrentMatrixKind,date.OpenDateId,selected.Select(s=>s.Id).ToArray());
        RenderMatrixGrid(slots,rows);
    }

    private void RenderMatrixGrid(IReadOnlyList<AvailabilitySlotOption> slots,IReadOnlyList<AvailabilityMatrixRow>? rows=null)
    {
        rows??=[];
        MatrixGrid.Children.Clear();MatrixGrid.RowDefinitions.Clear();MatrixGrid.ColumnDefinitions.Clear();
        if(rows.Count==0||slots.Count==0){MatrixGrid.Children.Add(new TextBlock{Text="対象と日付を選択してください。",Margin=new Thickness(4)});return;}

        MatrixGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        foreach(var _ in rows)MatrixGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        foreach(var _ in slots)MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});

        void Place(FrameworkElement element,int row,int column){Grid.SetRow(element,row);Grid.SetColumn(element,column);MatrixGrid.Children.Add(element);}
        Place(new TextBlock(),0,0);
        for(var c=0;c<slots.Count;c++)Place(new TextBlock{Text=slots[c].Label,FontWeight=FontWeights.SemiBold,Margin=new Thickness(4),TextWrapping=TextWrapping.Wrap},0,c+1);
        for(var r=0;r<rows.Count;r++)
        {
            Place(new TextBlock{Text=rows[r].Label,Margin=new Thickness(4),VerticalAlignment=VerticalAlignment.Center},r+1,0);
            for(var c=0;c<slots.Count;c++)
            {
                var level=rows[r].LevelsBySlot.TryGetValue(slots[c].TimeSlotId,out var value)?value:1;
                Place(new TextBlock{Text=level.ToString(CultureInfo.InvariantCulture),Margin=new Thickness(4),HorizontalAlignment=HorizontalAlignment.Center},r+1,c+1);
            }
        }
    }

    private async void ApplyMatrix_Click(object sender,RoutedEventArgs e)
    {
        var path=App.ProjectService.Current?.Path;
        var selected=MatrixEntities.SelectedItems.Cast<AvailabilityEntityOption>().Select(x=>x.Id).ToArray();
        if(path is null||MatrixDate.SelectedItem is not AvailabilityDateOption date||MatrixSlot.SelectedItem is not AvailabilitySlotOption slot||MatrixLevel.SelectedIndex<0)
        {ShowError("対象・日付・コマ・値をすべて選択してください。");return;}
        if(selected.Length==0){ShowError("対象を1件以上選択してください。");return;}
        try
        {
            IsEnabled=false;
            await App.AvailabilityMatrix.SetLevelAsync(path,CurrentMatrixKind,selected,date.OpenDateId,slot.TimeSlotId,MatrixLevel.SelectedIndex);
            await RefreshMatrixGridAsync();
            Status.Severity=InfoBarSeverity.Success;Status.Title="可用性を更新しました";Status.Message="";Status.IsOpen=true;
        }
        catch(Exception exception)when(exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(exception.Message);}
        finally{IsEnabled=true;}
    }

    private async void ApplyBulkMatrix_Click(object sender,RoutedEventArgs e)
    {
        var path=App.ProjectService.Current?.Path;
        var selectedEntities=MatrixEntities.SelectedItems.Cast<AvailabilityEntityOption>().Select(x=>x.Id).ToArray();
        var selectedDates=BulkMatrixDates.SelectedItems.Cast<AvailabilityDateOption>().ToArray();
        var selectedSlots=BulkMatrixSlots.SelectedItems.Cast<AvailabilitySlotOption>().ToArray();
        if(path is null||BulkMatrixLevel.SelectedIndex<0){ShowError("値を選択してください。");return;}
        if(selectedEntities.Length==0){ShowError("対象を1件以上選択してください。");return;}
        if(selectedDates.Length==0||selectedSlots.Length==0){ShowError("日付とコマをそれぞれ1件以上選択してください。");return;}
        var pairs=selectedDates.SelectMany(d=>selectedSlots.Select(s=>(d.OpenDateId,s.TimeSlotId))).ToArray();
        try
        {
            IsEnabled=false;
            await App.AvailabilityMatrix.SetLevelsAsync(path,CurrentMatrixKind,selectedEntities,pairs,BulkMatrixLevel.SelectedIndex);
            await RefreshMatrixGridAsync();
            Status.Severity=InfoBarSeverity.Success;Status.Title="可用性を一括更新しました";Status.Message=$"{selectedEntities.Length}件×{selectedDates.Length}日×{selectedSlots.Length}コマへ適用しました";Status.IsOpen=true;
        }
        catch(Exception exception)when(exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(exception.Message);}
        finally{IsEnabled=true;}
    }
}
