namespace SeminarSched.Domain.MasterData;

public sealed record Student
{
    // ユーザー要望（checkpoint145）「生徒や講師の氏名の入力について、出力で基本的に苗字のみを
    // 使う都合があるため、苗字と名前をわけてかいてください。姓と名を分けて保存」への対応。
    // 従来の単一Nameフィールドを廃止し、FamilyName（姓、必須）・GivenName（名、空文字許容）を
    // 正式なフィールドとする。これまでは"姓 名"という空白区切りの単一文字列という「規約」に
    // 頼っていた（WeeklyCalendarLayout.Surname等が先頭の空白で分割して姓を取り出していた）が、
    // DBレベルで正式に分離することで、入力ミス（空白の付け忘れ等）による姓抽出の失敗を防ぐ。
    public Student(long id, string externalId, string familyName, string givenName, string grade, int defaultMaxConsecutiveSlots = 2,
        bool allowGap = false, string note = "", bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(grade);
        if (defaultMaxConsecutiveSlots <= 0) throw new ArgumentOutOfRangeException(nameof(defaultMaxConsecutiveSlots));
        Id = id; ExternalId = externalId.Trim(); FamilyName = familyName.Trim(); GivenName = givenName?.Trim() ?? ""; Grade = grade.Trim();
        DefaultMaxConsecutiveSlots = defaultMaxConsecutiveSlots; AllowGap = allowGap; Note = note?.Trim() ?? ""; Active = active;
    }
    public long Id { get; init; }
    public string ExternalId { get; }
    public string FamilyName { get; }
    public string GivenName { get; }
    public string FullName => GivenName.Length == 0 ? FamilyName : $"{FamilyName} {GivenName}";
    public string Grade { get; }
    public int DefaultMaxConsecutiveSlots { get; }
    public bool AllowGap { get; }
    public string Note { get; }
    public bool Active { get; }
}
