namespace SeminarSched.Optimization.Execution;

public sealed class OptimizationRunControl : IDisposable
{
    private readonly CancellationTokenSource _acceptBest = new();

    internal CancellationToken AcceptBestToken => _acceptBest.Token;

    public void AcceptCurrentBest() => _acceptBest.Cancel();

    public void Dispose() => _acceptBest.Dispose();
}
