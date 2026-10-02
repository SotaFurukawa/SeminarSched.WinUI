using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SeminarSched.Domain.Scheduling;

namespace SeminarSched_WinUI;

/// <summary>
/// 探索方針（優先度）行の並び替えUIロジック。SetupPage（プロジェクトの既定値）と
/// OptimizationPage（その回限りの一時上書き）の両方で同じ挙動を共有するために切り出した。
/// </summary>
public sealed class SchedulingPolicyRowsController(
    ObservableCollection<SchedulingPolicyRowViewModel> rows,
    ItemsControl list)
{
    public void Load(SchedulingPolicy policy)
    {
        rows.Clear();
        foreach (var row in SchedulingPolicyRowViewModel.BuildRows(policy))
        {
            rows.Add(row);
        }
    }

    private void Renumber()
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].DisplayNumber = i + 1;
        }
    }

    // ObservableCollection.Move()だけでは、並び替え後にその場へ再配置されたコンテナ内の
    // RadioButtonsがSelectedIndexを正しく再描画しない（内部の値自体は正しいが、選択状態の丸印が
    // 表示されない）WinUIの既知の挙動が確認されたため、並び替えのたびにItemsSourceを張り直して
    // コンテナを作り直し、確実に正しい選択状態で再描画させる。
    private void Refresh()
    {
        list.ItemsSource = null;
        list.ItemsSource = rows;
    }

    // RadioButtonsのSelectedIndexをx:Bind Mode=TwoWayで双方向バインドしたところ、
    // ObservableCollection.Move()で並び替えた後に再配置されたコンテナのRadioButtonsが選択状態
    // （丸印）を正しく再描画しない不具合が確認された。バインドの自動同期に頼らず、Loadedで確実に
    // Items準備後に初期値を設定し、SelectionChangedで明示的にViewModelへ書き戻す完全手動の方式に
    // して回避した。
    public void OptionLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButtons { DataContext: SchedulingPolicyRowViewModel row } radioButtons)
        {
            radioButtons.SelectedIndex = row.SelectedOptionIndex;
        }
    }

    // ユーザー報告「考慮するものが2つ発生した場合、考慮するものは上に置くという指示がループして
    // おり、2つ以上のものを配置するとバグる」への対応。原因はRefresh()がItemsSourceを張り直す
    // たびに全行のRadioButtonsが再構築され、OptionLoadedが再度SelectedIndexを設定し直すことで
    // SelectionChangedが（ユーザー操作なしに）再発火していたこと。2つ目の非デフォルト行を先頭へ
    // 昇格させた際のRefresh()が、既に先頭ではない1つ目の非デフォルト行のSelectionChangedを
    // 誘発し、それがまた昇格→Refresh()→...と無限ループしていた。
    // 修正: SelectedIndexが（昇格前の）既存の値と一致する場合は「実際の変更ではない」として
    // 何もしない。OptionLoadedによる再同期は常にrow.SelectedOptionIndexと同じ値を設定するため
    // ここで弾かれ、ユーザーが実際に選択肢を変えた場合（新しい値が既存値と異なる）だけ処理が進む。
    public void OptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not RadioButtons { DataContext: SchedulingPolicyRowViewModel row } radioButtons)
        {
            return;
        }

        if (radioButtons.SelectedIndex < 0 || radioButtons.SelectedIndex == row.SelectedOptionIndex)
        {
            return;
        }

        row.SelectedOptionIndex = radioButtons.SelectedIndex;

        // ユーザー要望「デフォルトはすべて考慮しないとなっているが、もしそれ以外が選択されたら、
        // 自動的に一番上に来るようにしてほしい」への対応。
        if (row.SelectedOptionIndex == 0)
        {
            return;
        }

        var index = rows.IndexOf(row);
        if (index <= 0)
        {
            return;
        }

        rows.Move(index, 0);
        Renumber();
        Refresh();
    }

    public void MoveUp(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not SchedulingPolicyRowViewModel row)
        {
            return;
        }

        var index = rows.IndexOf(row);
        if (index <= 0)
        {
            return;
        }

        rows.Move(index, index - 1);
        Renumber();
        Refresh();
    }

    public void MoveDown(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not SchedulingPolicyRowViewModel row)
        {
            return;
        }

        var index = rows.IndexOf(row);
        if (index < 0 || index >= rows.Count - 1)
        {
            return;
        }

        rows.Move(index, index + 1);
        Renumber();
        Refresh();
    }

    // 各行のGridはDataTemplateから実体化されるため、x:Nameで付けた名前はページ全体のnamescopeへ
    // 登録されずFindNameでは解決できない（WinUIのDataTemplateの既知の制約）。x:Nameは対象のName
    // プロパティ自体には設定されるため、Grid.Childrenを直接たどってNameで探す。
    public static void RowPointerEntered(object sender, PointerRoutedEventArgs e) => SetArrowsVisibility((Grid)sender, Visibility.Visible);

    public static void RowPointerExited(object sender, PointerRoutedEventArgs e) => SetArrowsVisibility((Grid)sender, Visibility.Collapsed);

    private static void SetArrowsVisibility(Grid row, Visibility visibility)
    {
        if (row.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Name == "ArrowsPanel") is { } panel)
        {
            panel.Visibility = visibility;
        }
    }
}
