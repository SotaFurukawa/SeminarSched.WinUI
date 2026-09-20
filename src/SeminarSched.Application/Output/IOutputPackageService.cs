namespace SeminarSched.Application.Output;

/// <summary>
/// Python版⑥出力の5帳票（全体時間割・生徒配布時間割・講師配布時間割（学年順）・未配置警告一覧）を
/// それぞれ独立したxlsx/pdfとして、講師配布時間割（講師別）は講師ごとの個別ファイルを folder へ出力する。
/// </summary>
public sealed record OutputPackageResult(
    string DirectoryPath,
    string OverallExcelPath,string OverallPdfPath,
    string StudentHandoutsExcelPath,string StudentHandoutsPdfPath,
    string TeacherHandoutsExcelPath,string TeacherHandoutsPdfPath,
    string IssuesExcelPath,string IssuesPdfPath,
    string TeacherPacketDirectory,
    string CombinedTeacherPacketExcelPath,string CombinedTeacherPacketPdfPath,
    int AssignmentCount,int UnassignedCount);

public interface IOutputPackageService
{
    Task<OutputPackageResult> GenerateAsync(string projectPath,string parentDirectory,CancellationToken cancellationToken=default);
}
