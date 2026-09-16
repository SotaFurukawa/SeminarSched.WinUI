namespace SeminarSched.Domain.CourseSettings;

public sealed record CourseDay(DateOnly Date, bool IsOpen, string Note, IReadOnlyList<long> EnabledTimeSlotIds);
