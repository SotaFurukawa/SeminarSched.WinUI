namespace SeminarSched.Application.Importing;

public sealed record ImportIssue(int Row, string Field, string Message, bool IsError = true);

public sealed record ResponseImportPreview(
    string StudentFilePath,
    string TeacherFilePath,
    string StudentFileSha256,
    string TeacherFileSha256,
    int StudentRowCount,
    int TeacherRowCount,
    IReadOnlyList<ImportIssue> Issues)
{
    public bool CanApply => Issues.All(issue => !issue.IsError);
}
