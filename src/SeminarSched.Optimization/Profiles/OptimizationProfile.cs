namespace SeminarSched.Optimization.Profiles;

public sealed record OptimizationProfile
{
    public OptimizationProfile(
        OptimizationQualityLevel level,
        string displayName,
        string estimatedDuration,
        string userDescription,
        TimeSpan maximumDuration,
        TimeSpan stagnationTimeout,
        IReadOnlyList<OptimizationStageDefinition> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(estimatedDuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDescription);
        ArgumentNullException.ThrowIfNull(stages);

        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        if (stagnationTimeout <= TimeSpan.Zero || stagnationTimeout > maximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(stagnationTimeout));
        }

        if (stages.Count == 0)
        {
            throw new ArgumentException("At least one optimization stage is required.", nameof(stages));
        }

        var totalShare = stages.Sum(stage => stage.BudgetShare);
        if (Math.Abs(totalShare - 1.0) > 0.0001)
        {
            throw new ArgumentException("Stage budget shares must total 1.0.", nameof(stages));
        }

        Level = level;
        DisplayName = displayName;
        EstimatedDuration = estimatedDuration;
        UserDescription = userDescription;
        MaximumDuration = maximumDuration;
        StagnationTimeout = stagnationTimeout;
        Stages = stages;
    }

    public OptimizationQualityLevel Level { get; }

    public string DisplayName { get; }

    public string EstimatedDuration { get; }

    public string UserDescription { get; }

    public TimeSpan MaximumDuration { get; }

    public TimeSpan StagnationTimeout { get; }

    public IReadOnlyList<OptimizationStageDefinition> Stages { get; }

    public int StrategyCount => Stages.SelectMany(stage => stage.Strategies).Distinct().Count();

    public bool UsesTournament => Stages.Count > 1 && Stages[0].CandidatesToAdvance > 1;

    public bool UsesHints => Stages.SelectMany(stage => stage.Strategies)
        .Contains(OptimizationStrategyKind.HintImprovement);

    public bool UsesNeighborhoodRepair => Stages.SelectMany(stage => stage.Strategies)
        .Contains(OptimizationStrategyKind.NeighborhoodRepair);

    public bool UsesFinalPolishing => Stages.SelectMany(stage => stage.Strategies)
        .Contains(OptimizationStrategyKind.FinalPolishing);
}
