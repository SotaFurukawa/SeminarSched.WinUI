namespace SeminarSched.Domain.Output;

/// <summary>
/// Python版reporting/settings.pyのOutputSettings相当。プロジェクトごとに保存する出力の見た目設定。
/// Python版はさらに多くの項目（表示項目ON/OFF・ロゴ・1ページの日数/講師列数・生徒別改ページ方式）を
/// 持つが、本アプリでは既存の週単位固定レイアウトと衝突しない範囲（用紙・余白・ファイル名規則・
/// 主要な塗り色3種）のみを対象とする。
/// </summary>
public sealed record OutputSettings
{
    public const string PaperSizeA3 = "A3";
    public const string PaperSizeA4 = "A4";
    public const string OrientationLandscape = "landscape";
    public const string OrientationPortrait = "portrait";

    public OutputSettings(
        string paperSize = PaperSizeA4,
        string orientation = OrientationLandscape,
        double marginMm = 8,
        string fileNamePattern = "{project}-{report}",
        string closedFillHex = "#E8E8E8",
        string unavailableFillHex = "#D9D9D9",
        string groupFillHex = "#000000")
    {
        if (paperSize is not (PaperSizeA3 or PaperSizeA4)) throw new ArgumentOutOfRangeException(nameof(paperSize), "用紙はA3またはA4を指定してください。");
        if (orientation is not (OrientationLandscape or OrientationPortrait)) throw new ArgumentOutOfRangeException(nameof(orientation), "向きは横または縦を指定してください。");
        if (marginMm is < 0 or > 30) throw new ArgumentOutOfRangeException(nameof(marginMm), "余白は0～30mmで指定してください。");
        ValidateHexColor(closedFillHex, nameof(closedFillHex));
        ValidateHexColor(unavailableFillHex, nameof(unavailableFillHex));
        ValidateHexColor(groupFillHex, nameof(groupFillHex));
        ValidateFileNamePattern(fileNamePattern);

        PaperSize = paperSize; Orientation = orientation; MarginMm = marginMm; FileNamePattern = fileNamePattern.Trim();
        ClosedFillHex = closedFillHex.Trim(); UnavailableFillHex = unavailableFillHex.Trim(); GroupFillHex = groupFillHex.Trim();
    }

    public string PaperSize { get; }
    public string Orientation { get; }
    public double MarginMm { get; }
    public string FileNamePattern { get; }
    // 休校日の塗り色（生徒配布ページ等）。既定値は既存の固定色(#E8E8E8)と同一。
    public string ClosedFillHex { get; }
    // 勤務不可コマの塗り色（全体時間割）。既定値は既存の固定色(#D9D9D9)と同一。
    public string UnavailableFillHex { get; }
    // 集団授業のコマの塗り色（生徒配布ページ）。既定値は既存の固定色(黒)と同一。
    public string GroupFillHex { get; }

    // C#の静的フィールド初期化はソース上の記述順に実行されるため、Defaultより前で初期化しておく
    // 必要がある（Defaultのコンストラクタがこれらを参照する）。
    private static readonly System.Text.RegularExpressions.Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly string[] AllowedFileNameTokens = ["project", "report", "date"];

    public static readonly OutputSettings Default = new();

    private static void ValidateHexColor(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColorPattern.IsMatch(value.Trim()))
            throw new ArgumentException("色は#RRGGBB形式で指定してください。", paramName);
    }

    private static void ValidateFileNamePattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("ファイル名規則は空にできません。", nameof(value));
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            if (invalidChar != '{' && invalidChar != '}' && value.Contains(invalidChar))
                throw new ArgumentException("ファイル名規則に使用できない記号があります。", nameof(value));

        var i = 0;
        while (i < value.Length)
        {
            if (value[i] == '{')
            {
                var close = value.IndexOf('}', i);
                if (close < 0) throw new ArgumentException("ファイル名規則の波括弧が不正です。", nameof(value));
                var token = value[(i + 1)..close];
                if (!AllowedFileNameTokens.Contains(token))
                    throw new ArgumentException("ファイル名規則では{project}、{report}、{date}だけを使用できます。", nameof(value));
                i = close + 1;
            }
            else i++;
        }
    }

    public string BuildFileName(string projectTitle, string reportName, DateOnly date) =>
        FileNamePattern
            .Replace("{project}", SanitizeForFileName(projectTitle))
            .Replace("{report}", SanitizeForFileName(reportName))
            .Replace("{date}", date.ToString("yyyyMMdd"));

    public static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(ch => !invalid.Contains(ch)).ToArray());
    }
}
