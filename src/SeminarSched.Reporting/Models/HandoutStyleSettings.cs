namespace SeminarSched.Reporting.Models;

/// <summary>
/// 生徒配布・講師配布時間割xlsxの見た目（フォント・色・文字サイズ）を校舎ごとにカスタマイズ可能にする
/// 機能。<see cref="Default"/>がユーザー指定のデフォルト値であり、生成されたxlsx内の「デザイン設定」
/// シートを校舎側で編集して保存すると、次回以降の出力でその値が読み込まれ反映される。
/// </summary>
public sealed record HandoutStyleSettings(
    string TitleFontName,
    double TitleFontSize,
    string TitleFillHex,
    string TitleFontColorHex,
    string BodyFontName,
    double NameFontSize,
    double HeaderFontSize,
    double MonthFontSize,
    string MonthFillHex,
    string DayFillHex,
    string HeaderBlankFillHex,
    string AcademicTestFillHex)
{
    public static readonly HandoutStyleSettings Default = new(
        TitleFontName: "BIZ UDPMincho Medium",
        TitleFontSize: 16,
        TitleFillHex: "#000000",
        TitleFontColorHex: "#FFFFFF",
        BodyFontName: "HG丸ゴシックM-PRO",
        NameFontSize: 14,
        HeaderFontSize: 9,
        MonthFontSize: 11,
        MonthFillHex: "#0F243E",
        DayFillHex: "#90CAFE",
        HeaderBlankFillHex: "#BFBFBF",
        AcademicTestFillHex: "#95B3D7");
}
