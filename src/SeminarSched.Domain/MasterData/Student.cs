namespace SeminarSched.Domain.MasterData;

public sealed record Student
{
    public Student(long id, string externalId, string name, string grade, int defaultMaxConsecutiveSlots = 2,
        bool allowGap = false, string note = "", bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(grade);
        if (defaultMaxConsecutiveSlots <= 0) throw new ArgumentOutOfRangeException(nameof(defaultMaxConsecutiveSlots));
        Id = id; ExternalId = externalId.Trim(); Name = name.Trim(); Grade = grade.Trim();
        DefaultMaxConsecutiveSlots = defaultMaxConsecutiveSlots; AllowGap = allowGap; Note = note?.Trim() ?? ""; Active = active;
    }
    public long Id { get; init; }
    public string ExternalId { get; }
    public string Name { get; }
    public string Grade { get; }
    public int DefaultMaxConsecutiveSlots { get; }
    public bool AllowGap { get; }
    public string Note { get; }
    public bool Active { get; }
}
