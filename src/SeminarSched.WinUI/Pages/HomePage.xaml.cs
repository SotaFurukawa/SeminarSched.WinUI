using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.Projects;
using SeminarSched.Domain.Projects;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SeminarSched_WinUI.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (AcademicYearBox.Value == 0)
        {
            AcademicYearBox.Value = DateTime.Today.Year;
            StartDatePicker.Date = DateTimeOffset.Now.Date;
            EndDatePicker.Date = DateTimeOffset.Now.Date.AddDays(30);
            AcademicYearBox.ValueChanged += ProjectDefinition_Changed;
            SeasonBox.SelectionChanged += ProjectDefinition_Changed;
        }

        RefreshGeneratedTitle();
        RefreshCurrentProject();
    }

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var definition = BuildDefinition();
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var path = Path.Combine(folder.Path, definition.Title + ProjectService.ProjectExtension);
            SetBusy(true);
            var summary = await App.ProjectService.CreateAsync(path, definition);
            RefreshCurrentProject();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを作成しました", summary.Title);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを作成できませんでした", exception.Message);
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
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(ProjectService.ProjectExtension);
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            SetBusy(true);
            var summary = await App.ProjectService.OpenAsync(file.Path);
            RefreshCurrentProject();
            ShowStatus(InfoBarSeverity.Success, "プロジェクトを開きました", summary.Title);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを開けませんでした", exception.Message);
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
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var backupName = $"{current.Title}_backup_{DateTime.Now:yyyyMMdd_HHmmss}{ProjectService.ProjectExtension}";
            SetBusy(true);
            var path = await App.ProjectService.CreateBackupAsync(Path.Combine(folder.Path, backupName));
            ShowStatus(InfoBarSeverity.Success, "バックアップを作成しました", path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, "バックアップを作成できませんでした", exception.Message);
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
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(ProjectService.ProjectExtension);
            InitializePicker(picker);
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
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, "プロジェクトを復元できませんでした", exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
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
        var current = App.ProjectService.Current;
        CurrentProjectTitle.Text = current?.Title ?? "プロジェクトは開かれていません";
        CurrentProjectPeriod.Text = current is null
            ? string.Empty
            : $"{current.StartDate:yyyy年M月d日} ～ {current.EndDate:yyyy年M月d日}";
        CurrentProjectPath.Text = current?.Path ?? string.Empty;
    }

    private static void InitializePicker(object picker)
    {
        var window = App.MainWindow ?? throw new InvalidOperationException("The main window is not available.");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
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
