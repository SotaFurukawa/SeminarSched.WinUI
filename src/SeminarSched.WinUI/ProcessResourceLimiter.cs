using System.Diagnostics;

namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー報告「CPUにかなり負荷がかかってしまう」への対応。checkpoint88でnum_search_workersを
/// 論理コアの半分へ絞ったが実機で改善が足りず、checkpoint89でWindows Job ObjectのCPU rate control
/// （Docker Desktop等が使うのと同じ仕組み）を追加してプロセス全体のCPU使用率をハード制限した。
/// しかし後続の報告「CPUの制限をしたせいか、最高品質だと2時間かけても終了しなかった」を受けて
/// 調査した結果、HARD_CAPは他アプリが何もCPUを使っていなくても容赦なく上限で頭打ちにする方式
/// （「競合時だけ譲る」のではなく「常に頭打ち」）だと判明した。塾のPCで他の作業をしていない間も
/// 探索が上限で足止めされ続け、本来なら数十分で終わる探索が2時間の延長上限（
/// <see cref="SeminarSched.Optimization.Execution.ScheduleOptimizer"/>の「延長」機構、名目時間の
/// 最大2倍）を使い切っても終わらない、という直接の原因になっていた。
///
/// そのためJob Object HARD_CAPは撤去し、プロセス優先度を下げる方式（<see cref="Process.PriorityClass"/>
/// ＝<see cref="ProcessPriorityClass.BelowNormal"/>）へ置き換えた。優先度を下げるだけなら、他のアプリが
/// 実際にCPUを必要としている「競合時」にだけ道を譲り、PCがアイドルであれば探索は空いているCPU時間を
/// 遠慮なく使って通常速度で走る。「他の作業の重さを減らす」という元の目的は保ったまま、アイドル時間を
/// 無駄にする副作用（＝計算がPCの実性能に見合わず遅くなる）を取り除く狙い。
/// NumSearchWorkers側の絞り込み（<see cref="SeminarSched.Optimization.Core.CpSatScheduleSolver.ResolvedAutoSearchWorkers"/>）は
/// 論理コア数に応じて自動で軽くなる仕組みなので引き続き併用する。
/// </summary>
internal static class ProcessResourceLimiter
{
    /// <summary>⑤時間割自動作成の実行中だけ呼び出す。enabled=trueでBelowNormalへ下げ、falseでNormalへ
    /// 戻す。失敗しても（別のJob Objectのポリシーで優先度変更が拒否される環境等）アプリ本体の動作を
    /// 妨げてはならないため、例外は握りつぶしてログにだけ記録する。</summary>
    public static void SetCpuLimit(bool enabled)
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = enabled
                ? ProcessPriorityClass.BelowNormal
                : ProcessPriorityClass.Normal;
        }
        catch (Exception exception)
        {
            App.Logger.Warning($"ProcessResourceLimiter: process priority の変更に失敗しました ({exception.Message})。CPU使用率の抑制は今回のセッションでは適用されません。");
        }
    }
}
