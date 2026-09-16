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

    public static string NormalizeProjectPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path.Trim());
        return string.Equals(Path.GetExtension(fullPath), ProjectExtension, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.ChangeExtension(fullPath, ProjectExtension);
    }
}
