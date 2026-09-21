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

        // A profile referencing all three fakes directly (see the comment in
        // RunAsync_AcceptCurrentBestStopsWorkWithoutBehavingLikeCancel below for why this test
        // should not depend on a real quality level's exact strategy list).
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1,
                [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA, OptimizationStrategyKind.AlternateDecision])]);

        var result = await optimizer.RunAsync("input", profile, control);

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

        // A profile built just for this test, rather than OptimizationProfileCatalog.Get(...): this
        // test is exercising the generic engine's accept-early behavior with three sequential fake
        // strategies, not any particular catalog tuning, and pinning it to a real quality level's
        // exact strategy list makes it fragile against future catalog retuning (as happened once
        // already - Fast dropped SeededCpSatA when its time-per-strategy budget proved too short
        // for real data).
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1,
                [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA, OptimizationStrategyKind.AlternateDecision])]);

        var run = optimizer.RunAsync("input", profile, control);
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
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1,
                [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA, OptimizationStrategyKind.AlternateDecision])]);

        var run = optimizer.RunAsync(
            "input", profile, control,
            cancellationToken: cancellation.Token);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    // ユーザー報告: 最高品質(名目60分)でも数分で終わることが多く、経過時間/MaximumDurationで
    // パーセンテージを出すと数%のまま止まって終了の瞬間に100%へ飛ぶ。ProgressWeightは
    // 各ステージのBudgetShareを戦略数で均等割りした「計画済み作業の割合」を積み上げたもので
    // あるべきで、経過時間には依存しない。2ステージ（0.6を2戦略・0.4を1戦略）で、
    // 期待される重み0→0.3→0.3→0.6→0.6→1.0の順に報告されることを検証する。
    [Fact]
    public async Task RunAsync_ReportsProgressWeightFromStageBudgetSharesNotElapsedTime()
    {
        var strategies = new[]
        {
            Strategy(OptimizationStrategyKind.StandardCpSat, Candidate("a", 0, 0, 1)),
            Strategy(OptimizationStrategyKind.SeededCpSatA, Candidate("b", 0, 0, 2)),
            Strategy(OptimizationStrategyKind.AlternateDecision, Candidate("c", 0, 0, 3)),
        };
        var optimizer = new ScheduleOptimizer<string, string>(strategies);
        using var control = new OptimizationRunControl();
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            [
                new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 0.6, 1,
                    [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA]),
                new OptimizationStageDefinition(OptimizationStageKind.CandidateAdvancement, 0.4, 1,
                    [OptimizationStrategyKind.AlternateDecision]),
            ]);

        // System.Progress<T> marshals Report() through a captured SynchronizationContext, which
        // queues to the thread pool asynchronously when there is none current (as in a plain xUnit
        // test) - not guaranteed to have run by the time RunAsync's Task completes. A trivial
        // synchronous IProgress<T> avoids that timing ambiguity entirely.
        var reports = new List<OptimizationProgress>();
        await optimizer.RunAsync("input", profile, control, new SynchronousProgress<OptimizationProgress>(reports.Add));

        var weights = reports.Select(r => Math.Round(r.ProgressWeight, 3)).ToArray();
        Assert.Equal(new[] { 0.0, 0.3, 0.3, 0.6, 0.6, 1.0 }, weights);
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    // ScheduleOptimizerの以前の実装は、previousBestを「ステージ完了時点までのadvancing」だけから
    // 選んでいたため、同じステージ内で複数の戦略を並べても、2つ目以降は1つ目の改善結果を
    // 一切hintとして受け取れなかった（ステージ内で得た改善が次の戦略に伝播しない）。
    // stageCandidatesも見るよう修正したことを、2戦略構成のステージで検証する。
    [Fact]
    public async Task RunAsync_LaterStrategyInSameStageReceivesEarlierStrategysImprovementAsHint()
    {
        var improved = Candidate("improved", 0, 0, 100);
        var first = Strategy(OptimizationStrategyKind.StandardCpSat, improved);
        var second = new HintCapturingStrategy(OptimizationStrategyKind.SeededCpSatA);
        var optimizer = new ScheduleOptimizer<string, string>([first, second]);
        using var control = new OptimizationRunControl();
        var profile = new OptimizationProfile(
            OptimizationQualityLevel.Fast, "test", "test", "test",
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
            [new OptimizationStageDefinition(OptimizationStageKind.InitialExploration, 1.0, 1,
                [OptimizationStrategyKind.StandardCpSat, OptimizationStrategyKind.SeededCpSatA])]);

        await optimizer.RunAsync("input", profile, control);

        Assert.Equal(improved, second.ReceivedHint);
    }

    private sealed class HintCapturingStrategy(OptimizationStrategyKind kind) : IScheduleStrategy<string, string>
    {
        public ScheduleCandidate<string>? ReceivedHint { get; private set; }
        public OptimizationStrategyKind Kind => kind;
        public Task<ScheduleCandidate<string>?> ExecuteAsync(StrategyContext<string, string> context, CancellationToken cancellationToken)
        {
            ReceivedHint = context.Hint;
            return Task.FromResult<ScheduleCandidate<string>?>(null);
        }
    }

    private static FakeStrategy Strategy(OptimizationStrategyKind kind, ScheduleCandidate<string>? candidate) =>
        new(kind, candidate);

    private static ScheduleCandidate<string> Candidate(string solution, int hard, int unassigned, long objective) =>
        new(solution, new ScheduleEvaluation(hard, unassigned, 0, 0, 0, 0, 0, 0, objective),
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
