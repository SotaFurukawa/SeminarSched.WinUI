namespace SeminarSched.Domain.MasterData;

/// <summary>
/// Python版domain/defaults.pyのdefault_subject_short_name相当。<see cref="Subject.ShortName"/>を
/// 空のまま登録した場合でも、帳票の科目表記が必ず一文字の略称になるようにするfallback。
/// Python版はSubject.Code（"JH_MATH"等の固定命名規約）をキーにした辞書だったが、本アプリの
/// Subject.Codeは自由入力でPython版の命名規約と一致する保証が無いため、代わりに表示名の
/// キーワード一致で判定する。
/// </summary>
public static class SubjectAbbreviation
{
    private static readonly (string Keyword, string ShortName)[] KnownSubjects =
    [
        ("現代文", "現"), ("古文", "古"),
        ("数学", "数"), ("算数", "算"),
        ("英語", "英"),
        ("国語", "国"),
        ("物理", "物"), ("化学", "化"), ("生物", "生"), ("理科", "理"),
        ("日本史", "日"), ("世界史", "世"), ("地理", "地"), ("政治経済", "政"), ("社会", "社"),
        ("情報", "情"),
    ];

    /// <summary>ShortNameが空でなければそのまま使う。空なら表示名から一文字の略称を推定し、
    /// 表示名すら空ならCodeの先頭一文字、それも無ければ「科」を返す（Python版と同じ3段階fallback）。</summary>
    public static string Resolve(string displayName, string? shortName, string? code = null)
    {
        if (!string.IsNullOrWhiteSpace(shortName)) return shortName.Trim();

        var compact = (displayName ?? "").Trim().Replace("・", "");
        foreach (var (keyword, abbreviation) in KnownSubjects)
            if (compact.Contains(keyword, StringComparison.Ordinal)) return abbreviation;

        if (compact.Length > 0) return compact[^1..];
        if (!string.IsNullOrWhiteSpace(code)) return code.Trim()[..1];
        return "科";
    }
}
