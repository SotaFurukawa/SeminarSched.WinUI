namespace SeminarSched.Domain.MasterData;

public sealed record Teacher
{
    // ユーザー要望（checkpoint145）「姓と名を分けて保存」への対応。Student.csと同じ理由・同じ形。
    public Teacher(long id, string externalId, string familyName, string givenName, bool allowGap = false, string note = "", bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        Id = id; ExternalId = externalId.Trim(); FamilyName = familyName.Trim(); GivenName = givenName?.Trim() ?? ""; AllowGap = allowGap; Note = note?.Trim() ?? ""; Active = active;
    }
    public long Id { get; init; }
    public string ExternalId { get; }
    public string FamilyName { get; }
    public string GivenName { get; }
    public string FullName => GivenName.Length == 0 ? FamilyName : $"{FamilyName} {GivenName}";
    public bool AllowGap { get; }
    public string Note { get; }
    public bool Active { get; }
}
