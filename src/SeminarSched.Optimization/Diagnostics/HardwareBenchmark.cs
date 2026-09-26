using System.Diagnostics;
using SeminarSched.Optimization.Core;

namespace SeminarSched.Optimization.Diagnostics;

/// <summary>PCの実測性能の5段階区分。数値が大きいほど高性能。</summary>
public enum HardwareTier { VeryLow = 1, Low = 2, Standard = 3, High = 4, VeryHigh = 5 }

public sealed record HardwareBenchmarkResult(HardwareTier Tier, TimeSpan Elapsed, bool ReachedOptimal);

/// <summary>
/// ユーザー要望（checkpoint108）「品質プロファイルのリバランスを実装しましょうか。CPUのスコア表
/// みたいなのがあるはずなので、そのスコアを確認し（もしくはアプリ内のテストを自動的に動かしてみる
/// ことで）、そのスコアの閾値を設定することで、スコアを上回っていたら多めの負荷、下回っていたら
/// あまり負荷はかけないようにする。このスコアはアプリ内のテストをする場合、毎回やるものではなくて、
/// 初めて自動作成する際に、一度だけ調べることにする。こちらも5段階くらい用意しておいてください」
/// への対応。
///
/// 外部のCPUベンチマークデータベース（PassMark等）は、オフライン動作が前提のこのアプリからは
/// 参照できず、CPU名文字列との照合も型番表記の揺れで信頼性が低いため、ユーザー自身が代替案として
/// 挙げた「アプリ内のテストを自動的に動かす」方式を採用した。固定・決定的な合成スケジューリング
/// 問題（実データではなく、毎回同じ構造の架空の問題）を実際にCP-SATで解かせ、証明付き最適解
/// （Optimal）に到達するまでの実測時間で5段階のtierへ分類する。<see cref="SystemRequirements"/>
/// （CPU名・クロック数からの静的な推定）とは異なり、こちらは実際にCP-SATを動かした実測値である点が
/// 異なる（同じクロック数でも世代差・命令セットの違い等で実際の解探索速度は変わりうるため）。
///
/// 分類の閾値は、この開発機（16論理プロセッサ）での実測値（フル並列で約0.9〜1.0秒、2並列に絞ると
/// 約3.6秒、1並列（実質シングルコア相当）では約5.6秒）を基準に、そこから2倍刻みで前後に広げた
/// 相対値である。実際の様々なユーザー環境（特に「10年ほど前のノートPC」のような非力な機体）での
/// 検証はできていないため、将来ユーザーからの報告に応じて調整が必要になる可能性がある（過去の
/// CPU使用率調整（checkpoint88〜93）と同様、実測フィードバックに基づく反復調整を前提とした値）。
/// </summary>
public static class HardwareBenchmark
{
    /// <summary>この時間内にOptimalへ到達しなければ、これ以上待たずVeryLowと判定する。</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(20);

    private static readonly (TimeSpan Threshold, HardwareTier Tier)[] Thresholds =
    [
        (TimeSpan.FromSeconds(0.75), HardwareTier.VeryHigh),
        (TimeSpan.FromSeconds(1.5), HardwareTier.High),
        (TimeSpan.FromSeconds(3.0), HardwareTier.Standard),
        (TimeSpan.FromSeconds(6.0), HardwareTier.Low),
    ];

    /// <summary>合成問題を実際にCP-SATで解き、実測時間からtierを判定する。呼び出し元
    /// （<c>OptimizationRunState</c>）が、この機体で初めて⑤自動作成を実行するときにだけ1回呼び、
    /// 結果を<c>AppSettings</c>へ永続化して以降は再利用する想定。</summary>
    public static async Task<HardwareBenchmarkResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var problem = BuildSyntheticProblem();
        var options = new CpSatSolveOptions(TimeLimit, RandomSeed: 1, NumSearchWorkers: Environment.ProcessorCount);
        var stopwatch = Stopwatch.StartNew();
        var reachedOptimal = true;
        try
        {
            await new CpSatScheduleSolver().SolveAsync(problem, options, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // 制限時間内にOptimal/Feasibleへ到達できなかった（Unknown等）。VeryLow相当として扱う。
            reachedOptimal = false;
        }
        stopwatch.Stop();

        return Classify(stopwatch.Elapsed, reachedOptimal);
    }

    /// <summary>純粋な分類ロジックだけを切り出したもの（実際にCP-SATを走らせずにテストできるように）。</summary>
    public static HardwareBenchmarkResult Classify(TimeSpan elapsed, bool reachedOptimal)
    {
        if (!reachedOptimal) return new HardwareBenchmarkResult(HardwareTier.VeryLow, elapsed, false);
        foreach (var (threshold, tier) in Thresholds)
        {
            if (elapsed <= threshold) return new HardwareBenchmarkResult(tier, elapsed, true);
        }
        return new HardwareBenchmarkResult(HardwareTier.VeryLow, elapsed, true);
    }

    /// <summary>
    /// 固定・決定的な合成スケジューリング問題（実データは一切使わない）。24名の生徒がそれぞれ
    /// 2コマを必要とし、6名の講師のうち2名だけが指導可能（生徒ごとに固定の組み合わせ）という、
    /// 実際の運用でありがちな「講師の担当可能範囲が絞られている」状況を模した中程度の制約充足問題。
    /// 容量に対して需要は少なくないが、生徒の空きコマ回避（既定のハード制約）と講師容量制約が
    /// 絡み合うため、CP-SATにとって自明ではない探索量が必要になる。
    /// </summary>
    private static ScheduleProblem BuildSyntheticProblem()
    {
        const int studentCount = 40;
        const int teacherCount = 6;
        const int dayCount = 6;
        const int slotsPerDay = 4;

        var demands = new List<LessonDemand>();
        var candidates = new List<PlacementCandidate>();
        for (var s = 0; s < studentCount; s++)
        {
            var studentId = 1000 + s;
            var requestId = 1 + s;
            demands.Add(new LessonDemand(requestId, studentId, RequiredSessions: 2, AlreadyFixedSessions: 0));
            var teacherA = 2000 + s % teacherCount;
            var teacherB = 2000 + (s + 1) % teacherCount;
            for (var day = 0; day < dayCount; day++)
            {
                for (var slot = 0; slot < slotsPerDay; slot++)
                {
                    candidates.Add(new PlacementCandidate(requestId, studentId, teacherA, day, slot, day, slot));
                    candidates.Add(new PlacementCandidate(requestId, studentId, teacherB, day, slot, day, slot));
                }
            }
        }
        return new ScheduleProblem(demands, candidates);
    }
}
