using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Settings;

public sealed record AppSettings(OptimizationQualityLevel OptimizationQualityLevel)
{
    public static AppSettings Default { get; } = new(OptimizationProfileCatalog.DefaultLevel);
}
