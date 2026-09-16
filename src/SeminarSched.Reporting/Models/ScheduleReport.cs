namespace SeminarSched.Reporting.Models;

public sealed record ScheduleReportRow(string Date,string TimeSlot,string Student,string Subject,string Teacher,bool IsLocked);
public sealed record ScheduleReport(string Title,IReadOnlyList<ScheduleReportRow> Rows,IReadOnlyList<string> Unassigned);
