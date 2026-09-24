using Microsoft.UI.Xaml;
using SeminarSched.Application.Scheduling;
using SeminarSched.Domain.Scheduling;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;
using WinRT.Interop;

namespace SeminarSched_WinUI;

internal sealed record OptimizationRunOutcome(bool Success, ScheduleRunSummary? Summary, string? ErrorMessage);

/// <summary>
/// ⑤時間割自動作成の実行状態をPageの外（アプリ全体）で保持する。WinUIのFrame.Navigateは画面遷移の
/// たびに新しいPageインスタンスを作るため、実行状態をOptimizationPageの中だけに置くと、実行中に別の
/// 画面へ切り替えて戻ってきたときに進捗表示が消えて「中断したように見える」（実際にはRunAsyncの
/// Task自体はページの外で継続している）。ScheduleUndoStateと同じ思想で、実行制御・進捗・直近の結果を
/// ここに集約し、OptimizationPage・ナビゲーションペインの常設インジケーター・タスクバー進捗表示の
/// すべてがこの1箇所を参照する。
/// </summary>
internal static class OptimizationRunState
{
    public static event Action? Changed;

    public static bool IsRunning { get; private set; }
    public static OptimizationProgress? LatestProgress { get; private set; }
    public static TimeSpan MaximumDuration { get; private set; }
    public static OptimizationRunOutcome? LastOutcome { get; private set; }

    // ユーザー要望「一時停止ボタンを作ってほしい」（checkpoint99）への対応。IsPauseRequestedは
    // ボタンを押した瞬間から真になる（すぐ「再開」へ切り替えられるように）。IsPausedは実際に
    // 探索が止まって待機に入った瞬間（OptimizationRunControl.PausedChanged）から真になり、その間に
    // 実行中の1戦略・1延長パス分のタイムラグがありうる（画面側はこの2つを区別して案内文を出す）。
    public static bool IsPauseRequested { get; private set; }
    public static bool IsPaused { get; private set; }

    private static OptimizationRunControl? _control;
    private static TimeSpan _lastReportedElapsed;
    private static DateTime _lastReportedAtUtc;
    private static double _lastCompletedWeight;
    private static TimeSpan _lastCompletedElapsed;
    private static double _displayedPercent;
    private static DispatcherTimer? _timer;

    // progress?.Report()はストラテジーの開始・終了時にしか呼ばれず、1ストラテジーが数十秒〜数分
    // 続くことがあるため、報告の間だけ経過時間を毎秒補間して滑らかに見せる（経過時間の表示自体は
    // 引き続き実時間ベース）。一方、パーセンテージは経過時間とMaximumDuration（品質レベルの
    // 名目上の上限、最高品質なら3600秒）の比ではなく、OptimizationProgress.ProgressWeight
    // （計画済みの全戦略のうち、実際に完了・進行中の割合）を基準にする。CP-SATは対象規模によっては
    // 名目上の持ち時間をほとんど使わずに最適解を証明して終わることが多く、経過時間ベースだと
    // 数%のまま止まって見えたあと、終了した瞬間に100%へ飛ぶ（実際にユーザー報告のあった症状）。
    // 進捗ベースなら、戦略の完了に応じて滑らかかつ正確に100%へ近づく。
    //
    // 残り目安も、同じ理由で「MaximumDuration-経過時間」は使わない（ユーザー指摘: 名目上限までの
    // 残りを出すと、実際にはずっと早く終わるのに大きな残り時間が表示され続けて誤解を招く）。
    // 代わりに、直近に完了した戦略までの「実際に経過した時間 ÷ そこまでの進捗の割合」を実測ペースと
    // みなし、残り作業量をそのペースで割って外挿する。まだ1つも戦略が完了していない最初の数秒は
    // 外挿の元になる実測データが無いため、残り時間はnull（＝「計算中」）を返す。
    //
    // ユーザー指摘: 「戦略が一個終わったら一気にぎゅんと移動してしまう」。原因は、1戦略に割り当てた
    // 時間（StrategyBudget）に対する経過時間の比で滑らかに補間しているが、CP-SATは証明済み最適解に
    // 早期到達して名目の持ち時間よりずっと早く終わることが多く、割り当て時間のごく一部しか経過して
    // いない（＝補間後の%もわずかしか進んでいない）うちに戦略が完了し、そこで一気に「その戦略の
    // 満額」へ切り替わるため。EstimateRaw()の値（＝真の目標値）へ瞬時に飛びつくのではなく、
    // 公開APIであるEstimate()が返す値（_displayedPercent）を毎tickごとに少しずつ追いつかせる
    // ことで、この段差を視覚的になめらかにする（AdvanceDisplayedPercent参照）。
    public static (double Percent, TimeSpan Elapsed, TimeSpan? Remaining) Estimate()
    {
        var raw = EstimateRaw();
        if (!IsRunning)
        {
            _displayedPercent = 100;
            return raw;
        }
        return (_displayedPercent, raw.Elapsed, raw.Remaining);
    }

    private static (double Percent, TimeSpan Elapsed, TimeSpan? Remaining) EstimateRaw()
    {
        if (LatestProgress is null) return (0, TimeSpan.Zero, null);
        // 停滞検知や「中断して現在の結果を採用」で持ち時間を使い切る前に終了することがあり、その場合
        // 進捗の比率は100%未満のまま止まって見える。実行が終わっている（成功・失敗問わず）
        // 時点で、ユーザーから見れば「もう終わった」ので100%・残り0として表示する。
        if (!IsRunning) return (100, _lastReportedElapsed, TimeSpan.Zero);
        // 一時停止中（checkpoint99）は経過時間・パーセンテージとも動かさず、一時停止した瞬間の値で
        // 固定表示する（DateTime.UtcNowをそのまま使うと、止まっているはずの間も経過時間が増え続け、
        // ストラテジー内挿の分だけパーセンテージも動いてしまう）。
        var elapsed = IsPaused ? _lastReportedElapsed : _lastReportedElapsed + (DateTime.UtcNow - _lastReportedAtUtc);

        var progress = LatestProgress;
        double percent;
        if (IsPaused)
        {
            percent = progress.ProgressWeight * 100.0;
        }
        else if (progress.IsStrategyStarting)
        {
            var strategyElapsed = DateTime.UtcNow - _lastReportedAtUtc;
            var fraction = progress.StrategyBudget.TotalSeconds <= 0
                ? 0
                : Math.Clamp(strategyElapsed.TotalSeconds / progress.StrategyBudget.TotalSeconds, 0, 1);
            percent = (progress.ProgressWeight + fraction * progress.StrategyWeight) * 100.0;
        }
        else
        {
            percent = progress.ProgressWeight * 100.0;
        }
        percent = Math.Clamp(percent, 0, 100);

        TimeSpan? remaining = null;
        if (_lastCompletedWeight > 0.0001 && _lastCompletedElapsed > TimeSpan.Zero)
        {
            var observedWeightPerSecond = _lastCompletedWeight / _lastCompletedElapsed.TotalSeconds;
            var remainingWeight = Math.Max(0, 1.0 - percent / 100.0);
            remaining = observedWeightPerSecond > 0
                ? TimeSpan.FromSeconds(remainingWeight / observedWeightPerSecond)
                : TimeSpan.Zero;
        }
        return (percent, elapsed, remaining);
    }

    public static async Task StartAsync(string projectPath, OptimizationProfile profile, bool unrestrictedResourceUsage = false, SchedulingPolicy? policyOverride = null)
    {
        if (IsRunning) throw new InvalidOperationException("既に時間割自動作成が実行中です。");
        var beforeRun = await App.ScheduleEditor.CaptureSnapshotAsync(projectPath);
        ScheduleUndoState.Push(beforeRun);
        ScheduleUndoState.ReoptimizationBaseline = beforeRun;
        _control = new OptimizationRunControl();
        _control.PausedChanged += OnPausedChanged;
        MaximumDuration = profile.MaximumDuration;
        LatestProgress = null;
        _lastReportedElapsed = TimeSpan.Zero;
        _lastReportedAtUtc = DateTime.UtcNow;
        _lastCompletedWeight = 0;
        _lastCompletedElapsed = TimeSpan.Zero;
        _displayedPercent = 0;
        LastOutcome = null;
        IsPauseRequested = false;
        IsPaused = false;
        IsRunning = true;
        // ユーザー報告「CPUにかなり負荷がかかってしまう」への対応。既定ではプロセス優先度を下げ
        // （ProcessResourceLimiter、他アプリと競合したときだけ譲る方式）、CP-SATの並列探索ワーカー数も
        // 論理コアの半分に制限する（SeminarSched.Optimization.Core.CpSatScheduleSolver.WorkerLimitEnabled）。
        // どちらも「制限しない」チェックボックスがオンの間だけ両方まとめて解除する。
        // （以前はOS側のJob Object CPU rate controlでハード制限していたが、アイドル時間まで無駄に
        // 頭打ちにしてしまい「最高品質が2時間かけても終わらない」の直接原因になったため撤去した。）
        SeminarSched.Optimization.Core.CpSatScheduleSolver.WorkerLimitEnabled = !unrestrictedResourceUsage;
        ProcessResourceLimiter.SetCpuLimit(enabled: !unrestrictedResourceUsage);
        _timer ??= CreateTimer();
        _timer.Start();
        Changed?.Invoke();
        _ = RunCoreAsync(projectPath, profile, policyOverride);
    }

    public static void AcceptCurrentBest() => _control?.AcceptCurrentBest();

    public static void RequestPause()
    {
        _control?.RequestPause();
        IsPauseRequested = true;
        Changed?.Invoke();
    }

    public static void ResumeFromPause()
    {
        _control?.Resume();
        IsPauseRequested = false;
        Changed?.Invoke();
    }

    // OptimizationRunControl.PausedChangedは、ScheduleOptimizer.RunAsync内部（ConfigureAwait(false)で
    // 実行されるバックグラウンドスレッド）から直接発火する。progress?.Report()経由の更新はSystem.
    // Progress<T>が生成時に捕捉したSynchronizationContext（UIスレッド）へ自動的にマーシャリングされる
    // が、このイベントは素のAction<bool>のためその恩恵が無い。UIスレッド側のDispatcherTimerが同じ
    // フィールドを読むため、必ずDispatcherQueueへ積んでからフィールドを触る。
    private static void OnPausedChanged(bool isPaused) =>
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            IsPaused = isPaused;
            // 実際に止まった／再開した瞬間に、経過時間の補間基準を今この瞬間へ合わせ直す。合わせないと、
            // 一時停止中に進んでいない実時間分が「経過時間」の表示へそのまま加算され続けてしまう
            // （EstimateRaw()の_lastReportedAtUtcからの経過時間の補間はIsRunningの間ずっと動き続けるため）。
            _lastReportedElapsed += DateTime.UtcNow - _lastReportedAtUtc;
            _lastReportedAtUtc = DateTime.UtcNow;
            Changed?.Invoke();
        });

    public static OptimizationRunOutcome? ConsumeLastOutcome()
    {
        var outcome = LastOutcome;
        LastOutcome = null;
        return outcome;
    }

    private static DispatcherTimer CreateTimer()
    {
        // 200ms間隔（1秒間隔だと、ゲージを少しずつ追いつかせる演出が5コマ/秒しか出ず、かえって
        // カクついて見える）。CPU負荷への影響は、この間隔で行うのは軽量な数値計算とUI再描画要求
        // だけなので無視できる（重いのはCP-SATの探索スレッド側）。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (!IsRunning) return;
            AdvanceDisplayedPercent();
            Changed?.Invoke();
            if (App.MainWindow is { } window) TaskbarProgress.SetRunning(WindowNative.GetWindowHandle(window), _displayedPercent);
        };
        return timer;
    }

    /// <summary>表示用パーセンテージを、真の目標値（EstimateRaw().Percent）へ毎tickごとに20%ずつ
    /// 追いつかせる（指数緩和）。目標値は戦略完了のたびに段差を持って進むが（1戦略が名目の持ち時間
    /// よりずっと早く終わると、その段差は大きくなりうる）、表示側はこの緩和により数tick（1秒未満）
    /// かけて滑らかに追いつくため、瞬間的な「ぎゅん」という飛びが視覚的になくなる。目標値自体は
    /// 単調増加のみなので、追いつく方向は常に前進（後退）はしない。</summary>
    private static void AdvanceDisplayedPercent()
    {
        var target = EstimateRaw().Percent;
        _displayedPercent = Math.Abs(target - _displayedPercent) < 0.15
            ? target
            : _displayedPercent + (target - _displayedPercent) * 0.2;
    }

    private static async Task RunCoreAsync(string projectPath, OptimizationProfile profile, SchedulingPolicy? policyOverride)
    {
        var progress = new Progress<OptimizationProgress>(p =>
        {
            LatestProgress = p;
            _lastReportedElapsed = p.Elapsed;
            _lastReportedAtUtc = DateTime.UtcNow;
            if (p.IsExtending && p.IsStrategyStarting)
            {
                // 延長フェーズ開始時点（checkpoint96で目盛りの単位自体は通常ステージと統一済み、
                // ScheduleOptimizer.ExtensionReservedShare参照）。それでも延長前の実測ペース
                // （weight/秒）をそのまま使わない: 通常ステージはCP-SATが早期に証明済み最適解へ
                // 到達しがちで速いペースになりやすい一方、延長（grinding）は持ち時間いっぱいまで
                // 試行を粘り続けるため大幅に遅いペースになるのが通常で、そのまま外挿すると残り時間を
                // 実際より大幅に少なく見積もってしまう。延長中は次の完了報告（＝延長自体が終わる時）
                // までデータが無いため、素直に「計算中…」を表示する（不正確な数字を出すより誠実）。
                _lastCompletedWeight = 0;
                _lastCompletedElapsed = TimeSpan.Zero;
            }
            // 「戦略が完了した」報告だけを実測ペースの基準にする（開始直後の報告はまだ何も終わって
            // いないため、外挿の元にすると常にMaximumDurationへ収束してしまい元の木阿弥になる）。
            else if (!p.IsStrategyStarting && p.Elapsed > TimeSpan.Zero)
            {
                _lastCompletedWeight = p.ProgressWeight;
                _lastCompletedElapsed = p.Elapsed;
            }
            Changed?.Invoke();
        });
        try
        {
            var result = await App.ScheduleRun.RunAsync(projectPath, profile, _control!, progress, policyOverride: policyOverride);
            LastOutcome = new OptimizationRunOutcome(true, result, null);
            App.Logger.Info($"Schedule run completed: placed={result.PlacedLessons} unassigned={result.UnassignedLessons} elapsedSec={result.Elapsed.TotalSeconds:F1} strategy={result.StrategyLabel}");
        }
        // ユーザー報告「途中で強制終了されてしまう」の調査で判明: RunCoreAsyncはStartAsyncから
        // '_ = RunCoreAsync(...)'（await無し）で起動されるため、ここで捕まえ損ねた例外はどこにも
        // 表示されず、実行中の表示がただ消えるだけの「原因不明の強制終了」に見えてしまう
        // （.NET自体は既定でプロセスを落とさないが、ユーザーからは成功・失敗どちらの通知も出ない
        // まま止まって見える）。以前は3種類の例外だけを対象にしていたが、原因を問わず必ず何らかの
        // 結果（エラーメッセージ）を表示できるよう、対象をすべての例外へ広げた。
        catch (Exception ex)
        {
            if (ScheduleUndoState.UndoStack.Count > 0) ScheduleUndoState.UndoStack.Pop();
            ScheduleUndoState.ReoptimizationBaseline = null;
            LastOutcome = new OptimizationRunOutcome(false, null, ex.Message);
            App.Logger.Error("Schedule run failed", ex);
        }
        finally
        {
            IsRunning = false;
            IsPauseRequested = false;
            IsPaused = false;
            _timer?.Stop();
            if (_control is not null) _control.PausedChanged -= OnPausedChanged;
            _control?.Dispose();
            _control = null;
            // 実行専用のCPU制限は、実行が終わったら解除する（帳票出力など他の操作まで巻き込んで
            // 遅くしないため）。次回実行時にチェックボックスの状態へ応じて改めて設定される。
            SeminarSched.Optimization.Core.CpSatScheduleSolver.WorkerLimitEnabled = true;
            ProcessResourceLimiter.SetCpuLimit(enabled: false);
            if (App.MainWindow is { } window)
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                if (LastOutcome?.Success == true) TaskbarProgress.NotifyCompleted(hwnd);
                else TaskbarProgress.Clear(hwnd);
            }
            Changed?.Invoke();
        }
    }
}
