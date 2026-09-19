namespace SeminarSched.Domain.MasterData;

/// <summary>
/// 新しい季節講習（新年度）を迎える際に、生徒の学年をまとめて1つ繰り上げるための変換。
/// 高3の次は既卒（卒業）として扱い、既卒は在籍状態を停止（Active=false）にする。
/// </summary>
public static class GradeAdvancement
{
    public const string GraduateGrade = "既卒";

    private static readonly string[] Sequence =
    [
        .. Enumerable.Range(1, 6).Select(year => $"小{year}"),
        .. Enumerable.Range(1, 3).Select(year => $"中{year}"),
        .. Enumerable.Range(1, 3).Select(year => $"高{year}"),
    ];

    /// <summary>現在の学年から、繰り上げ後の学年を返す。認識できない学年表記（既卒を含む）は
    /// そのまま返し、BecameGraduateはfalseになる。</summary>
    public static (string Grade, bool BecameGraduate) Advance(string currentGrade)
    {
        var index = Array.IndexOf(Sequence, currentGrade.Trim());
        if (index < 0) return (currentGrade, false);
        if (index == Sequence.Length - 1) return (GraduateGrade, true);
        return (Sequence[index + 1], false);
    }
}
