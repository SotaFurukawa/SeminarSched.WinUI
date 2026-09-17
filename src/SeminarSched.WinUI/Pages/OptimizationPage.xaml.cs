using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Application.Settings;
using SeminarSched_WinUI.ViewModels;
using SeminarSched.Application.Scheduling;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace SeminarSched_WinUI.Pages;

public sealed partial class OptimizationPage : Page
{
    private CancellationTokenSource? _saveDebounce;
    private bool _isLoaded;
    private readonly HashSet<long> _extraTeacherIds = [];
    private readonly Stack<ScheduleSnapshot> _undoStack = new();
    private readonly Stack<ScheduleSnapshot> _redoStack = new();
    private ScheduleBoard? _currentBoard;
    private long? _selectedDateId;
    private sealed record CellTag(long TimeSlotId, long TeacherId, bool Blocked);

    public OptimizationPage()
    {
        InitializeComponent();
    }

    public OptimizationQualityViewModel ViewModel { get; } = new();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await App.SettingsStore.LoadAsync();
            ViewModel.Select((int)settings.OptimizationQualityLevel);
            QualitySlider.Value = ViewModel.SliderValue;
            _isLoaded = true;
            RunButton.IsEnabled = App.ProjectService.Current is not null;
            _undoStack.Clear(); _redoStack.Clear(); UpdateUndoRedoButtons();
            if (RunButton.IsEnabled) await ReloadEditorAsync();
        }
        catch (IOException)
        {
            SaveErrorInfoBar.IsOpen = true;
            _isLoaded = true;
        }
        catch (UnauthorizedAccessException)
        {
            SaveErrorInfoBar.IsOpen = true;
            _isLoaded = true;
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var path=App.ProjectService.Current!.Path;
        try
        {
            RunButton.IsEnabled=false;RunProgress.IsActive=true;RunStatus.IsOpen=false;
            PushUndoSnapshot(await App.ScheduleEditor.CaptureSnapshotAsync(path));
            var profile=SeminarSched.Optimization.Profiles.OptimizationProfileCatalog.Get(ViewModel.Level);
            var result=await App.ScheduleRun.RunAsync(path,profile.MaximumDuration);
            RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title="時間割を作成しました";RunStatus.Message=$"配置 {result.PlacedLessons}件、未配置 {result.UnassignedLessons}件、{result.Elapsed.TotalSeconds:F1}秒";RunStatus.IsOpen=true;
            await ReloadEditorAsync();
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if(_undoStack.Count>0)_undoStack.Pop();UpdateUndoRedoButtons();
            RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を作成できませんでした";RunStatus.Message=ex.Message;RunStatus.IsOpen=true;
        }
        finally{RunProgress.IsActive=false;RunButton.IsEnabled=App.ProjectService.Current is not null;}
    }

    private void PushUndoSnapshot(ScheduleSnapshot snapshot)
    {
        _undoStack.Push(snapshot);
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0 || App.ProjectService.Current?.Path is not { } path) return;
        try
        {
            IsEnabled = false;
            var current = await App.ScheduleEditor.CaptureSnapshotAsync(path);
            var previous = _undoStack.Pop();
            _redoStack.Push(current);
            await App.ScheduleEditor.RestoreSnapshotAsync(path, previous);
            await ReloadEditorAsync();
            UpdateUndoRedoButtons();
            RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "元に戻しました"; RunStatus.Message = ""; RunStatus.IsOpen = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        { ShowEditorError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0 || App.ProjectService.Current?.Path is not { } path) return;
        try
        {
            IsEnabled = false;
            var current = await App.ScheduleEditor.CaptureSnapshotAsync(path);
            var next = _redoStack.Pop();
            _undoStack.Push(current);
            await App.ScheduleEditor.RestoreSnapshotAsync(path, next);
            await ReloadEditorAsync();
            UpdateUndoRedoButtons();
            RunStatus.Severity = InfoBarSeverity.Success; RunStatus.Title = "やり直しました"; RunStatus.Message = ""; RunStatus.IsOpen = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        { ShowEditorError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private async Task ReloadEditorAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        ManualRequest.ItemsSource=await App.FixedLessons.GetRequestsAsync(path);ManualTeacher.ItemsSource=await App.FixedLessons.GetTeachersAsync(path);ManualSlot.ItemsSource=await App.FixedLessons.GetSlotsAsync(path);Assignments.ItemsSource=await App.ScheduleEditor.GetAssignmentsAsync(path);
        await ReloadBoardDatesAsync(path);
        await ReloadBoardAsync();
    }

    private async Task ReloadBoardDatesAsync(string path)
    {
        var previous = (BoardDate.SelectedItem as OpenDateOption)?.Id;
        var dates = await App.ScheduleEditor.GetOpenDatesAsync(path);
        BoardDate.ItemsSource = dates;
        BoardDate.SelectedItem = dates.Count == 0 ? null : dates.FirstOrDefault(d => d.Id == previous) ?? dates[0];
    }

    private async void BoardDate_SelectionChanged(object sender, SelectionChangedEventArgs e) => await ReloadBoardAsync();

    private async Task ReloadBoardAsync()
    {
        var path = App.ProjectService.Current?.Path;
        if (path is null || BoardDate.SelectedItem is not OpenDateOption date)
        {
            _currentBoard = null; _selectedDateId = null; UnplacedList.ItemsSource = null; RenderBoard();
            return;
        }
        _selectedDateId = date.Id;
        _currentBoard = await App.ScheduleEditor.GetBoardAsync(path, date.Id, _extraTeacherIds);
        UnplacedList.ItemsSource = await App.ScheduleEditor.GetUnplacedSessionsAsync(path);
        RenderBoard();
    }

    private void BoardSearch_TextChanged(object sender, TextChangedEventArgs e) => RenderBoard();

    private void RenderBoard()
    {
        BoardGrid.Children.Clear();
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();
        if (_currentBoard is not { } board || board.Slots.Count == 0)
        {
            BoardGrid.Children.Add(new TextBlock { Text = "この日は開講コマがありません。", Margin = new Thickness(8) });
            return;
        }
        var search = BoardSearch.Text?.Trim() ?? "";

        BoardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in board.Slots) BoardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in board.Teachers) BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        void Place(FrameworkElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            BoardGrid.Children.Add(element);
        }

        Place(new TextBlock(), 0, 0);
        for (var c = 0; c < board.Teachers.Count; c++)
            Place(new TextBlock { Text = board.Teachers[c].Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap }, 0, c + 1);

        for (var r = 0; r < board.Slots.Count; r++)
        {
            var slot = board.Slots[r];
            Place(new TextBlock { Text = slot.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
            for (var c = 0; c < board.Teachers.Count; c++)
            {
                var teacher = board.Teachers[c];
                Place(CreateCell(slot.TimeSlotId, teacher.TeacherId, board.Cell(slot.TimeSlotId, teacher.TeacherId), search), r + 1, c + 1);
            }
        }
    }

    private Border CreateCell(long timeSlotId, long teacherId, BoardCell? cell, string search)
    {
        var blocked = cell?.Blocked ?? false;
        var content = new StackPanel { Spacing = 2 };
        var toggle = new Button { Content = blocked ? "○" : "×", FontSize = 10, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right, Tag = new CellTag(timeSlotId, teacherId, blocked) };
        toggle.Click += CellToggle_Click;
        content.Children.Add(toggle);
        foreach (var card in cell?.Cards ?? [])
            content.Children.Add(CreateCard(card, search));

        var border = new Border
        {
            Padding = new Thickness(4),
            MinHeight = 56,
            Background = blocked ? ResourceBrush("ControlFillColorDisabledBrush", Color.FromArgb(255, 232, 232, 232)) : ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 250, 250, 250)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(255, 210, 210, 210)),
            BorderThickness = new Thickness(1),
            AllowDrop = !blocked,
            Tag = new CellTag(timeSlotId, teacherId, blocked),
            Child = content,
        };
        border.DragOver += Cell_DragOver;
        border.Drop += Cell_Drop;
        return border;
    }

    private Border CreateCard(BoardCard card, string search)
    {
        var highlighted = !string.IsNullOrEmpty(search) && card.StudentLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        var border = new Border
        {
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 2, 0, 0),
            CornerRadius = new CornerRadius(4),
            Background = ResourceBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(255, 235, 235, 235)),
            BorderThickness = new Thickness(highlighted ? 2 : 0),
            BorderBrush = highlighted ? new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]) : null,
            CanDrag = !card.IsLocked,
            Child = new TextBlock { Text = card.ToString(), FontSize = 11, TextWrapping = TextWrapping.Wrap },
        };
        border.DragStarting += (_, args) => { args.Data.SetText($"assignment:{card.AssignmentId}"); args.Data.RequestedOperation = DataPackageOperation.Move; };
        var flyout = new MenuFlyout();
        var lockItem = new MenuFlyoutItem { Text = card.IsLocked ? "ロックを解除" : "ロックする" };
        lockItem.Click += async (_, _) => await ExecuteEditorAsync(() => App.ScheduleEditor.SetLockedAsync(App.ProjectService.Current!.Path, card.AssignmentId, !card.IsLocked), card.IsLocked ? "ロックを解除しました" : "ロックしました");
        flyout.Items.Add(lockItem);
        if (card.IsManual)
        {
            var removeItem = new MenuFlyoutItem { Text = "手動配置を削除" };
            removeItem.Click += async (_, _) => await ExecuteEditorAsync(() => App.ScheduleEditor.RemoveManualAsync(App.ProjectService.Current!.Path, card.AssignmentId), "手動配置を削除しました");
            flyout.Items.Add(removeItem);
        }
        border.ContextFlyout = flyout;
        return border;
    }

    private static Brush ResourceBrush(string key, Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(fallback);

    private async void CellToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CellTag cell } || App.ProjectService.Current?.Path is not { } path || _selectedDateId is not { } dateId) return;
        await ExecuteEditorAsync(() => App.ScheduleEditor.SetTeacherUnavailableAsync(path, cell.TeacherId, dateId, cell.TimeSlotId, !cell.Blocked), cell.Blocked ? "出勤可能にしました" : "出勤不可にしました");
    }

    private void Cell_DragOver(object sender, DragEventArgs e)
        => e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.Text) ? DataPackageOperation.Move : DataPackageOperation.None;

    private async void Cell_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (sender is not FrameworkElement { Tag: CellTag cell } || cell.Blocked) return;
            if (!e.DataView.Contains(StandardDataFormats.Text)) return;
            if (App.ProjectService.Current?.Path is not { } path || _selectedDateId is not { } dateId) return;
            var text = await e.DataView.GetTextAsync();
            if (text.StartsWith("assignment:", StringComparison.Ordinal))
            {
                var id = long.Parse(text.AsSpan("assignment:".Length), CultureInfo.InvariantCulture);
                await ExecuteEditorAsync(() => App.ScheduleEditor.MoveAsync(path, id, cell.TeacherId, dateId, cell.TimeSlotId), "配置を移動しました");
            }
            else if (text.StartsWith("request:", StringComparison.Ordinal))
            {
                var id = long.Parse(text.AsSpan("request:".Length), CultureInfo.InvariantCulture);
                await ExecuteEditorAsync(() => App.ScheduleEditor.AddManualAsync(path, id, cell.TeacherId, dateId, cell.TimeSlotId, false), "手動配置を追加しました");
            }
        }
        finally { deferral.Complete(); }
    }

    private void UnplacedList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is UnplacedSessionOption option)
        {
            e.Data.SetText($"request:{option.LessonRequestId}");
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private async void AddTeacher_Click(object sender, RoutedEventArgs e)
    {
        if (App.ProjectService.Current?.Path is not { } path) return;
        var all = await App.FixedLessons.GetTeachersAsync(path);
        var shown = (_currentBoard?.Teachers ?? Array.Empty<BoardTeacherColumn>()).Select(t => t.TeacherId).ToHashSet();
        var hidden = all.Where(t => !shown.Contains(t.Id)).ToList();
        if (hidden.Count == 0) { ShowEditorError("表示できる講師がありません。"); return; }
        var list = new ListView { ItemsSource = hidden, DisplayMemberPath = nameof(TeacherOption.Label), SelectionMode = ListViewSelectionMode.Single };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "講師を表示", Content = list, PrimaryButtonText = "表示", CloseButtonText = "キャンセル", IsPrimaryButtonEnabled = false };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is TeacherOption picked)
        {
            _extraTeacherIds.Add(picked.Id);
            await ReloadBoardAsync();
        }
    }

    private async void AddManual_Click(object sender,RoutedEventArgs e)
    {
        if(ManualRequest.SelectedItem is not LessonRequestOption request||ManualTeacher.SelectedItem is not TeacherOption teacher||ManualSlot.SelectedItem is not ScheduleSlotOption slot){ShowEditorError("受講希望・講師・日時を選択してください。");return;}
        await ExecuteEditorAsync(async()=>await App.ScheduleEditor.AddManualAsync(App.ProjectService.Current!.Path,request.Id,teacher.Id,slot.OpenDateId,slot.TimeSlotId,ManualLocked.IsChecked==true),"手動配置を追加しました");
    }
    private async void RemoveManual_Click(object sender,RoutedEventArgs e)
    {
        if(Assignments.SelectedItem is not ScheduleAssignmentItem assignment||!assignment.IsManual){ShowEditorError("削除する手動配置を選択してください。自動配置はリセットを使用します。");return;}await ExecuteEditorAsync(async()=>await App.ScheduleEditor.RemoveManualAsync(App.ProjectService.Current!.Path,assignment.Id),"手動配置を削除しました");
    }
    private async void ToggleLock_Click(object sender,RoutedEventArgs e)
    {
        if(Assignments.SelectedItem is not ScheduleAssignmentItem assignment){ShowEditorError("配置を選択してください。");return;}await ExecuteEditorAsync(async()=>await App.ScheduleEditor.SetLockedAsync(App.ProjectService.Current!.Path,assignment.Id,!assignment.IsLocked),assignment.IsLocked?"ロックを解除しました":"ロックしました");
    }
    private async void ResetAutomatic_Click(object sender,RoutedEventArgs e)=>await ExecuteEditorAsync(async()=>await App.ScheduleEditor.ResetAutomaticAsync(App.ProjectService.Current!.Path),"自動配置をリセットしました");
    private async Task ExecuteEditorAsync(Func<Task> action,string success)
    {
        var path=App.ProjectService.Current?.Path;var snapshotPushed=false;
        try
        {
            IsEnabled=false;
            if(path is not null){PushUndoSnapshot(await App.ScheduleEditor.CaptureSnapshotAsync(path));snapshotPushed=true;}
            await action();await ReloadEditorAsync();
            RunStatus.Severity=InfoBarSeverity.Success;RunStatus.Title=success;RunStatus.Message="";RunStatus.IsOpen=true;
        }
        catch(Exception exception)when(exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if(snapshotPushed){_undoStack.Pop();UpdateUndoRedoButtons();}
            ShowEditorError(exception.Message);
        }
        finally{IsEnabled=true;}
    }
    private void ShowEditorError(string message){RunStatus.Severity=InfoBarSeverity.Error;RunStatus.Title="時間割を編集できませんでした";RunStatus.Message=message;RunStatus.IsOpen=true;}

    private void QualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.Select(e.NewValue);
        if (!_isLoaded)
        {
            return;
        }

        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _saveDebounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_saveDebounce.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            var settings = await App.SettingsStore.LoadAsync(cancellationToken);
            await App.SettingsStore.SaveAsync(settings with { OptimizationQualityLevel = ViewModel.Level }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
        catch (UnauthorizedAccessException)
        {
            SaveErrorInfoBar.IsOpen = true;
        }
    }
}
