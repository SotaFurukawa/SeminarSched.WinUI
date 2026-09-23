namespace SeminarSched.Optimization.Execution;

public sealed record OptimizationRunResult<TSolution>(
    ScheduleCandidate<TSolution>? Best,
    IReadOnlyList<ScheduleCandidate<TSolution>> Candidates,
    int ImprovementCount,
    bool AcceptedEarly,
    bool StoppedForStagnation,
    TimeSpan Elapsed,
    /// <summary>名目時間を使い切っても未完成だったため、延長フェーズ（<see cref="OptimizationProgress.IsExtending"/>
    /// 参照）が実際に動いたかどうか。呼び出し側が完了メッセージに「時間がかかりましたが」等を
    /// 添える判断材料に使う。</summary>
    bool WasExtended = false);
