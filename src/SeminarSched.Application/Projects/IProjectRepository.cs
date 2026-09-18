using SeminarSched.Domain.Projects;

namespace SeminarSched.Application.Projects;

public interface IProjectRepository
{
    Task<ProjectSummary> CreateAsync(
        string path,
        CourseProjectDefinition definition,
        CancellationToken cancellationToken = default);

    Task<ProjectSummary> OpenAsync(string path, CancellationToken cancellationToken = default);

    Task<ProjectIntegrityResult> CheckIntegrityAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task CreateBackupAsync(
        string sourcePath,
        string backupPath,
        CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(
        string backupPath,
        string targetPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a timestamped backup of <paramref name="path"/> in its dedicated "_backups" folder and
    /// prunes older automatic backups beyond <paramref name="maxGenerations"/>. Best-effort: failures
    /// (e.g. a read-only backup folder) are swallowed so this never blocks the caller's primary operation.
    /// </summary>
    Task CreateAutomaticBackupAsync(
        string path,
        int maxGenerations,
        CancellationToken cancellationToken = default);
}
