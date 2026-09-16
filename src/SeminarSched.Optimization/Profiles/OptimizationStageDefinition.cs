namespace SeminarSched.Optimization.Profiles;

public sealed record OptimizationStageDefinition
{
    public OptimizationStageDefinition(
        OptimizationStageKind kind,
        double budgetShare,
        int candidatesToAdvance,
        IReadOnlyList<OptimizationStrategyKind> strategies)
    {
        if (budgetShare <= 0 || budgetShare > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetShare));
        }

        if (candidatesToAdvance < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(candidatesToAdvance));
        }

        ArgumentNullException.ThrowIfNull(strategies);
        if (strategies.Count == 0)
        {
            throw new ArgumentException("At least one strategy is required.", nameof(strategies));
        }

        Kind = kind;
        BudgetShare = budgetShare;
        CandidatesToAdvance = candidatesToAdvance;
        Strategies = strategies;
    }

    public OptimizationStageKind Kind { get; }

    public double BudgetShare { get; }

    public int CandidatesToAdvance { get; }

    public IReadOnlyList<OptimizationStrategyKind> Strategies { get; }
}
