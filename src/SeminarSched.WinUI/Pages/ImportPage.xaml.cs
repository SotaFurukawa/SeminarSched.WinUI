using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.Importing;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace SeminarSched_WinUI.Pages;
public sealed partial class ImportPage : WorkflowPageBase
{
    private ResponseImportPreview? _preview;
    public ImportPage() => InitializeComponent();
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        SelectButton.IsEnabled=EnsureProject(ProjectRequired);
        if(SelectButton.IsEnabled)await ReloadMatrixAsync();
    }
    private async void Select_Click(object sender,RoutedEventArgs e)
    {
        var student=await PickResponseAsync();if(student is null)return;var teacher=await PickResponseAsync();if(teacher is null)return;
        try
        {
            IsEnabled=false;
            _preview=await App.ResponseImport.PreviewAsync(App.ProjectService.Current!.Path,student,teacher);
            Issues.ItemsSource=_preview.Issues.Select(x=>$"{(x.IsError?"[エラー]":"[警告]")} 行{x.Row} [{x.Field}] {x.Message}").ToArray();
            ApplyButton.IsEnabled=_preview.CanApply;
            Status.Severity=_preview.CanApply?InfoBarSeverity.Success:InfoBarSeverity.Error;
            Status.Title=_preview.CanApply?$"検証成功（生徒{_preview.StudentRowCount}行・講師{_preview.TeacherRowCount}行）":"検証エラーがあります";
            Status.IsOpen=true;
            RenderDiff(_preview.Diff);
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException){ShowError(ex.Message);}finally{IsEnabled=true;}
    }

    private void RenderDiff(SeminarSched.Application.Importing.ResponseImportDiff diff)
    {
        DiffSummary.Visibility=Visibility.Visible;
        DiffSummary.Text=$"生徒: 追加{diff.StudentAdded}件・変更{diff.StudentChanged}件・変更なし{diff.StudentUnchanged}件 / 講師: 追加{diff.TeacherAdded}件・変更{diff.TeacherChanged}件・変更なし{diff.TeacherUnchanged}件";
        RemovalPanel.Visibility=diff.HasRemovalCandidates?Visibility.Visible:Visibility.Collapsed;
        RemoveUnlisted.IsChecked=false;
        RemovalList.ItemsSource=diff.StudentRemovalCandidates.Select(x=>$"生徒 {x}").Concat(diff.TeacherRemovalCandidates.Select(x=>$"講師 {x}")).ToArray();
    }

    private async void Apply_Click(object sender,RoutedEventArgs e){if(_preview is null)return;try{IsEnabled=false;await App.ResponseImport.ApplyAsync(App.ProjectService.Current!.Path,_preview,RemoveUnlisted.IsChecked==true);Status.Severity=InfoBarSeverity.Success;Status.Title="回答を反映しました";Status.IsOpen=true;ApplyButton.IsEnabled=false;}catch(Exception ex)when(ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){ShowError(ex.Message);}finally{IsEnabled=true;}}
    private async Task<string?> PickResponseAsync(){var p=new FileOpenPicker{SuggestedStartLocation=PickerLocationId.DocumentsLibrary};p.FileTypeFilter.Add(".csv");p.FileTypeFilter.Add(".xlsx");InitializeWithWindow.Initialize(p,WindowNative.GetWindowHandle(App.MainWindow!));return (await p.PickSingleFileAsync())?.Path;}
    private void ShowError(string message){Status.Severity=InfoBarSeverity.Error;Status.Title="処理できませんでした";Status.Message=message;Status.IsOpen=true;}

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

    private async void MatrixKind_Changed(object sender,RoutedEventArgs e)=>await ReloadMatrixAsync();

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
