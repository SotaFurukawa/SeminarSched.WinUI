namespace SeminarSched.Reporting.Models;

public sealed record ScheduleReportRow(string Date,string TimeSlot,string Student,string StudentGrade,string Subject,string SubjectShortName,string Teacher,bool IsLocked,bool IsRegularTeacher,bool OneToOneRequired,bool IsManual);
public sealed record AbsentStudent(string Grade,string Name);
public sealed record TeacherUnavailabilityCell(string Date,string TimeSlot,string Teacher);

/// <summary>
/// TimeSlot.Codeは表示名(DisplayName)とは別の短いコード（"A"等）。Python版の生徒配布handoutページは
/// 「{code}タイム」列を使うため、結合済みラベル文字列を後から分割するのではなく、この専用フィールドを使う。
/// 区切り文字はPython版の実出力でレポートごとに異なる（handoutページは全角チルダ「～」、全体時間割の
/// コマラベルセルは改行＋enダッシュ「–」）ため、生の開始/終了時刻も別々に保持する。
/// </summary>
public sealed record SlotDefinition(string Label,string Code,string StartTimeText,string EndTimeText)
{
    public string TimeRangeText => $"{StartTimeText}～{EndTimeText}";
    public string OverviewLabelText => $"{Code}\n{StartTimeText}–{EndTimeText}";
}

/// <summary>Python版issue_builder.pyの未配置一覧1行。</summary>
public sealed record UnassignedRequestRow(string Student,string Subject,int Required,int Placed,int Missing,string MainReason,IReadOnlyList<string> ResolutionCandidates,int Priority,string? RegularTeacher,bool OneToOneRequired,string Note);

/// <summary>Python版issue_builder.pyの警告一覧1行。現時点のC#は通常担当不足のみを検知できるため、そこから生成する。</summary>
public sealed record WarningRow(string Severity,string IssueType,string? Date,string? Slot,string Student,string Teacher,string Content,string Status);

/// <summary>生徒が集団授業を受講する日時（開始・終了は自由入力でコマに縛られない）。個別指導の
/// 生徒配布ページ上で、この時間帯を黒塗り「集団」表示にするために使う。</summary>
public sealed record GroupLessonAttendance(string Student,DateOnly Date,TimeOnly StartTime,TimeOnly EndTime);

/// <summary>集団授業のクラスへ任意で割り当てられた担当講師が、その授業を担当する日時（開始・終了は
/// 自由入力でコマに縛られない）。全体時間割上で、この時間帯を黒塗り「集団」表示にするために使う。</summary>
public sealed record GroupLessonTeacherAttendance(string Teacher,DateOnly Date,TimeOnly StartTime,TimeOnly EndTime);

public sealed record ScheduleReport(
    string ProjectTitle,
    int AcademicYear,
    string SeasonName,
    string GeneratedAtText,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<DateOnly> OpenDates,
    IReadOnlyList<SlotDefinition> SlotDefinitions,
    IReadOnlyList<ScheduleReportRow> Rows,
    IReadOnlyList<UnassignedRequestRow> UnassignedRequests,
    IReadOnlyList<AbsentStudent> AbsentStudents,
    IReadOnlyList<WarningRow> Warnings,
    IReadOnlyList<TeacherUnavailabilityCell> TeacherUnavailabilities,
    IReadOnlyList<GroupLessonAttendance> GroupLessonAttendances,
    IReadOnlyList<GroupLessonTeacherAttendance> GroupLessonTeacherAttendances)
{
    public IReadOnlyList<string> SlotLabels { get; } = SlotDefinitions.Select(s => s.Label).ToArray();
}
