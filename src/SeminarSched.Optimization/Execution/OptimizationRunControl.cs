namespace SeminarSched.Optimization.Execution;

public sealed class OptimizationRunControl : IDisposable
{
    private readonly CancellationTokenSource _acceptBest = new();
    private TaskCompletionSource? _pauseGate;

    internal CancellationToken AcceptBestToken => _acceptBest.Token;

    public void AcceptCurrentBest() => _acceptBest.Cancel();

    // ユーザー要望「一時停止ボタンを作ってほしい」（checkpoint99）への対応。CP-SATの探索そのものは
    // pause/resumeできない（ネイティブ側の実行中の解探索を中断して後から続きから再開する手段は無い）
    // ため、「進捗を止めて待機」方式を採る: 次の戦略・次の延長パスを開始する直前の区切りでのみ
    // 一時停止を確認する（ScheduleOptimizer.RunAsync参照）。そのため、要求してから実際に止まる
    // までには、実行中の1戦略・1延長パス分のタイムラグがありうる（ユーザーに確認・了承済みの仕様）。
    public bool IsPauseRequested => _pauseGate is not null;

    /// <summary>一時停止が実際に効いて待機に入った／抜けた瞬間に発火する（要求した瞬間ではない）。
    /// UIが「一時停止を要求中（現在の探索が終わり次第、停止します）」と「一時停止中」を
    /// 区別して表示するために使う。</summary>
    public event Action<bool>? PausedChanged;

    public void RequestPause() => _pauseGate ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Resume()
    {
        var gate = _pauseGate;
        _pauseGate = null;
        gate?.TrySetResult();
    }

    internal async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (_pauseGate is not { } gate) return;
        PausedChanged?.Invoke(true);
        try
        {
            using var registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            await gate.Task.ConfigureAwait(false);
        }
        finally
        {
            PausedChanged?.Invoke(false);
        }
    }

    public void Dispose() => _acceptBest.Dispose();
}
