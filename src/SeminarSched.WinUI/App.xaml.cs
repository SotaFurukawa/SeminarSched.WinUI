using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SeminarSched.Application.Projects;
using SeminarSched.Application.Settings;
using SeminarSched.Infrastructure.Projects;
using SeminarSched.Application.MasterData;
using SeminarSched.Infrastructure.MasterData;
using SeminarSched.Application.CourseSettings;
using SeminarSched.Infrastructure.CourseSettings;
using SeminarSched.Application.Questionnaires;
using SeminarSched.Application.Importing;
using SeminarSched.Infrastructure.Importing;
using SeminarSched.Application.Scheduling;
using SeminarSched.Infrastructure.Scheduling;
using SeminarSched.Application.Output;
using SeminarSched.Infrastructure.Output;
using SeminarSched.Infrastructure.Settings;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeminarSched_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static Window? MainWindow { get; private set; }

    public static IAppSettingsStore SettingsStore { get; } = new JsonAppSettingsStore(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeminarSched.WinUI",
            "settings.json"));

    public static ProjectService ProjectService { get; } = new(new SqliteProjectRepository());

    public static RecentProjectService RecentProjects { get; } = new(SettingsStore);

    public static IMasterDataRepository MasterData { get; } = new SqliteMasterDataRepository();

    public static IMasterDataWorkbookService MasterDataWorkbook { get; } = new MasterDataWorkbookService();

    public static ICourseSettingsRepository CourseSettings { get; } = new SqliteCourseSettingsRepository();

    public static QuestionnaireKitService QuestionnaireKit { get; } = new(CourseSettings, MasterData);

    public static IResponseImportService ResponseImport { get; } = new CsvResponseImportService();

    public static IAvailabilityMatrixService AvailabilityMatrix { get; } = new SqliteAvailabilityMatrixService();

    public static IFixedLessonService FixedLessons { get; } = new SqliteFixedLessonService();

    public static IScheduleRunService ScheduleRun { get; } = new SqliteScheduleRunService();

    public static IScheduleEditorService ScheduleEditor { get; } = new SqliteScheduleEditorService();

    public static IOutputPackageService OutputPackage { get; } = new SqliteOutputPackageService();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }
}
