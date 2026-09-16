namespace SeminarSched.Domain.CourseSettings;

public sealed record TimeSlot
{
    public TimeSlot(long id, string code, string displayName, TimeOnly startTime, TimeOnly endTime, int sortOrder, bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (endTime <= startTime) throw new ArgumentException("終了時刻は開始時刻より後にしてください。");
        if (sortOrder < 1) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        Id = id; Code = code.Trim(); DisplayName = displayName.Trim(); StartTime = startTime; EndTime = endTime; SortOrder = sortOrder; Active = active;
    }
    public long Id { get; init; }
    public string Code { get; }
    public string DisplayName { get; }
    public TimeOnly StartTime { get; }
    public TimeOnly EndTime { get; }
    public int SortOrder { get; }
    public bool Active { get; }
}
