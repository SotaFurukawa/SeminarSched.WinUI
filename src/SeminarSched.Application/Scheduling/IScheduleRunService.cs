using SeminarSched.Domain.Scheduling;
using SeminarSched.Optimization.Execution;
using SeminarSched.Optimization.Profiles;

namespace SeminarSched.Application.Scheduling;

/// <summary>
/// <see cref="UnassignedDueToRegularTeacherPriority"/> and <see cref="UnassignedWithNoQualifiedTeacher"/>
/// are diagnostic subsets of <see cref="UnassignedLessons"/> (see
/// SqliteScheduleRunService.DiagnoseUnassignedDemands), not additional counts on top of it - every
/// unassigned session falls into at most one of these two buckets, or into neither (a generic "lost the
/// competition for a scarce slot within the given time" case that has no single attributable cause).
/// </summary>
public sealed record ScheduleRunSummary(
    int PlacedLessons,
    int UnassignedLessons,
    TimeSpan Elapsed,
    string StrategyLabel = "",
    bool WasExtended = false,
    int UnassignedDueToRegularTeacherPriority = 0,
    int UnassignedWithNoQualifiedTeacher = 0);

public interface IScheduleRunService
{
    Task<ScheduleRunSummary> RunAsync(string projectPath, TimeSpan maximumDuration, CancellationToken cancellationToken = default);

    // policyOverride: 実行時だけの方針上書き（ユーザー要望「両方（プロジェクトの既定値＋実行時に
    // 上書き可）」）。nullならプロジェクトに保存済みの方針（未保存ならSchedulingPolicy.Default）を
    // そのまま使う。省略時の互換のため引数リストの末尾に追加してある。
    // keepExistingPlacements: ユーザー要望（checkpoint151）「既に配置したものを動かさないように
    // するか、つまり未配置のみを操作するようにするか」。trueの間は、ロック・手動配置済みの行だけで
    // なく、既に配置済みの行（前回までの自動作成結果を含む）もすべて固定扱いにし、ソルバーは
    // まだ配置されていない受講希望だけを対象に動かす。
    Task<ScheduleRunSummary> RunAsync(
        string projectPath,
        OptimizationProfile profile,
        OptimizationRunControl control,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        SchedulingPolicy? policyOverride = null,
        bool keepExistingPlacements = false);
}
