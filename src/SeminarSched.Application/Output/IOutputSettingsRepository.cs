using SeminarSched.Domain.Output;

namespace SeminarSched.Application.Output;

public interface IOutputSettingsRepository
{
    Task<OutputSettings> GetAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string projectPath, OutputSettings settings, CancellationToken cancellationToken = default);
}
