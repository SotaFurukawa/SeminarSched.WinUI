namespace SeminarSched.Application.Importing;

public enum CourseSurveyIssueSeverity { Error, Warning }

public sealed record CourseSurveyIssue(CourseSurveyIssueSeverity Severity, string Source, int Row, string PersonName, string Message, string Resolution);

public sealed record CourseSurveyPreview(
    string StudentPath,
    string TeacherPath,
    string StudentSha256,
    string TeacherSha256,
    int StudentCount,
    int TeacherCount,
    int RequestCount,
    IReadOnlyList<CourseSurveyIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == CourseSurveyIssueSeverity.Error);
}

public sealed record CourseSurveyApplyResult(int Students, int Teachers, int LessonRequests, int TrialStudents, int WarningCount);

/// <summary>
/// Googleフォームが生成した生の回答CSV/XLSX（質問文そのままの列名、日付ごとに分かれた
/// 受講不可日時列、教科ごとに分かれた受講教科・受講回数・学校区分列）を2ファイルまとめて
/// 検証・反映する。既存の<see cref="IResponseImportService"/>（列名を固定した簡易CSV向けの
/// 差分更新）とは異なり、生徒・講師ごとに受講希望・可用性を全置換する。
/// </summary>
public interface ICourseSurveyImportService
{
    Task<CourseSurveyPreview> PreviewAsync(string projectPath, string studentPath, string teacherPath, CancellationToken cancellationToken = default);
    Task<CourseSurveyApplyResult> ApplyAsync(string projectPath, CourseSurveyPreview preview, CancellationToken cancellationToken = default);
}
