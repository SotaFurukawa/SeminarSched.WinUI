namespace SeminarSched.Reporting.Models;

public sealed record ScheduleReportRow(string Date,string TimeSlot,string Student,string StudentGrade,string Subject,string SubjectShortName,string Teacher,bool IsLocked);
public sealed record AbsentStudent(string Grade,string Name);
public sealed record ScheduleReport(
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<DateOnly> OpenDates,
    IReadOnlyList<string> SlotLabels,
    IReadOnlyList<ScheduleReportRow> Rows,
    IReadOnlyList<string> Unassigned,
    IReadOnlyList<AbsentStudent> AbsentStudents);
