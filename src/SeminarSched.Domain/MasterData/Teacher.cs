namespace SeminarSched.Domain.MasterData;

public sealed record Teacher
{
    public Teacher(long id, string externalId, string name, bool allowGap = false, string note = "", bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id; ExternalId = externalId.Trim(); Name = name.Trim(); AllowGap = allowGap; Note = note?.Trim() ?? ""; Active = active;
    }
    public long Id { get; init; }
    public string ExternalId { get; }
    public string Name { get; }
    public bool AllowGap { get; }
    public string Note { get; }
    public bool Active { get; }
}
