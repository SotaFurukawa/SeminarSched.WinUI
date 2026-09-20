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
        // Python版domain/validation.pyのvalidate_subjectと同じ制約: 略称は未入力か、ちょうど1文字。
        // 時間割の科目表記が常に一文字に収まることを保証するための不変条件（SubjectAbbreviation参照）。
        if ((shortName ?? "").Trim() is { Length: > 0 } trimmedShortName && trimmedShortName.Length != 1)
            throw new ArgumentException("科目略称は1文字で入力してください。", nameof(shortName));
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
