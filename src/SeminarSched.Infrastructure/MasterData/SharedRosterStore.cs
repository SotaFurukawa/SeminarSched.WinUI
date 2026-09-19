using SeminarSched.Application.MasterData;
using SeminarSched.Domain.MasterData;
using SeminarSched.Domain.Projects;
using SeminarSched.Infrastructure.Projects;

namespace SeminarSched.Infrastructure.MasterData;

public sealed class SharedRosterStore : ISharedRosterStore
{
    private readonly ISharedRosterImportService _importService;
    private readonly IMasterDataRepository _masterData;
    private readonly string _databasePath;

    public string WorkbookPath { get; }

    public SharedRosterStore(ISharedRosterImportService importService, IMasterDataRepository masterData, string? directory = null)
    {
        _importService = importService;
        _masterData = masterData;
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeminarSched.WinUI", "Workspace", "SharedRoster");
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "roster.db");
        WorkbookPath = Path.Combine(directory, "生徒・講師_基本情報.xlsx");
    }

    public async Task<string> EnsureWorkbookAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        if (!File.Exists(WorkbookPath))
        {
            var (students, teachers, subjects, qualifications, regularLessons) = await LoadAsync(cancellationToken).ConfigureAwait(false);
            SharedRosterWorkbookWriter.Write(WorkbookPath, students, teachers, subjects, qualifications, regularLessons);
        }
        return WorkbookPath;
    }

    public Task ExportBlankTemplateAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        SharedRosterWorkbookWriter.Write(Path.GetFullPath(targetPath), [], [], [], [], []);
        return Task.CompletedTask;
    }

    public async Task<SharedRosterPreview> PreviewImportAsync(string sourceWorkbookPath, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        return await _importService.PreviewAsync(_databasePath, sourceWorkbookPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedRosterImportResult> ApplyImportAsync(SharedRosterPreview preview, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var result = await _importService.ApplyAsync(_databasePath, preview, cancellationToken).ConfigureAwait(false);
        // 次に「Excelで編集」を開いたとき、今反映した最新の内容から再生成させる（編集中の古い内容を残さない）。
        if (File.Exists(WorkbookPath)) File.Delete(WorkbookPath);
        return result;
    }

    public async Task<SharedRosterImportResult?> CopyIntoProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var (students, teachers, subjects, qualifications, regularLessons) = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (students.Count == 0 && teachers.Count == 0 && subjects.Count == 0) return null;

        var temporary = Path.Combine(Path.GetTempPath(), $"SeminarSched-SharedRoster-{Guid.NewGuid():N}.xlsx");
        try
        {
            SharedRosterWorkbookWriter.Write(temporary, students, teachers, subjects, qualifications, regularLessons);
            var preview = await _importService.PreviewAsync(projectPath, temporary, cancellationToken).ConfigureAwait(false);
            if (preview.HasErrors)
                throw new InvalidOperationException("共通名簿をプロジェクトへ反映できませんでした。" + Environment.NewLine +
                    string.Join(Environment.NewLine, preview.Issues.Where(issue => issue.Severity == SharedRosterIssueSeverity.Error).Select(issue => issue.Message)));
            return await _importService.ApplyAsync(projectPath, preview, cancellationToken).ConfigureAwait(false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<GradeAdvancementSummary> AdvanceStudentGradesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var students = await _masterData.GetStudentsAsync(_databasePath, includeInactive: false, cancellationToken).ConfigureAwait(false);
        var advanced = 0; var graduated = 0;
        foreach (var student in students)
        {
            var (grade, becameGraduate) = GradeAdvancement.Advance(student.Grade);
            if (grade == student.Grade) continue;
            var updated = new Student(student.Id, student.ExternalId, student.Name, grade, student.DefaultMaxConsecutiveSlots, student.AllowGap, student.Note, active: becameGraduate ? false : student.Active);
            await _masterData.SaveStudentAsync(_databasePath, updated, cancellationToken).ConfigureAwait(false);
            advanced++;
            if (becameGraduate) graduated++;
        }
        // 次に「Excelで編集」を開いたとき、繰り上げ後の最新の内容から再生成させる。
        if (File.Exists(WorkbookPath)) File.Delete(WorkbookPath);
        return new GradeAdvancementSummary(advanced, graduated);
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_databasePath)) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        await new SqliteProjectRepository().CreateAsync(_databasePath, CourseProjectDefinition.Create(today.Year, CourseSeason.Summer, today, today), cancellationToken).ConfigureAwait(false);
    }

    private async Task<(IReadOnlyList<Student> Students, IReadOnlyList<Teacher> Teachers, IReadOnlyList<Subject> Subjects, IReadOnlyList<TeacherQualification> Qualifications, IReadOnlyList<RegularLessonProfile> RegularLessons)> LoadAsync(CancellationToken cancellationToken)
    {
        var students = await _masterData.GetStudentsAsync(_databasePath, includeInactive: true, cancellationToken).ConfigureAwait(false);
        var teachers = await _masterData.GetTeachersAsync(_databasePath, includeInactive: true, cancellationToken).ConfigureAwait(false);
        var subjects = await _masterData.GetSubjectsAsync(_databasePath, includeInactive: true, cancellationToken).ConfigureAwait(false);
        var qualifications = await _masterData.GetQualificationsAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        var regularLessons = await _masterData.GetRegularLessonsAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        return (students, teachers, subjects, qualifications, regularLessons);
    }
}
