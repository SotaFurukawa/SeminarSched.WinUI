namespace SeminarSched.Application.Projects;

public sealed record ProjectIntegrityResult(bool IsValid, string Message)
{
    public static ProjectIntegrityResult Valid { get; } = new(true, "整合性に問題はありません。");
}
