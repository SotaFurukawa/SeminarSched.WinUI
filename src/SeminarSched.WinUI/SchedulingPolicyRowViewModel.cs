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

    /// <summary>各探索方針の表示名・選択肢ラベル・各ラベル位置に対応する実際のPreference enum序数
    /// （生徒の授業日・講師の出勤日は表示順と序数が一致しないため明示的に対応表を持つ）。
    /// SetupPage（プロジェクトの既定値）とOptimizationPage（その回限りの一時上書き）の両方から
    /// 参照する共有定義。</summary>
    public static readonly (SchedulingPolicyDimension Dimension, string Title, string[] Labels, int[] Values)[] DimensionMetadata =
    [
        (SchedulingPolicyDimension.TeacherCountPerDay, "一日当たりの講師人数",
            ["考慮しない", "できるだけ少なくする", "できるだけ多くする"], [0, 1, 2]),
        (SchedulingPolicyDimension.TeacherLoadBalance, "講師ごとのコマ数の偏り",
            ["考慮しない", "均等にする"], [0, 1]),
        (SchedulingPolicyDimension.StudentAttendanceDays, "生徒の授業日",
            ["考慮しない", "できるだけ減らす（同じ日にまとめる）", "分散する"], [0, 2, 1]),
        (SchedulingPolicyDimension.TeacherAttendanceDays, "講師の出勤日",
            ["考慮しない", "できるだけ減らす（同じ日にまとめる）", "分散する"], [0, 2, 1]),
        (SchedulingPolicyDimension.PairingSize, "1コマあたりの生徒の対応人数",
            ["考慮しない", "できるだけ多くする", "できるだけ少なくする"], [0, 1, 2]),
        (SchedulingPolicyDimension.TimeOfDay, "時間帯",
            ["考慮しない", "できるだけ遅くする", "できるだけ早くする"], [0, 1, 2]),
        (SchedulingPolicyDimension.TeacherStudentConsecutive, "同一講師×同一生徒の連続コマ",
            ["考慮しない", "できるだけ連続にする"], [0, 1]),
    ];

    public static List<SchedulingPolicyRowViewModel> BuildRows(SchedulingPolicy policy)
    {
        var currentValueByDimension = new Dictionary<SchedulingPolicyDimension, int>
        {
            [SchedulingPolicyDimension.TeacherCountPerDay] = (int)policy.TeacherCountPerDayPreference,
            [SchedulingPolicyDimension.TeacherLoadBalance] = (int)policy.TeacherLoadBalancePreference,
            [SchedulingPolicyDimension.StudentAttendanceDays] = (int)policy.StudentAttendanceDaysPreference,
            [SchedulingPolicyDimension.TeacherAttendanceDays] = (int)policy.TeacherAttendanceDaysPreference,
            [SchedulingPolicyDimension.PairingSize] = (int)policy.PairingSizePreference,
            [SchedulingPolicyDimension.TimeOfDay] = (int)policy.TimeOfDayPreference,
            [SchedulingPolicyDimension.TeacherStudentConsecutive] = (int)policy.TeacherStudentConsecutivePreference,
        };
        var metadataByDimension = DimensionMetadata.ToDictionary(m => m.Dimension);

        var rows = new List<SchedulingPolicyRowViewModel>();
        var position = 1;
        foreach (var dimension in policy.PreferenceOrder)
        {
            var metadata = metadataByDimension[dimension];
            var optionIndex = Array.IndexOf(metadata.Values, currentValueByDimension[dimension]);
            if (optionIndex < 0) optionIndex = 0;
            rows.Add(new SchedulingPolicyRowViewModel(dimension, metadata.Title, metadata.Labels, metadata.Values)
            {
                SelectedOptionIndex = optionIndex,
                DisplayNumber = position++,
            });
        }

        return rows;
    }

    public static SchedulingPolicy BuildPolicy(
        IReadOnlyList<SchedulingPolicyRowViewModel> rows,
        int maxStudentsPerTeacher,
        int maxConcurrentSeats,
        bool continueBeyondNominalTimeIfIncomplete)
    {
        var valueByDimension = rows.ToDictionary(row => row.Dimension, row => row.SelectedOptionValue);
        var order = rows.Select(row => row.Dimension).ToArray();
        return new SchedulingPolicy(
            maxStudentsPerTeacher,
            (TeacherCountPerDayPreference)valueByDimension[SchedulingPolicyDimension.TeacherCountPerDay],
            (TeacherLoadBalancePreference)valueByDimension[SchedulingPolicyDimension.TeacherLoadBalance],
            (StudentAttendanceDaysPreference)valueByDimension[SchedulingPolicyDimension.StudentAttendanceDays],
            (TeacherAttendanceDaysPreference)valueByDimension[SchedulingPolicyDimension.TeacherAttendanceDays],
            (PairingSizePreference)valueByDimension[SchedulingPolicyDimension.PairingSize],
            (TimeOfDayPreference)valueByDimension[SchedulingPolicyDimension.TimeOfDay],
            (TeacherStudentConsecutivePreference)valueByDimension[SchedulingPolicyDimension.TeacherStudentConsecutive],
            maxConcurrentSeats,
            continueBeyondNominalTimeIfIncomplete,
            order);
    }
}
