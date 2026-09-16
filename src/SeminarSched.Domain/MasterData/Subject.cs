namespace SeminarSched.Domain.MasterData;

public sealed record Subject
{
    public Subject(long id, string code, string displayName, string shortName, string schoolLevel, int sortOrder, bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schoolLevel);
        if (sortOrder < 1) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if ((shortName ?? "").Trim().Length > 10) throw new ArgumentException("科目略称は10文字以内です。", nameof(shortName));
        Id = id; Code = code.Trim(); DisplayName = displayName.Trim(); ShortName = shortName?.Trim() ?? "";
        SchoolLevel = schoolLevel.Trim(); SortOrder = sortOrder; Active = active;
    }
    public long Id { get; init; }
    public string Code { get; }
    public string DisplayName { get; }
    public string ShortName { get; }
    public string SchoolLevel { get; }
    public int SortOrder { get; }
    public bool Active { get; }
}
