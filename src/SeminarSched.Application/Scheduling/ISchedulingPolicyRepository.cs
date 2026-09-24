using SeminarSched.Domain.Scheduling;

namespace SeminarSched.Application.Scheduling;

public interface ISchedulingPolicyRepository
{
    Task<SchedulingPolicy> GetAsync(string projectPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string projectPath, SchedulingPolicy policy, CancellationToken cancellationToken = default);
}
