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
}
