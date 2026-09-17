namespace SeminarSched.Application.Importing;

public sealed record ImportIssue(int Row, string Field, string Message, bool IsError = true);

public sealed record AvailabilityDiffKey(string ExternalId, string Date)
{
    public override string ToString() => $"{ExternalId}　{Date}";
}

public sealed record ResponseImportDiff(
    int StudentAdded, int StudentChanged, int StudentUnchanged, IReadOnlyList<AvailabilityDiffKey> StudentRemovalCandidates,
    int TeacherAdded, int TeacherChanged, int TeacherUnchanged, IReadOnlyList<AvailabilityDiffKey> TeacherRemovalCandidates)
{
    public static ResponseImportDiff Empty { get; } = new(0, 0, 0, [], 0, 0, 0, []);
    public bool HasRemovalCandidates => StudentRemovalCandidates.Count > 0 || TeacherRemovalCandidates.Count > 0;
}

public sealed record ResponseImportPreview(
    string StudentFilePath,
    string TeacherFilePath,
    string StudentFileSha256,
    string TeacherFileSha256,
    int StudentRowCount,
    int TeacherRowCount,
    IReadOnlyList<ImportIssue> Issues,
    ResponseImportDiff Diff)
{
    public bool CanApply => Issues.All(issue => !issue.IsError);
}
