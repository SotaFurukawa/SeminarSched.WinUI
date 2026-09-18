namespace SeminarSched.Application.Scheduling;

public sealed record ScheduleAssignmentItem(
    long Id,
    long LessonRequestId,
    long TeacherId,
    long OpenDateId,
    long TimeSlotId,
    bool IsLocked,
    bool IsManual,
    string Source,
    string Label)
{
    public override string ToString() => $"{Label}　[{(IsManual ? "手動" : "自動")}{(IsLocked ? "・ロック" : "")}]";
}

public sealed record OpenDateOption(long Id, string Label);

public sealed record BoardSlotRow(long TimeSlotId, string Label, int SortOrder);

public sealed record BoardTeacherColumn(long TeacherId, string Label);

public sealed record BoardCard(long AssignmentId, long TeacherId, long TimeSlotId, long StudentId, string StudentLabel, string SubjectLabel, bool IsManual, bool IsLocked, bool OneToOneRequired, bool IsPriorityFive)
{
    // Python版のバッジ表示（①=1対1必須, P5=通常担当優先度5, 🔒=ロック, ✎=手動）に合わせる。
    public override string ToString() =>
        $"{StudentLabel}　{SubjectLabel}{(OneToOneRequired ? "　①" : "")}{(IsPriorityFive ? "　P5" : "")}{(IsLocked ? "　🔒" : "")}{(IsManual ? "　✎" : "")}";
}

public sealed record BoardCell(long TimeSlotId, long TeacherId, bool Blocked, IReadOnlyList<BoardCard> Cards);

public sealed record ScheduleBoard(IReadOnlyList<BoardSlotRow> Slots, IReadOnlyList<BoardTeacherColumn> Teachers, IReadOnlyList<BoardCell> Cells)
{
    public BoardCell? Cell(long timeSlotId, long teacherId) => Cells.FirstOrDefault(c => c.TimeSlotId == timeSlotId && c.TeacherId == teacherId);
}

// Python版の未配置カード表示（remainingCount/candidateCount）に合わせ、残り回数に加えて
// 現時点で資格・空き時間の条件を満たす候補コマ数も表示する（講師の同時担当上限は候補数に含めない。
// Python版のcandidateCountもsolverの候補生成と同じ定義=容量制約はsolver側の変数間制約であり
// 候補列挙時点ではフィルタしないため、C#版もSqliteScheduleRunServiceの候補生成クエリをそのまま流用する）。
public sealed record UnplacedSessionOption(long LessonRequestId, string Label, int Remaining, int CandidateCount)
{
    public override string ToString() => $"{Label}　(残り{Remaining}回・候補{CandidateCount}枠{(CandidateCount == 0 ? "　⚠配置先なし" : "")})";
}

public sealed record AssignmentSnapshotRow(long Id, long LessonRequestId, long TeacherId, long OpenDateId, long TimeSlotId, bool IsLocked, string Source, int SessionIndex, long? OptimizationRunId, bool IsManual, string Note);

public sealed record TeacherUnavailabilitySnapshotRow(long TeacherId, long OpenDateId, long TimeSlotId);

public sealed record ScheduleSnapshot(IReadOnlyList<AssignmentSnapshotRow> Assignments, IReadOnlyList<TeacherUnavailabilitySnapshotRow> TeacherUnavailabilities);

public sealed record AuditHistoryEntry(DateTimeOffset TimestampUtc, string ActionLabel, string? Reason)
{
    public override string ToString() => $"{TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}　{ActionLabel}{(string.IsNullOrWhiteSpace(Reason) ? "" : $"　({Reason})")}";
}

// Python版のドロップ判定（green/yellow/red）に対応。redはハード制約違反（適用不可）、
// yellowはハード制約は満たすがソフト指標が悪化する変更（確認ダイアログ→理由入力の上で適用可）、
// greenはそのまま適用してよい変更。
public enum EditDecision { Green, Yellow, Red }

public sealed record SoftMetricDelta(string Code, string Label, bool HigherIsBetter, int Before, int After)
{
    public bool Worsened => HigherIsBetter ? After < Before : After > Before;
    public bool Improved => HigherIsBetter ? After > Before : After < Before;
    public string Message => $"{Label}：{Before} → {After}（{(Worsened ? "悪化します" : Improved ? "改善します" : "変わりません")}）";
}

public sealed record EditPreview(EditDecision Decision, string Message, IReadOnlyList<SoftMetricDelta> SoftDeltas)
{
    public bool Allowed => Decision != EditDecision.Red;
    public IReadOnlyList<SoftMetricDelta> WorsenedDeltas => SoftDeltas.Where(d => d.Worsened).ToList();
}

// MoveAsyncはこの例外を投げた場合、hard制約は満たしているがソフト指標が悪化するため、
// 呼び出し側（UI）はPreview.SoftDeltasを確認ダイアログで提示し、ユーザー確認後
// confirmSoftWarnings:trueで再実行する必要がある（Python版のSoftWarningConfirmationRequiredと同じ役割）。
public sealed class SoftWarningConfirmationRequiredException(EditPreview preview) : InvalidOperationException(preview.Message)
{
    public EditPreview Preview { get; } = preview;
}

public interface IScheduleEditorService
{
    Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task AddManualAsync(string projectPath, long lessonRequestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, CancellationToken cancellationToken = default);
    Task RemoveManualAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default);
    Task SetLockedAsync(string projectPath, long assignmentId, bool isLocked, CancellationToken cancellationToken = default);
    Task ResetAutomaticAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenDateOption>> GetOpenDatesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ScheduleBoard> GetBoardAsync(string projectPath, long openDateId, IReadOnlyCollection<long> extraTeacherIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnplacedSessionOption>> GetUnplacedSessionsAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<EditPreview> PreviewMoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task MoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, bool confirmSoftWarnings = false, string? reason = null, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableAsync(string projectPath, long teacherId, long openDateId, long timeSlotId, bool unavailable, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableManyAsync(string projectPath, long openDateId, IReadOnlyCollection<(long TeacherId, long TimeSlotId)> targets, bool unavailable, CancellationToken cancellationToken = default);
    Task<ScheduleSnapshot> CaptureSnapshotAsync(string projectPath, CancellationToken cancellationToken = default);
    Task RestoreSnapshotAsync(string projectPath, ScheduleSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditHistoryEntry>> GetAuditHistoryAsync(string projectPath, int limit = 50, CancellationToken cancellationToken = default);
}
