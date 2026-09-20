using Microsoft.UI.Xaml;
using SeminarSched.Application.Scheduling;
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

    private static OptimizationRunControl? _control;
    private static TimeSpan _lastReportedElapsed;
    private static DateTime _lastReportedAtUtc;
    private static DispatcherTimer? _timer;

    // progress?.Report()はストラテジーの開始・終了時にしか呼ばれず、1ストラテジーが数十秒〜数分
    // 続くことがあるため、報告の間だけ経過時間を毎秒補間して滑らかに見せる（経過時間の表示自体は
    // 引き続き実時間ベース）。一方、パーセンテージは経過時間とMaximumDuration（品質レベルの
    // 名目上の上限、最高品質なら3600秒）の比ではなく、OptimizationProgress.ProgressWeight
    // （計画済みの全戦略のうち、実際に完了・進行中の割合）を基準にする。CP-SATは対象規模によっては
    // 名目上の持ち時間をほとんど使わずに最適解を証明して終わることが多く、経過時間ベースだと
    // 数%のまま止まって見えたあと、終了した瞬間に100%へ飛ぶ（実際にユーザー報告のあった症状）。
    // 進捗ベースなら、戦略の完了に応じて滑らかかつ正確に100%へ近づく。
    public static (double Percent, TimeSpan Elapsed, TimeSpan Remaining) Estimate()
    {
        if (LatestProgress is null) return (0, TimeSpan.Zero, MaximumDuration);
        // 停滞検知や「中断して現在の結果を採用」で持ち時間を使い切る前に終了することがあり、その場合
        // 進捗の比率は100%未満のまま止まって見える。実行が終わっている（成功・失敗問わず）
        // 時点で、ユーザーから見れば「もう終わった」ので100%・残り0として表示する。
        if (!IsRunning) return (100, _lastReportedElapsed, TimeSpan.Zero);
        var elapsed = _lastReportedElapsed + (DateTime.UtcNow - _lastReportedAtUtc);
        var remaining = MaximumDuration - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var progress = LatestProgress;
        double percent;
        if (progress.IsStrategyStarting)
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
        return (Math.Clamp(percent, 0, 100), elapsed, remaining);
    }

    public static async Task StartAsync(string projectPath, OptimizationProfile profile)
    {
        if (IsRunning) throw new InvalidOperationException("既に時間割自動作成が実行中です。");
        var beforeRun = await App.ScheduleEditor.CaptureSnapshotAsync(projectPath);
        ScheduleUndoState.Push(beforeRun);
        ScheduleUndoState.ReoptimizationBaseline = beforeRun;
        _control = new OptimizationRunControl();
        MaximumDuration = profile.MaximumDuration;
        LatestProgress = null;
        _lastReportedElapsed = TimeSpan.Zero;
        _lastReportedAtUtc = DateTime.UtcNow;
        LastOutcome = null;
        IsRunning = true;
        _timer ??= CreateTimer();
        _timer.Start();
        Changed?.Invoke();
        _ = RunCoreAsync(projectPath, profile);
    }

    public static void AcceptCurrentBest() => _control?.AcceptCurrentBest();

    public static OptimizationRunOutcome? ConsumeLastOutcome()
    {
        var outcome = LastOutcome;
        LastOutcome = null;
        return outcome;
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (!IsRunning) return;
            Changed?.Invoke();
            if (App.MainWindow is { } window) TaskbarProgress.SetRunning(WindowNative.GetWindowHandle(window), Estimate().Percent);
        };
        return timer;
    }

    private static async Task RunCoreAsync(string projectPath, OptimizationProfile profile)
    {
        var progress = new Progress<OptimizationProgress>(p =>
        {
            LatestProgress = p;
            _lastReportedElapsed = p.Elapsed;
            _lastReportedAtUtc = DateTime.UtcNow;
            Changed?.Invoke();
        });
        try
        {
            var result = await App.ScheduleRun.RunAsync(projectPath, profile, _control!, progress);
            LastOutcome = new OptimizationRunOutcome(true, result, null);
            App.Logger.Info($"Schedule run completed: placed={result.PlacedLessons} unassigned={result.UnassignedLessons} elapsedSec={result.Elapsed.TotalSeconds:F1} strategy={result.StrategyLabel}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (ScheduleUndoState.UndoStack.Count > 0) ScheduleUndoState.UndoStack.Pop();
            ScheduleUndoState.ReoptimizationBaseline = null;
            LastOutcome = new OptimizationRunOutcome(false, null, ex.Message);
            App.Logger.Error("Schedule run failed", ex);
        }
        finally
        {
            IsRunning = false;
            _timer?.Stop();
            _control?.Dispose();
            _control = null;
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
