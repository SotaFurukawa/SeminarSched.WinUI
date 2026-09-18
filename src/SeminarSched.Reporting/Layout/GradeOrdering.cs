namespace SeminarSched.Reporting.Layout;

/// <summary>
/// Python版distribution_builder.pyの_GRADE_ORDER相当（小1..小6 &lt; 中1..中3 &lt; 高1..高3の学年順）。
/// 既定の文字列比較は五十音の読み順（高→小→中）になってしまうため、帳票の生徒並び順には必ずこれを使う。
/// </summary>
public static class GradeOrdering
{
    public static int SortKey(string grade)
    {
        if (grade.Length == 0) return 999;
        var number = new string(grade.Where(char.IsDigit).ToArray());
        if (!int.TryParse(number, out var year)) return 999;
        return grade[0] switch { '小' => year, '中' => 10 + year, '高' => 20 + year, _ => 999 };
    }
}
