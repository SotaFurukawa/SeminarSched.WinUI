using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SeminarSched.Application.Scheduling;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace SeminarSched_WinUI.Pages;

public sealed partial class ScheduleEditorPage : WorkflowPageBase
{
    private readonly HashSet<long> _extraTeacherIds = [];
    private ScheduleBoard? _currentBoard;
    private long? _selectedDateId;
    private readonly TranslateTransform _columnHeaderTransform = new();
    private readonly TranslateTransform _rowHeaderTransform = new();
    private readonly TranslateTransform _cornerTransform = new();
    private sealed record CellTag(long TimeSlotId, long TeacherId, bool Blocked);

    public ScheduleEditorPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUndoRedoButtons();
        var ready = EnsureProject(ProjectRequired);
        ContentPanel.IsEnabled = ready;
        if (ready) await ReloadEditorAsync();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = ScheduleUndoState.UndoStack.Count > 0;
        RedoButton.IsEnabled = ScheduleUndoState.RedoStack.Count > 0;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleUndoState.UndoStack.Count == 0 || App.ProjectService.Current?.Path is not { } path) return;
        try
        {
            IsEnabled = false;
            var current = await App.ScheduleEditor.CaptureSnapshotAsync(path);
            var previous = ScheduleUndoState.UndoStack.Pop();
            ScheduleUndoState.RedoStack.Push(current);
            await App.ScheduleEditor.RestoreSnapshotAsync(path, previous);
            await ReloadEditorAsync();
            UpdateUndoRedoButtons();
            EditorStatus.Severity = InfoBarSeverity.Success; EditorStatus.Title = "元に戻しました"; EditorStatus.Message = ""; EditorStatus.IsOpen = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        { ShowEditorError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleUndoState.RedoStack.Count == 0 || App.ProjectService.Current?.Path is not { } path) return;
        try
        {
            IsEnabled = false;
            var current = await App.ScheduleEditor.CaptureSnapshotAsync(path);
            var next = ScheduleUndoState.RedoStack.Pop();
            ScheduleUndoState.UndoStack.Push(current);
            await App.ScheduleEditor.RestoreSnapshotAsync(path, next);
            await ReloadEditorAsync();
            UpdateUndoRedoButtons();
            EditorStatus.Severity = InfoBarSeverity.Success; EditorStatus.Title = "やり直しました"; EditorStatus.Message = ""; EditorStatus.IsOpen = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        { ShowEditorError(exception.Message); }
        finally { IsEnabled = true; }
    }

    private void GoToOptimization_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(OptimizationPage));

    private async Task ReloadEditorAsync()
    {
        var path=App.ProjectService.Current?.Path;if(path is null)return;
        PreconfirmRequest.ItemsSource=await App.FixedLessons.GetRequestsAsync(path);PreconfirmTeacher.ItemsSource=await App.FixedLessons.GetTeachersAsync(path);PreconfirmSlot.ItemsSource=await App.FixedLessons.GetSlotsAsync(path);
        ManualRequest.ItemsSource=await App.FixedLessons.GetRequestsAsync(path);ManualTeacher.ItemsSource=await App.FixedLessons.GetTeachersAsync(path);ManualSlot.ItemsSource=await App.FixedLessons.GetSlotsAsync(path);Assignments.ItemsSource=await App.ScheduleEditor.GetAssignmentsAsync(path);
        HistoryList.ItemsSource=await App.ScheduleEditor.GetAuditHistoryAsync(path);
        await ReloadDiffAsync(path);
        await ReloadBoardDatesAsync(path);
        await ReloadBoardAsync();
    }

    private async Task ReloadDiffAsync(string path)
    {
        if (ScheduleUndoState.ReoptimizationBaseline is not { } baseline) { DiffCard.Visibility = Visibility.Collapsed; return; }
        var current = await App.ScheduleEditor.CaptureSnapshotAsync(path);
        var (newlyPlaced, dateChanged, teacherChanged, unassigned) = ComputeDiff(baseline, current);
        if (newlyPlaced == 0 && dateChanged == 0 && teacherChanged == 0 && unassigned == 0)
        {
            DiffCard.Visibility = Visibility.Visible;
            DiffSummary.Text = "比較対象との差分はありません。";
            return;
        }
        DiffCard.Visibility = Visibility.Visible;
        DiffSummary.Text = $"新規配置: {newlyPlaced}件　日時変更: {dateChanged}件　講師変更: {teacherChanged}件　未配置化: {unassigned}件";
    }

    private static (int NewlyPlaced, int DateChanged, int TeacherChanged, int Unassigned) ComputeDiff(ScheduleSnapshot before, ScheduleSnapshot after)
    {
        var beforeByKey = before.Assignments.ToDictionary(a => (a.LessonRequestId, a.SessionIndex));
        var afterByKey = after.Assignments.ToDictionary(a => (a.LessonRequestId, a.SessionIndex));
        var newlyPlaced = 0; var dateChanged = 0; var teacherChanged = 0; var unassigned = 0;
        foreach (var (key, afterRow) in afterByKey)
        {
            if (!beforeByKey.TryGetValue(key, out var beforeRow)) { newlyPlaced++; continue; }
            if (beforeRow.OpenDateId != afterRow.OpenDateId || beforeRow.TimeSlotId != afterRow.TimeSlotId) dateChanged++;
            if (beforeRow.TeacherId != afterRow.TeacherId) teacherChanged++;
        }
        foreach (var key in beforeByKey.Keys)
            if (!afterByKey.ContainsKey(key)) unassigned++;
        return (newlyPlaced, dateChanged, teacherChanged, unassigned);
    }

    private async void Preconfirm_Click(object sender, RoutedEventArgs e)
    {
        if (PreconfirmRequest.SelectedItem is not LessonRequestOption request || PreconfirmTeacher.SelectedItem is not TeacherOption teacher || PreconfirmSlot.SelectedItem is not ScheduleSlotOption slot)
        { ShowEditorError("生徒・科目、担当講師、日付・コマを選択してください。"); return; }
        await ExecuteEditorAsync(async () => await App.ScheduleEditor.AddManualAsync(App.ProjectService.Current!.Path, request.Id, teacher.Id, slot.OpenDateId, slot.TimeSlotId, true), "事前確定として固定しました");
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
            BulkAvailabilityTeachers.ItemsSource = null; BulkAvailabilitySlots.ItemsSource = null;
            return;
        }
        var search = BoardSearch.Text?.Trim() ?? "";
        BulkAvailabilityTeachers.ItemsSource = board.Teachers;
        BulkAvailabilitySlots.ItemsSource = board.Slots;

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

        var headerBackground = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 250, 250, 250));
        Border HeaderCell(FrameworkElement content, TranslateTransform transform) => new()
        {
            Background = headerBackground,
            Padding = new Thickness(4),
            RenderTransform = transform,
            Child = content,
        };

        var corner = HeaderCell(new TextBlock(), _cornerTransform);
        Canvas.SetZIndex(corner, 2);
        Place(corner, 0, 0);
        for (var c = 0; c < board.Teachers.Count; c++)
        {
            var header = HeaderCell(new TextBlock { Text = board.Teachers[c].Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, _columnHeaderTransform);
            Canvas.SetZIndex(header, 1);
            Place(header, 0, c + 1);
        }

        for (var r = 0; r < board.Slots.Count; r++)
        {
            var slot = board.Slots[r];
            var header = HeaderCell(new TextBlock { Text = slot.Label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, _rowHeaderTransform);
            Canvas.SetZIndex(header, 1);
            Place(header, r + 1, 0);
            for (var c = 0; c < board.Teachers.Count; c++)
            {
                var teacher = board.Teachers[c];
                Place(CreateCell(slot.TimeSlotId, teacher.TeacherId, board.Cell(slot.TimeSlotId, teacher.TeacherId), search), r + 1, c + 1);
            }
        }
        _columnHeaderTransform.Y = BoardScroll.VerticalOffset;
        _rowHeaderTransform.X = BoardScroll.HorizontalOffset;
        _cornerTransform.X = BoardScroll.HorizontalOffset; _cornerTransform.Y = BoardScroll.VerticalOffset;
    }

    private void BoardScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        _columnHeaderTransform.Y = BoardScroll.VerticalOffset;
        _rowHeaderTransform.X = BoardScroll.HorizontalOffset;
        _cornerTransform.X = BoardScroll.HorizontalOffset; _cornerTransform.Y = BoardScroll.VerticalOffset;
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
        if (card.IsManual && !card.IsLocked)
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
        await ExecuteEditorAsync(() => App.ScheduleEditor.SetTeacherUnavailableAsync(path, cell.TeacherId, dateId, cell.TimeSlotId, !cell.Blocked), cell.Blocked ? "出勤可能にしました" : "出勤不可にしました", clearsHistory: true);
    }

    private async void BulkSetUnavailable_Click(object sender, RoutedEventArgs e) => await BulkSetTeacherAvailabilityAsync(true);
    private async void BulkSetAvailable_Click(object sender, RoutedEventArgs e) => await BulkSetTeacherAvailabilityAsync(false);

    private async Task BulkSetTeacherAvailabilityAsync(bool unavailable)
    {
        if (App.ProjectService.Current?.Path is not { } path || _selectedDateId is not { } dateId) return;
        var teachers = BulkAvailabilityTeachers.SelectedItems.Cast<BoardTeacherColumn>().ToArray();
        var slots = BulkAvailabilitySlots.SelectedItems.Cast<BoardSlotRow>().ToArray();
        if (teachers.Length == 0 || slots.Length == 0) { ShowEditorError("講師とコマをそれぞれ1件以上選択してください。"); return; }
        var targets = teachers.SelectMany(teacher => slots.Select(slot => (teacher.TeacherId, slot.TimeSlotId))).ToArray();
        await ExecuteEditorAsync(() => App.ScheduleEditor.SetTeacherUnavailableManyAsync(path, dateId, targets, unavailable),
            $"{teachers.Length}名×{slots.Length}コマを{(unavailable ? "出勤不可" : "出勤可能")}にしました", clearsHistory: true);
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
                var (proceed, confirmSoftWarnings, reason) = await ResolveMovePreviewAsync(path, id, cell.TeacherId, dateId, cell.TimeSlotId);
                if (!proceed) return;
                await ExecuteEditorAsync(() => App.ScheduleEditor.MoveAsync(path, id, cell.TeacherId, dateId, cell.TimeSlotId, confirmSoftWarnings, reason), "配置を移動しました");
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
    private async void ResetAutomatic_Click(object sender,RoutedEventArgs e)=>await ExecuteEditorAsync(async()=>await App.ScheduleEditor.ResetAutomaticAsync(App.ProjectService.Current!.Path),"自動配置をリセットしました",clearsHistory:true);

    // ドラッグ移動の実行前にPython版と同じgreen/yellow/red判定を行う。redはエラー表示して中止、
    // yellowはソフト指標の悪化内容を確認ダイアログで提示し、ユーザーが理由を入力・確認した場合のみ
    // 実行を許可する（confirmSoftWarnings:trueで再度MoveAsyncを呼ぶ）。
    private async Task<(bool Proceed,bool ConfirmSoftWarnings,string? Reason)> ResolveMovePreviewAsync(string path,long assignmentId,long teacherId,long openDateId,long timeSlotId)
    {
        EditPreview preview;
        try
        {
            preview = await App.ScheduleEditor.PreviewMoveAsync(path, assignmentId, teacherId, openDateId, timeSlotId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            ShowEditorError(ex.Message);
            return (false, false, null);
        }
        if (preview.Decision == EditDecision.Red)
        {
            ShowEditorError(preview.Message);
            return (false, false, null);
        }
        if (preview.Decision == EditDecision.Green) return (true, false, null);

        var reasonBox = new TextBox { PlaceholderText = "変更理由（監査ログへ保存）" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "△ " + preview.Message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new ListView { ItemsSource = preview.WorsenedDeltas.Select(d => d.Message).ToArray(), IsItemClickEnabled = false });
        panel.Children.Add(reasonBox);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "ソフト条件の悪化を確認",
            Content = panel,
            PrimaryButtonText = "変更する",
            CloseButtonText = "キャンセル",
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return (false, false, null);
        return (true, true, string.IsNullOrWhiteSpace(reasonBox.Text) ? null : reasonBox.Text);
    }

    private async Task ExecuteEditorAsync(Func<Task> action,string success,bool clearsHistory=false)
    {
        var path=App.ProjectService.Current?.Path;var snapshotPushed=false;
        try
        {
            IsEnabled=false;
            if(path is not null && !clearsHistory){ScheduleUndoState.Push(await App.ScheduleEditor.CaptureSnapshotAsync(path));snapshotPushed=true;}
            await action();
            if(clearsHistory)ScheduleUndoState.Clear();
            await ReloadEditorAsync();
            UpdateUndoRedoButtons();
            EditorStatus.Severity=InfoBarSeverity.Success;EditorStatus.Title=success;EditorStatus.Message="";EditorStatus.IsOpen=true;
        }
        catch(Exception exception)when(exception is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if(snapshotPushed){ScheduleUndoState.UndoStack.Pop();UpdateUndoRedoButtons();}
            ShowEditorError(exception.Message);
        }
        finally{IsEnabled=true;}
    }
    private void ShowEditorError(string message){EditorStatus.Severity=InfoBarSeverity.Error;EditorStatus.Title="時間割を編集できませんでした";EditorStatus.Message=message;EditorStatus.IsOpen=true;}
}
