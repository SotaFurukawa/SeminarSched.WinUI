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
        // 当初はPython版domain/validation.pyのvalidate_subjectと同じ「略称は未入力か、ちょうど1文字」
        // という制約だった（時間割の科目表記を一文字に収める想定、SubjectAbbreviation参照）。
        // ユーザー指示（「基本一文字だが、2文字以上でも大丈夫なようにしてください」、続けて
        // 「4文字の制約ではなく制約をつけないでください」）により、文字数の制約自体を撤廃した
        // （Python版からの意図的な逸脱。IME変換途中のひらがな2文字以上がMaxLengthで打ち切られ、
        // 漢字1文字へすら変換できなかった問題への対応）。
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
