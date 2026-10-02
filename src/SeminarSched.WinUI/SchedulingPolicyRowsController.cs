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

        Refresh();
    }

    public void LoadFromSaved(IReadOnlyList<(SchedulingPolicyDimension Dimension, int SelectedOptionIndex)> saved)
    {
        rows.Clear();
        foreach (var row in SchedulingPolicyRowViewModel.BuildRowsFromSaved(saved))
        {
            rows.Add(row);
        }

        Refresh();
    }

    private void Renumber()
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].DisplayNumber = i + 1;
        }
    }

    // ObservableCollection.Move()だけでは、並び替え後にその場へ再配置されたコンテナ内の
    // RadioButtonがIsCheckedを正しく再描画しない（内部の値自体は正しいが、選択状態の丸印が
    // 表示されない）WinUIの既知の挙動が確認されたため、並び替えのたびにItemsSourceを張り直して
    // コンテナを作り直し、確実に正しい選択状態で再描画させる。
    private void Refresh()
    {
        list.ItemsSource = null;
        list.ItemsSource = rows;
    }

    // ユーザー要望（checkpoint129）「探索方針について、スライドもできるようにしてほしい」への
    // 対応。ListViewのCanReorderItems+AllowDropによるドラッグ並び替えも、内部的には▲▼と同じ
    // ObservableCollection.Move()を使うため、完了後に番号を振り直し、RadioButtonの選択状態の
    // 再描画不具合を避けるためのコンテナ再構築が必要。
    public void DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        Renumber();
        Refresh();
    }

    // ユーザー指摘（checkpoint128）「探索方針のラジオボタンが揃っていない」への対応。以前は
    // WinUIのRadioButtons（ItemsSource一括表示、MaxColumns=3）を使っていたが、これは各行ごとに
    // 独立して列幅を自前の内容量から自動計算するため、行間で選択肢の横位置が揃わなかった。
    // 個別のRadioButton×3（SchedulingPolicyRowViewModel.Option0Label〜Option2Label、固定幅の
    // Gridカラム）へ置き換え、Tagに選択肢の位置（0〜2）を持たせる。
    // IsCheckedをx:Bind Mode=TwoWayで双方向バインドしたところ、ObservableCollection.Move()で
    // 並び替えた後に再配置されたコンテナのRadioButtonが選択状態（丸印）を正しく再描画しない不具合が
    // 確認された。バインドの自動同期に頼らず、Loadedで確実にIsCheckedを設定し、Checkedで明示的に
    // ViewModelへ書き戻す完全手動の方式にして回避した。
    public void OptionLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: SchedulingPolicyRowViewModel row, Tag: string tag } button
            && int.TryParse(tag, out var optionIndex))
        {
            button.IsChecked = row.SelectedOptionIndex == optionIndex;
        }
    }

    // ユーザー報告「考慮するものが2つ発生した場合、考慮するものは上に置くという指示がループして
    // おり、2つ以上のものを配置するとバグる」への対応。原因はRefresh()がItemsSourceを張り直す
    // たびに全行のRadioButtonが再構築され、OptionLoadedが再度IsCheckedを設定し直すことで
    // Checkedが（ユーザー操作なしに）再発火していたこと。2つ目の非デフォルト行を先頭へ昇格させた
    // 際のRefresh()が、既に先頭ではない1つ目の非デフォルト行のCheckedを誘発し、それがまた
    // 昇格→Refresh()→…と無限ループしていた。
    // 修正: 新しい選択値が（昇格前の）既存の値と一致する場合は「実際の変更ではない」として何も
    // しない。OptionLoadedによる再同期は常にrow.SelectedOptionIndexと同じ値を設定するためここで
    // 弾かれ、ユーザーが実際に選択肢を変えた場合（新しい値が既存値と異なる）だけ処理が進む。
    //
    // ユーザー要望（checkpoint128）「探索方針の選択により一番上にいくものについて、考慮しない
    // ものの上に来るイメージ。例えば、すでに①から③まで考慮するようになっていて、⑤も考慮する
    // ようにするなら、⑤だったものが④の位置にくるようにする」への対応。以前は「考慮しない」
    // 以外を選ぶと絶対的な先頭（index 0）へ移動していたが、既に考慮する設定になっている行の
    // 直後（＝最初の「考慮しない」行の直前）へ挿入するよう変更した。また、この昇格は
    // 「考慮しない→考慮する」への変更時にだけ行う（既に考慮する行の中で選択肢を変えただけでは
    // 優先順位を変えない）。
    public void OptionChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { DataContext: SchedulingPolicyRowViewModel row, Tag: string tag } button
            || !int.TryParse(tag, out var optionIndex))
        {
            return;
        }

        if (optionIndex == row.SelectedOptionIndex)
        {
            return;
        }

        var wasDefault = row.SelectedOptionIndex == 0;
        row.SelectedOptionIndex = optionIndex;

        if (!wasDefault || row.SelectedOptionIndex == 0)
        {
            return;
        }

        var currentIndex = rows.IndexOf(row);
        var targetIndex = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (i == currentIndex) continue;
            if (rows[i].SelectedOptionIndex == 0) break;
            targetIndex++;
        }

        if (currentIndex == targetIndex)
        {
            return;
        }

        rows.Move(currentIndex, targetIndex);
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
