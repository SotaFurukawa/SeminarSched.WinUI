using SeminarSched.Optimization.Evaluation;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Optimization.Tests;

public sealed class ScheduleOptimizerTests
{
    [Fact]
    public async Task RunAsync_SelectsBestCandidateUsingCommonLexicographicEvaluation()
    {
        var strategies = new[]
        {
            Strategy(OptimizationStrategyKind.StandardCpSat, Candidate("higher objective", hard: 0, unassigned: 0, objective: 100)),
            Strategy(OptimizationStrategyKind.SeededCpSatA, Candidate("unassigned", hard: 0, unassigned: 1, objective: 1)),
            Strategy(OptimizationStrategyKind.AlternateDecision, Candidate("best", hard: 0, unassigned: 0, objective: 50)),
        };
        var optimizer = new ScheduleOptimizer<string, string>(strategies);
        using var control = new OptimizationRunControl();

        var result = await optimizer.RunAsync("input", OptimizationProfileCatalog.Get(OptimizationQualityLevel.Fast), control);

        Assert.Equal("best", result.Best?.Solution);
        Assert.Equal(2, result.ImprovementCount);
        Assert.False(result.AcceptedEarly);
    }

    [Fact]
    public async Task RunAsync_AcceptCurrentBestStopsWorkWithoutBehavingLikeCancel()
    {
        var first = Strategy(OptimizationStrategyKind.StandardCpSat, Candidate("usable", 0, 0, 10));
        var blocking = new BlockingStrategy(OptimizationStrategyKind.SeededCpSatA);
        var final = Strategy(OptimizationStrategyKind.AlternateDecision, Candidate("not reached", 0, 0, 0));
        var optimizer = new ScheduleOptimizer<string, string>([first, blocking, final]);
        using var control = new OptimizationRunControl();

        var run = optimizer.RunAsync("input", OptimizationProfileCatalog.Get(OptimizationQualityLevel.Fast), control);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        control.AcceptCurrentBest();
        var result = await run;

        Assert.True(result.AcceptedEarly);
        Assert.Equal("usable", result.Best?.Solution);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task RunAsync_UserCancellationDoesNotReturnPartialResult()
    {
        var blocking = new BlockingStrategy(OptimizationStrategyKind.StandardCpSat);
        var optimizer = new ScheduleOptimizer<string, string>(
            [blocking,
             Strategy(OptimizationStrategyKind.SeededCpSatA, null),
             Strategy(OptimizationStrategyKind.AlternateDecision, null)]);
        using var control = new OptimizationRunControl();
        using var cancellation = new CancellationTokenSource();

        var run = optimizer.RunAsync(
            "input", OptimizationProfileCatalog.Get(OptimizationQualityLevel.Fast), control,
            cancellationToken: cancellation.Token);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private static FakeStrategy Strategy(OptimizationStrategyKind kind, ScheduleCandidate<string>? candidate) =>
        new(kind, candidate);

    private static ScheduleCandidate<string> Candidate(string solution, int hard, int unassigned, long objective) =>
        new(solution, new ScheduleEvaluation(hard, unassigned, 0, 0, 0, 0, 0, objective),
            OptimizationStrategyKind.StandardCpSat, TimeSpan.Zero);

    private sealed class FakeStrategy(
        OptimizationStrategyKind kind,
        ScheduleCandidate<string>? candidate) : IScheduleStrategy<string, string>
    {
        public OptimizationStrategyKind Kind => kind;
        public Task<ScheduleCandidate<string>?> ExecuteAsync(
            StrategyContext<string, string> context, CancellationToken cancellationToken) => Task.FromResult(candidate);
    }

    private sealed class BlockingStrategy(OptimizationStrategyKind kind) : IScheduleStrategy<string, string>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OptimizationStrategyKind Kind => kind;

        public async Task<ScheduleCandidate<string>?> ExecuteAsync(
            StrategyContext<string, string> context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
