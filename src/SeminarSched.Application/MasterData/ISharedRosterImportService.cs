namespace SeminarSched.Application.MasterData;

public enum SharedRosterIssueSeverity { Error, Warning }

public sealed record SharedRosterIssue(SharedRosterIssueSeverity Severity, string SheetName, int? RowNumber, string? ColumnName, string Message);

public sealed record SharedRosterPreview(
    string SourcePath,
    string Sha256,
    int StudentCount,
    int TeacherCount,
    int SubjectCount,
    int QualificationCount,
    int RegularLessonCount,
    IReadOnlyList<SharedRosterIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == SharedRosterIssueSeverity.Error);
    public bool CanApply => !HasErrors;
}

public sealed record SharedRosterImportResult(int ImportedRows, int WarningCount);

/// <summary>
/// Imports the Python reference app's "生徒・講師_基本情報.xlsx" (年度をまたいで利用する共通名簿) format:
/// 生徒/講師/科目/講師対応科目/通常授業 sheets carried over between course periods.
/// </summary>
public interface ISharedRosterImportService
{
    Task<SharedRosterPreview> PreviewAsync(string projectPath, string sourcePath, CancellationToken cancellationToken = default);
    Task<SharedRosterImportResult> ApplyAsync(string projectPath, SharedRosterPreview preview, CancellationToken cancellationToken = default);
}
