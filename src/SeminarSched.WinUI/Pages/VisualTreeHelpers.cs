using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SeminarSched_WinUI.Pages;

/// <summary>
/// ユーザー要望（checkpoint142）の行内編集（SetupPage: 生徒・講師・科目、ImportPage: 受講希望）で
/// 共通して使うビジュアルツリー操作。DataTemplateから実体化される要素のx:Nameはページの
/// namescopeへ登録されないため（WinUIの既知の制約）、ビジュアルツリーを直接たどって探す。
/// 探索方針並び替えリスト（<see cref="SchedulingPolicyRowsController.RowPointerEntered"/>等）で
/// 最初に使われたのと同じ手法。
/// </summary>
internal static class VisualTreeHelpers
{
    public static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var c = VisualTreeHelper.GetParent(node); c is not null; c = VisualTreeHelper.GetParent(c))
            if (c is T match) return match;
        return null;
    }

    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>行ホバー時に表示する「変更」ボタン（またはボタン群を包むPanel）の表示/非表示を、
    /// 表示用Grid直下のx:Nameで切り替える。</summary>
    public static void SetNamedChildVisible(Panel container, string name, bool visible)
    {
        if (container.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Name == name) is { } element)
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ユーザー報告「生徒の行にカーソルを合わせたときに、少し上にズレる」への対応。原因は、
    // ChangeDeletePanel（またはChangeButton）のボタンがTextBlockより背が高いため、ホバーで
    // Visibility=Visibleへ切り替わるたびにAuto列の必要な高さが増え、行（Grid）自体の高さが
    // 伸び縮みしていたこと。SetupPage.xaml／ImportPage.xaml側で表示用Gridに固定のHeightを
    // 設定し行の伸縮自体を無くした上で、ホバーの合図としてこのGridのBackgroundを少し濃い
    // グレーへ切り替える（「ずれをなくして、枠を少し濃いグレーに変える」というユーザー要望への
    // 対応）。テーマに関わらず常に「今より少し暗くなる」ことだけが目的のため、黒の半透明
    // オーバーレイを使う（ThemeResourceの明暗切り替えを気にせず済む）。
    private static readonly SolidColorBrush RowHoverBackground = new(Color.FromArgb(26, 0, 0, 0));
    private static readonly SolidColorBrush RowHoverTransparent = new(Colors.Transparent);

    public static void SetRowHoverBackground(Panel displayRow, bool hovered) =>
        displayRow.Background = hovered ? RowHoverBackground : RowHoverTransparent;
}
