using SeminarSched.Application.Projects;
using SeminarSched.Domain.Projects;

namespace SeminarSched.Application.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SeminarSched.ProjectService.{Guid.NewGuid():N}");

    [Fact]
    public void DefaultProjectsAndBackupDirectories_AreCentralizedUnderLocalAppDataAndExist()
    {
        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeminarSched.WinUI", "Workspace");
        Assert.Equal(Path.Combine(appDataRoot, "Projects"), ProjectService.DefaultProjectsDirectory);
        Assert.Equal(Path.Combine(appDataRoot, "Backups"), ProjectService.DefaultBackupDirectory);
        Assert.True(Directory.Exists(ProjectService.DefaultProjectsDirectory));
        Assert.True(Directory.Exists(ProjectService.DefaultBackupDirectory));
    }

    [Fact]
    public async Task SaveAsAsync_CopiesProjectAndSwitchesCurrentWithoutChangingSource()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.jukuschedule");
        await File.WriteAllTextAsync(source, "source-content");
        var repository = new FileBackedProjectRepository();
        var service = new ProjectService(repository);
        await service.OpenAsync(source);

        var copyWithoutExtension = Path.Combine(_directory, "copy");
        var result = await service.SaveAsAsync(copyWithoutExtension);

        var expectedCopy = Path.Combine(_directory, "copy.jukuschedule");
        Assert.Equal(expectedCopy, result.Path);
        Assert.Equal(expectedCopy, service.Current?.Path);
        Assert.Equal("source-content", await File.ReadAllTextAsync(source));
        Assert.Equal("source-content", await File.ReadAllTextAsync(expectedCopy));
    }

    [Fact]
    public async Task SaveAsAsync_DoesNotOverwriteExistingDestination()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.jukuschedule");
        var destination = Path.Combine(_directory, "existing.jukuschedule");
        await File.WriteAllTextAsync(source, "source-content");
        await File.WriteAllTextAsync(destination, "keep-content");
        var service = new ProjectService(new FileBackedProjectRepository());
        await service.OpenAsync(source);

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(destination));

        Assert.Equal("keep-content", await File.ReadAllTextAsync(destination));
        Assert.Equal(source, service.Current?.Path);
    }

    [Fact]
    public async Task OpenAsync_TriggersAutomaticBackupWithFiveGenerations()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.jukuschedule");
        await File.WriteAllTextAsync(source, "source-content");
        var repository = new FileBackedProjectRepository();
        var service = new ProjectService(repository);

        await service.OpenAsync(source);

        Assert.Equal((source, ProjectService.DefaultBackupDirectory, 5), repository.LastAutomaticBackupCall);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FileBackedProjectRepository : IProjectRepository
    {
        public (string Path, string BackupDirectory, int MaxGenerations)? LastAutomaticBackupCall { get; private set; }

        public Task<ProjectSummary> CreateAsync(string path, CourseProjectDefinition definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProjectSummary> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSummary(path, "Test", 2026, CourseSeason.Summer, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), 0));

        public Task<ProjectIntegrityResult> CheckIntegrityAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateBackupAsync(string sourcePath, string backupPath, CancellationToken cancellationToken = default)
        {
            File.Copy(sourcePath, backupPath, overwrite: false);
            return Task.CompletedTask;
        }

        public Task RestoreBackupAsync(string backupPath, string targetPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateAutomaticBackupAsync(string path, string backupDirectory, int maxGenerations, CancellationToken cancellationToken = default)
        {
            LastAutomaticBackupCall = (path, backupDirectory, maxGenerations);
            return Task.CompletedTask;
        }
    }
}
