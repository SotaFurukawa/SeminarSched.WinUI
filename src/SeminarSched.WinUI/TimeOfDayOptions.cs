using System.Globalization;

namespace SeminarSched_WinUI;

// コマ設定(SetupPage)・集団授業クラスのセッション時刻(GroupLessonClassPage)で共有する時刻選択肢。
// ユーザー指摘（WinUI標準TimePickerのflyoutが位置ずれ・確定ボタン必須で使いにくかった。checkpoint78で
// 一度NumberBox2個の組へ置き換えたが、今度は分まで見えづらいとの指摘を受けた）を踏まえ、
// 編集可能なComboBoxへ5分刻みの候補一覧を渡す方式にした。選択すれば即座に反映され、
// 5分刻みに無い既存の値（レガシーデータ）もIsEditable="True"により表示・編集できる。
internal static class TimeOfDayOptions
{
    public const int MinuteStep = 5;

    public static readonly IReadOnlyList<string> Values = BuildValues();

    private static IReadOnlyList<string> BuildValues()
    {
        var values = new List<string>(24 * 60 / MinuteStep);
        for (var minutes = 0; minutes < 24 * 60; minutes += MinuteStep)
            values.Add($"{minutes / 60:D2}:{minutes % 60:D2}");
        return values;
    }

    public static TimeOnly Parse(string? text)
        => TimeOnly.TryParseExact((text ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value)
            ? value
            : throw new FormatException("時刻はHH:mm形式で入力してください（例: 09:05）。");

    public static string Format(TimeOnly time) => time.ToString("HH\\:mm", CultureInfo.InvariantCulture);
}
