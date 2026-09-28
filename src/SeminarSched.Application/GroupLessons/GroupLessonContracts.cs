using SeminarSched.Domain.GroupLessons;

namespace SeminarSched.Application.GroupLessons;

public sealed record GroupLessonCalendarDate(long OpenDateId, DateOnly Date);

// 3.1のカレンダー1マスに表示する1件（クラス名・科目・時刻帯）。時刻はTimeSlotに縛られない自由入力。
public sealed record GroupLessonSessionOption(long Id, long ClassId, string ClassName, string ClassSubject, long OpenDateId, TimeOnly StartTime, TimeOnly EndTime)
{
    public string TimeRangeLabel => $"{StartTime:HH\\:mm}～{EndTime:HH\\:mm}";
    public override string ToString() => $"{ClassName}　{ClassSubject}　{TimeRangeLabel}";
}

public sealed record GroupLessonEnrollmentCandidate(long StudentId, string ExternalId, string Name, string Grade, bool Enrolled)
{
    public string Display => $"{Name}　（{Grade}）";
    public override string ToString() => $"{(Enrolled ? "✓ " : "")}{Display}";
}

public interface IGroupLessonService
{
    Task<IReadOnlyList<GroupLessonClass>> GetClassesAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<GroupLessonClass> SaveClassAsync(string projectPath, GroupLessonClass value, CancellationToken cancellationToken = default);
    Task DeleteClassAsync(string projectPath, long classId, CancellationToken cancellationToken = default);

    // 3.1のカレンダー描画用。project期間内の全OpenDate（開校・休校を問わない）を日付昇順で返す。
    Task<IReadOnlyList<GroupLessonCalendarDate>> GetCalendarDatesAsync(string projectPath, CancellationToken cancellationToken = default);

    // project全体・全クラス分のセッションを返す（カレンダーは特定の1クラスだけでなく全クラスを表示するため）。
    Task<IReadOnlyList<GroupLessonSessionOption>> GetAllSessionsAsync(string projectPath, CancellationToken cancellationToken = default);

    // カレンダーで選択した複数日付へ、同じクラス・同じ時刻帯のセッションを一括追加する（既に同一内容が
    // 登録済みの日付は無視して続行する＝チェック済み日付を再度追加してもエラーにならない）。
    Task AddSessionsAsync(string projectPath, long classId, IReadOnlyCollection<long> openDateIds, TimeOnly startTime, TimeOnly endTime, CancellationToken cancellationToken = default);
    Task RemoveSessionAsync(string projectPath, long sessionId, CancellationToken cancellationToken = default);

    // allowOtherGradesがfalseの場合はvalue.Gradeと一致する生徒のみ、trueの場合は全学年の生徒を返す。
    Task<IReadOnlyList<GroupLessonEnrollmentCandidate>> GetEnrollmentCandidatesAsync(string projectPath, long classId, CancellationToken cancellationToken = default);
    Task SetEnrollmentAsync(string projectPath, long classId, long studentId, bool enrolled, CancellationToken cancellationToken = default);
}
