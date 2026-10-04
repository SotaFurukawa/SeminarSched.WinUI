using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
}
