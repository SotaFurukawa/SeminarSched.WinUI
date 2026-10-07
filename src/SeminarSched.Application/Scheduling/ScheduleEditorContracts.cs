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

// 選択中の日付でこの受講希望が実際に置ける状態のときだけ一覧に出す（生徒がその日出席できない・
// 資格のある講師の空きが無い等の場合は表示自体をしない）。カードには生徒ID等は出さず、氏名・学年・
// 科目（略称）・残り回数・その日置ける具体的なコマだけを表示する。
public sealed record UnplacedSessionOption(long LessonRequestId, string StudentName, string Grade, string SubjectShortName, int Remaining, IReadOnlyList<string> AvailableSlotCodes)
{
    public string AvailableSlotsText => $"配置可能: {string.Join("・", AvailableSlotCodes)}";
}

public sealed record AssignmentSnapshotRow(long Id, long LessonRequestId, long TeacherId, long OpenDateId, long TimeSlotId, bool IsLocked, string Source, int SessionIndex, long? OptimizationRunId, bool IsManual, string Note);

public sealed record TeacherUnavailabilitySnapshotRow(long TeacherId, long OpenDateId, long TimeSlotId);

public sealed record ScheduleSnapshot(IReadOnlyList<AssignmentSnapshotRow> Assignments, IReadOnlyList<TeacherUnavailabilitySnapshotRow> TeacherUnavailabilities);

// 再最適化の差分表示（④「自動作成の差分」カード）でIDを人間可読な文字列へ解決するための
// 軽量ルックアップ。プロジェクト全体のRequest/Teacher/Date/Slotを一括取得してUI側で使い回す。
public sealed record ScheduleLabelSet(
    IReadOnlyDictionary<long, string> RequestLabels,
    IReadOnlyDictionary<long, string> TeacherLabels,
    IReadOnlyDictionary<long, string> DateLabels,
    IReadOnlyDictionary<long, string> SlotLabels)
{
    public string Request(long id) => RequestLabels.GetValueOrDefault(id, $"#{id}");
    public string Teacher(long id) => TeacherLabels.GetValueOrDefault(id, $"#{id}");
    public string DateSlot(long dateId, long slotId) => $"{DateLabels.GetValueOrDefault(dateId, $"#{dateId}")} {SlotLabels.GetValueOrDefault(slotId, $"#{slotId}")}";
}

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

// ユーザー要望（checkpoint152）「自動作成の後から配置するものは元から動かないようにするという
// 設定で、残ったものがどこにも配置できなかった場合に、ここをこう変えれば配置できますよ、という
// のがあれば警告を出して、『入れ替えて配置する』『配置せずそのままにする』ボタンを用意してほしい。
// この入れ替えはできるだけ最小になるように」への対応。未配置の受講希望1件につき、既存の固定配置
// （ロック・手動配置は対象外）を1件だけ別の空きコマへ移動すれば配置できる、という「1手」の
// 入れ替え案だけを探す（それ以上の連鎖的な入れ替えは探索しない＝これが「最小」の実装範囲）。
public sealed record SwapSuggestion(
    long UnassignedRequestId, string UnassignedLabel,
    long MovingAssignmentId, string MovingAssignmentLabel,
    long FreedTeacherId, long FreedOpenDateId, long FreedTimeSlotId, string FreedSlotLabel,
    long NewTeacherId, long NewOpenDateId, long NewTimeSlotId, string NewSlotLabel)
{
    public string Message => $"{UnassignedLabel}は、{MovingAssignmentLabel}（現在: {FreedSlotLabel}）を{NewSlotLabel}へ移動すれば{FreedSlotLabel}へ配置できます。";
}

public interface IScheduleEditorService
{
    // プロジェクトファイル（WALモードのため本体＋"-wal"サイドカーの新しい方）の最終更新時刻を
    // ticksで返す軽量フィンガープリント（Python版ScheduleEditServiceのcontent fingerprintに相当）。
    // 呼び出し側（WinUIの編集画面）が読み込み時にこの値を保持しておき、編集操作の直前に再取得して
    // 不一致なら「他経路でプロジェクトが変更された」と判断し、書き込みを中止して再読み込みする。
    // 実装当初はSQLiteのPRAGMA data_versionを使う案を検証したが、この環境（WALモード+
    // Microsoft.Data.Sqliteの毎回新規接続）では外部接続からの変更を検出できないことが
    // テストで実証されたため、HomePageの最終更新時刻表示と同じファイルタイムスタンプ方式に変更した。
    Task<long> GetDataVersionAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleAssignmentItem>> GetAssignmentsAsync(string projectPath, long? openDateId = null, CancellationToken cancellationToken = default);
    Task AddManualAsync(string projectPath, long lessonRequestId, long teacherId, long openDateId, long timeSlotId, bool isLocked, bool confirmSoftWarnings = false, string? reason = null, CancellationToken cancellationToken = default);
    Task<EditPreview> PreviewAddAsync(string projectPath, long lessonRequestId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task RemoveManualAsync(string projectPath, long assignmentId, CancellationToken cancellationToken = default);
    Task SetLockedAsync(string projectPath, long assignmentId, bool isLocked, CancellationToken cancellationToken = default);
    Task ResetAutomaticAsync(string projectPath, CancellationToken cancellationToken = default);
    Task ResetAllAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenDateOption>> GetOpenDatesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<ScheduleBoard> GetBoardAsync(string projectPath, long openDateId, IReadOnlyCollection<long> extraTeacherIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnplacedSessionOption>> GetUnplacedSessionsAsync(string projectPath, long openDateId, CancellationToken cancellationToken = default);
    // ユーザー要望（checkpoint151）「未配置に残っているものを移そうとしてドラッグしているときに、
    // 生徒が出席不可にしているコマに禁止マークをつけるようにしておく」への対応。ドラッグ開始時に
    // この受講希望の生徒が選択中の日付で明示的に出席不可（AvailabilityLevel=0）に設定している
    // コマのIdだけを返す（未回答＝既定で出席可の行は含めない）。
    Task<IReadOnlyList<long>> GetStudentUnavailableSlotIdsAsync(string projectPath, long lessonRequestId, long openDateId, CancellationToken cancellationToken = default);
    Task<EditPreview> PreviewMoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, CancellationToken cancellationToken = default);
    Task MoveAsync(string projectPath, long assignmentId, long teacherId, long openDateId, long timeSlotId, bool confirmSoftWarnings = false, string? reason = null, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableAsync(string projectPath, long teacherId, long openDateId, long timeSlotId, bool unavailable, CancellationToken cancellationToken = default);
    Task SetTeacherUnavailableManyAsync(string projectPath, long openDateId, IReadOnlyCollection<(long TeacherId, long TimeSlotId)> targets, bool unavailable, CancellationToken cancellationToken = default);
    Task<ScheduleSnapshot> CaptureSnapshotAsync(string projectPath, CancellationToken cancellationToken = default);
    Task RestoreSnapshotAsync(string projectPath, ScheduleSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditHistoryEntry>> GetAuditHistoryAsync(string projectPath, int limit = 50, CancellationToken cancellationToken = default);
    Task<ScheduleLabelSet> GetLabelSetAsync(string projectPath, CancellationToken cancellationToken = default);

    // ユーザー要望（checkpoint152）。keepExistingPlacements=trueで自動作成した結果、未配置が残った
    // ときに呼び出し側（OptimizationPage）から使う。1件も見つからなければ空配列を返す。
    Task<IReadOnlyList<SwapSuggestion>> FindSwapSuggestionsAsync(string projectPath, CancellationToken cancellationToken = default);
    // suggestion.MovingAssignmentIdをsuggestion.New...へ移動し、suggestion.UnassignedRequestIdを
    // suggestion.Freed...（＝移動前にMovingAssignmentIdがあったコマ）へ手動配置する。1つの
    // トランザクションで原子的に行う（Finding時点から状態が変わっていた場合はInvalidOperationException）。
    Task ApplySwapSuggestionAsync(string projectPath, SwapSuggestion suggestion, CancellationToken cancellationToken = default);
}
