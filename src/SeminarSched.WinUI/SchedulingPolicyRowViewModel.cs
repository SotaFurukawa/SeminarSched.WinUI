using System.ComponentModel;
using System.Runtime.CompilerServices;
using SeminarSched.Domain.Scheduling;

namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー要望（checkpoint123）「自動作成のオプション機能（探索方針）について、この優先度を
/// 変えられるようにしたい。カーソルをそれぞれの探索方針のところに持ってくると、右端に上下矢印が
/// 出てくるようになり、それを押すと、探索方針の順序が入れ替わる」への対応。SetupPageの
/// 「⑦最適化探索の方針設定」で、7つの探索方針をユーザーが上下矢印で並べ替えられるようにするための
/// 行ビューモデル。1行が<see cref="SchedulingPolicyDimension"/>1つに対応し、表示用の番号
/// （並び替えに応じて再計算）・選択肢ラベル・選択中インデックスを保持する。
/// </summary>
public sealed class SchedulingPolicyRowViewModel(
    SchedulingPolicyDimension dimension,
    string title,
    IReadOnlyList<string> optionLabels,
    IReadOnlyList<int> optionValues) : INotifyPropertyChanged
{
    public SchedulingPolicyDimension Dimension { get; } = dimension;
    public string Title { get; } = title;
    public IReadOnlyList<string> OptionLabels { get; } = optionLabels;

    /// <summary>OptionLabelsの各位置に対応する、実際のPreference enum序数。表示順と序数が
    /// 一致しないダイメンション（生徒の授業日・講師の出勤日は「考慮しない・できるだけ減らす・
    /// 分散する」の順で表示するが、enumの宣言順はNone・Spread・Concentrate）があるため、
    /// 位置↔値の対応を明示的に保持する。</summary>
    public IReadOnlyList<int> OptionValues { get; } = optionValues;

    private int _selectedOptionIndex;
    public int SelectedOptionIndex
    {
        get => _selectedOptionIndex;
        set
        {
            if (_selectedOptionIndex == value) return;
            _selectedOptionIndex = value;
            OnPropertyChanged();
        }
    }

    private int _displayNumber;
    public int DisplayNumber
    {
        get => _displayNumber;
        set
        {
            if (_displayNumber == value) return;
            _displayNumber = value;
            OnPropertyChanged();
        }
    }

    public int SelectedOptionValue => OptionValues[SelectedOptionIndex];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
