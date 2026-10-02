namespace SeminarSched.Application.Updates;

public sealed record UpdateCheckResult(
    bool Succeeded,
    bool IsUpdateAvailable,
    ApplicationVersion? LatestVersion,
    string? ReleaseUrl,
    string? ErrorMessage)
{
    public static UpdateCheckResult Failure(string errorMessage) => new(false, false, null, null, errorMessage);

    public static UpdateCheckResult UpToDate() => new(true, false, null, null, null);

    public static UpdateCheckResult Available(ApplicationVersion latestVersion, string releaseUrl) =>
        new(true, true, latestVersion, releaseUrl, null);
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(ApplicationVersion currentVersion, CancellationToken cancellationToken = default);
}
