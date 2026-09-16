using SeminarSched.Domain.Projects;

namespace SeminarSched.Application.Projects;

public sealed class ProjectService
{
    public const string ProjectExtension = ".jukuschedule";

    private readonly IProjectRepository _repository;

    public ProjectService(IProjectRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public ProjectSummary? Current { get; private set; }

    public async Task<ProjectSummary> CreateAsync(
        string path,
        CourseProjectDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var normalized = NormalizeProjectPath(path);
        if (File.Exists(normalized))
        {
            throw new IOException("同名のプロジェクトが既に存在します。上書きは行いません。");
        }

        Current = await _repository.CreateAsync(normalized, definition, cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public async Task<ProjectSummary> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProjectPath(path);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException("プロジェクトファイルが見つかりません。", normalized);
        }

        Current = await _repository.OpenAsync(normalized, cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public void Close() => Current = null;

    public async Task<string> CreateBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
        var normalized = NormalizeProjectPath(backupPath);
        if (File.Exists(normalized))
        {
            throw new IOException("同名のバックアップが既に存在します。上書きは行いません。");
        }

        await _repository.CreateBackupAsync(current.Path, normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    public async Task<ProjectSummary> RestoreBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("復元先のプロジェクトが開かれていません。");
        var source = NormalizeProjectPath(backupPath);
        await _repository.RestoreBackupAsync(source, current.Path, cancellationToken).ConfigureAwait(false);
        Current = await _repository.OpenAsync(current.Path, cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public async Task<ProjectSummary> SaveAsAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("プロジェクトが開かれていません。");
        var target = NormalizeProjectPath(destinationPath);
        if (File.Exists(target))
        {
            throw new IOException("同名のプロジェクトが既に存在します。上書きは行いません。");
        }

        await _repository.CreateBackupAsync(current.Path, target, cancellationToken).ConfigureAwait(false);
        Current = await _repository.OpenAsync(target, cancellationToken).ConfigureAwait(false);
        return Current;
    }

    public static string NormalizeProjectPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path.Trim());
        return string.Equals(Path.GetExtension(fullPath), ProjectExtension, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.ChangeExtension(fullPath, ProjectExtension);
    }
}
