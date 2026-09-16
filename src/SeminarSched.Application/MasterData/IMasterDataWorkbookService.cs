namespace SeminarSched.Application.MasterData;

public enum MasterWorkbookIssueSeverity
{
    Warning,
    Error,
}

public sealed record MasterWorkbookIssue(
    MasterWorkbookIssueSeverity Severity,
    string SheetName,
    int? RowNumber,
    string? ColumnName,
    string Message);

public sealed record MasterWorkbookPreview(
    string SourcePath,
    string Sha256,
    IReadOnlyDictionary<string, int> NewCounts,
    IReadOnlyDictionary<string, int> UpdateCounts,
    IReadOnlyList<MasterWorkbookIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == MasterWorkbookIssueSeverity.Error);
    public int WarningCount => Issues.Count(issue => issue.Severity == MasterWorkbookIssueSeverity.Warning);
}

public sealed record MasterWorkbookImportResult(int ImportedRows, int WarningCount);

public interface IMasterDataWorkbookService
{
    Task ExportAsync(string projectPath, string destinationPath, CancellationToken cancellationToken = default);
    Task<MasterWorkbookPreview> PreviewAsync(string projectPath, string sourcePath, CancellationToken cancellationToken = default);
    Task<MasterWorkbookImportResult> ApplyAsync(string projectPath, MasterWorkbookPreview preview, CancellationToken cancellationToken = default);
}
