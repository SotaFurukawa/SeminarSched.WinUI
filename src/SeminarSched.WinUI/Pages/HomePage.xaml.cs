using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using SeminarSched.Application.MasterData;
using SeminarSched.Application.Projects;
using SeminarSched.Application.Settings;
using SeminarSched.Domain.Projects;
using WinRT.Interop;

namespace SeminarSched_WinUI.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // NumberBoxの未設定値はdouble.NaNであり0ではないため、この判定は常にfalseとなり
        // 年度・開始/終了日の既定値設定とProjectDefinition_Changedの購読が一度も実行されない
        // 不具合があった（ユーザー報告：年度の初期値が今の年度になっていない）。
        if (double.IsNaN(AcademicYearBox.Value))
        {
            AcademicYearBox.Value = DateTime.Today.Year;
            StartDatePicker.Date = DateTimeOffset.Now.Date;
            EndDatePicker.Date = DateTimeOffset.Now.Date.AddDays(30);
            AcademicYearBox.ValueChanged += ProjectDefinition_Changed;
            SeasonBox.SelectionChanged += ProjectDefinition_Changed;
        }

        RefreshGeneratedTitle();
        RefreshCurrentProject();
        SharedRosterPathText.Text = $"保存先: {App.SharedRosterStore.WorkbookPath}";
        await RefreshRecentProjectsAsync();
    }

    private async void EditSharedRoster_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            var path = await App.SharedRosterStore.EnsureWorkbookAsync();
            SharedRosterPathText.Text = $"保存先: {path}";
            var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ShowStatus(started is null ? InfoBarSeverity.Warning : InfoBarSeverity.Success, started is null ? "既定のアプリで開けませんでした" : "共通名簿Excelを開きました", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(InfoBarSeverity.Error, "共通名簿Excelを開けませんでした", exception.Message);
        }
        finally { SetBusy(false); }
    }

    private async void NewSharedRosterTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker(GetWindowId()) { SuggestedFolder = ProjectService.DefaultProjectsDirectory, SuggestedFileName = "生徒・講師_基本情報" };
            picker.FileTypeChoices.Add("Excelブック", [".xlsx"]);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            SetBusy(true);
            await App.SharedRosterStore.ExportBlankTemplateAsync(file.Path);
            ShowStatus(InfoBarSeverity.Success, "新しい基本情報テンプレートを保存しました", file.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, "テンプレートを保存できませんでした", exception.Message);
        }
        finally { SetBusy(false); }
    }

    private async void ImportSharedRoster_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(GetWindowId()) { SuggestedFolder = ProjectService.DefaultProjectsDirectory };
            picker.FileTypeFilter.Add(".xlsx");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            SetBusy(true);
            var preview = await App.SharedRosterStore.PreviewImportAsync(file.Path);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = preview.HasErrors ? "取込エラーがあります" : "共通名簿へ反映しますか？",
                Content = new ScrollViewer { MaxHeight = 480, Content = new TextBlock { Text = BuildSharedRosterPreviewSummary(preview), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                CloseButtonText = preview.HasErrors ? "閉じる" : "キャンセル",
                PrimaryButtonText = preview.HasErrors ? null : "反映する",
                DefaultButton = preview.HasErrors ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            SetBusy(false);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            SetBusy(true);
            var result = await App.SharedRosterStore.ApplyImportAsync(preview);
            var message = $"{result.ImportedRows}行（警告{result.WarningCount}件）";
            if (App.ProjectService.Current is { } current)
            {
                var projectResult = await App.SharedRosterStore.CopyIntoProjectAsync(current.Path);
                if (projectResult is not null) message += "。現在開いているプロジェクトへも反映しました";
            }
            ShowStatus(InfoBarSeverity.Success, "共通名簿を反映しました", message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or SqliteException)
        {
            ShowStatus(InfoBarSeverity.Error, "反映できませんでした", exception.Message);
        }
        finally { SetBusy(false); }
    }

    private static string BuildSharedRosterPreviewSummary(SharedRosterPreview preview)
    {
        var lines = new List<string> { "シート                         件数" };
        foreach (var (name, count) in new (string, int)[] { ("生徒", preview.StudentCount), ("講師", preview.TeacherCount), ("科目", preview.SubjectCount), ("講師対応科目", preview.QualificationCount), ("通常授業", preview.RegularLessonCount) })
            lines.Add($"{name,-14} {count,4}");
        if (preview.Issues.Count != 0)
        {
            lines.Add(""); lines.Add($"検証結果（エラー{preview.Issues.Count(issue => issue.Severity == SharedRosterIssueSeverity.Error)}件・警告{preview.Issues.Count(issue => issue.Severity == SharedRosterIssueSeverity.Warning)}件）");
            lines.AddRange(preview.Issues.Take(100).Select(issue => $"{issue.SheetName} {(issue.RowNumber is null ? "" : $"{issue.RowNumber}行 ")}{issue.ColumnName}: {issue.Message}"));
            if (preview.Issues.Count > 100) lines.Add($"ほか{preview.Issues.Count - 100}件");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var definition = BuildDefinition();
            var picker = new FolderPicker(GetWindowId()) { SuggestedFolder = ProjectService.DefaultProjectsDirectory };
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var path = Path.Combine(folder.Path, definition.Title + ProjectService.ProjectExtension);
            SetBusy(true);
            var summary = await App.ProjectService.CreateAsync(path, definition);
            var sharedRosterResult = await App.SharedRosterStore.CopyIntoProjectAsync(summary.Path);
            await App.RecentProjects.TouchAsync(summary.Path, summary.Title);
            RefreshCurrentProject();
            await RefreshRecentProjectsAsync();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを作成しました", sharedRosterResult is null ? summary.Title : $"{summary.Title}（共通名簿から{sharedRosterResult.ImportedRows}行を反映）");
            App.Logger.Info("Project created");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or SqliteException or InvalidDataException or InvalidOperationException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを作成できませんでした", exception.Message);
            App.Logger.Error("Project creation failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(GetWindowId()) { SuggestedFolder = ProjectService.DefaultProjectsDirectory };
            picker.FileTypeFilter.Add(ProjectService.ProjectExtension);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            SetBusy(true);
            var summary = await App.ProjectService.OpenAsync(file.Path);
            await App.RecentProjects.TouchAsync(summary.Path, summary.Title);
            RefreshCurrentProject();
            await RefreshRecentProjectsAsync();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを開きました", summary.Title);
            App.Logger.Info("Project opened");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or SqliteException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを開けませんでした", exception.Message);
            App.Logger.Error("Project open failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        App.ProjectService.Close();
        RefreshCurrentProject();
        ShowStatus(InfoBarSeverity.Informational, "プロジェクトを閉じました", string.Empty);
        App.Logger.Info("Project closed");
    }

    private async void RecentProject_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RecentProjectEntry entry)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var summary = await App.ProjectService.OpenAsync(entry.Path);
            await App.RecentProjects.TouchAsync(summary.Path, summary.Title);
            RefreshCurrentProject();
            await RefreshRecentProjectsAsync();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを開きました", summary.Title);
        }
        catch (FileNotFoundException)
        {
            await App.RecentProjects.RemoveAsync(entry.Path);
            await RefreshRecentProjectsAsync();
            ShowStatus(InfoBarSeverity.Warning, "プロジェクトが見つかりません", "履歴から削除しました。");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or SqliteException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを開けませんでした", exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void HideRecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        try
        {
            await App.RecentProjects.RemoveAsync(path);
            await RefreshRecentProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Warning, "履歴を更新できませんでした", exception.Message);
        }
    }

    private async Task RefreshRecentProjectsAsync()
    {
        try
        {
            var entries = await App.RecentProjects.GetAsync();
            RecentProjectsList.ItemsSource = entries;
            NoRecentProjectsText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Warning, "最近使ったプロジェクトを読み込めませんでした", exception.Message);
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var current = App.ProjectService.Current;
        if (current is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "バックアップを作成できません", "先にプロジェクトを開いてください。");
            return;
        }

        try
        {
            var backupName = $"{current.Title}_backup_{DateTime.Now:yyyyMMdd_HHmmss}{ProjectService.ProjectExtension}";
            SetBusy(true);
            var path = await App.ProjectService.CreateBackupAsync(Path.Combine(ProjectService.DefaultBackupDirectory, backupName));
            ShowStatus(InfoBarSeverity.Success, "バックアップを作成しました", path);
            App.Logger.Info("Backup created");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or SqliteException)
        {
            ShowStatus(InfoBarSeverity.Error, "バックアップを作成できませんでした", exception.Message);
            App.Logger.Error("Backup creation failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (App.ProjectService.Current is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "復元できません", "復元先のプロジェクトを先に開いてください。");
            return;
        }

        try
        {
            var picker = new FileOpenPicker(GetWindowId()) { SuggestedFolder = ProjectService.DefaultBackupDirectory };
            picker.FileTypeFilter.Add(ProjectService.ProjectExtension);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "バックアップから復元しますか？",
                Content = "現在のプロジェクトを選択したバックアップの内容で置き換えます。",
                PrimaryButtonText = "復元する",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            SetBusy(true);
            var restored = await App.ProjectService.RestoreBackupAsync(file.Path);
            RefreshCurrentProject();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを復元しました", restored.Title);
            App.Logger.Info("Project restored from backup");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or SqliteException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを復元できませんでした", exception.Message);
            App.Logger.Error("Project restore failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveAs_Click(object sender,RoutedEventArgs e)
    {
        var current=App.ProjectService.Current;if(current is null){ShowStatus(InfoBarSeverity.Warning,"複製できません","先にプロジェクトを開いてください。");return;}
        try{var picker=new FolderPicker(GetWindowId()){SuggestedFolder=ProjectService.DefaultProjectsDirectory};var folder=await picker.PickSingleFolderAsync();if(folder is null)return;var name=$"{current.Title}_copy_{DateTime.Now:yyyyMMdd_HHmmss}{ProjectService.ProjectExtension}";SetBusy(true);var copy=await App.ProjectService.SaveAsAsync(Path.Combine(folder.Path,name));await App.RecentProjects.TouchAsync(copy.Path,copy.Title);RefreshCurrentProject();await RefreshRecentProjectsAsync();ShowStatus(InfoBarSeverity.Success,"複製へ切り替えました",copy.Path);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException){ShowStatus(InfoBarSeverity.Error,"複製できませんでした",ex.Message);}finally{SetBusy(false);}
    }

    private void ProjectDefinition_Changed(object sender, object e) => RefreshGeneratedTitle();

    private CourseProjectDefinition BuildDefinition()
    {
        var seasonItem = SeasonBox.SelectedItem as ComboBoxItem
            ?? throw new ArgumentException("講習区分を選択してください。");
        if (!int.TryParse(Convert.ToString(seasonItem.Tag), out var seasonValue))
        {
            throw new ArgumentException("講習区分が正しくありません。");
        }

        var start = StartDatePicker.Date
            ?? throw new ArgumentException("開始日を選択してください。");
        var end = EndDatePicker.Date
            ?? throw new ArgumentException("終了日を選択してください。");
        return CourseProjectDefinition.Create(
            checked((int)AcademicYearBox.Value),
            (CourseSeason)seasonValue,
            DateOnly.FromDateTime(start.DateTime),
            DateOnly.FromDateTime(end.DateTime));
    }

    private void RefreshGeneratedTitle()
    {
        try
        {
            GeneratedTitleBox.Text = BuildDefinition().Title;
        }
        catch (ArgumentException)
        {
            GeneratedTitleBox.Text = string.Empty;
        }
    }

    private void RefreshCurrentProject()
    {
        ScheduleUndoState.Clear();
        var current = App.ProjectService.Current;
        CurrentProjectTitle.Text = current?.Title ?? "プロジェクトは開かれていません";
        CurrentProjectPeriod.Text = current is null
            ? string.Empty
            : $"{current.StartDate:yyyy年M月d日} ～ {current.EndDate:yyyy年M月d日}";
        CurrentProjectPath.Text = current?.Path ?? string.Empty;
    }

    private static WindowId GetWindowId()
    {
        var window = App.MainWindow ?? throw new InvalidOperationException("The main window is not available.");
        return Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(window));
    }

    private void SetBusy(bool isBusy) => IsEnabled = !isBusy;

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }
}
