namespace SeminarSched.Domain.Scheduling;

/// <summary>一日当たりに登場する講師の人数（＝異なる講師の出勤日数の合計）を、できるだけ少なく
/// するか多くするかの方針。checkpoint93までは常時「少なく」で固定（weight 1,500）だった機能を、
/// ユーザー要望によりトグル化した。</summary>
public enum TeacherCountPerDayPreference { None = 0, Minimize = 1, Maximize = 2 }

/// <summary>講師ごとの総コマ数の偏りを均等にするかどうかの方針。</summary>
public enum TeacherLoadBalancePreference { None = 0, Balance = 1 }

/// <summary>生徒の授業日を分散させるか、逆にできるだけ少ない日数へ集中させるかの方針。
/// checkpoint93までは常時「分散する」で固定（weight 10,000、日程分散機能）だった機能を、
/// ユーザー要望によりトグル化した。既定を「考慮しない」にすると既存の日程分散が既定でOFFになる
/// ことをユーザーへ確認済み。</summary>
public enum StudentAttendanceDaysPreference { None = 0, Spread = 1, Concentrate = 2 }

/// <summary>個々の講師の出勤日数を、できるだけ少ない日数へ集中させるか、逆に分散させるかの方針。
/// ユーザー要望（checkpoint104）「講師の出勤日について、考慮しない・できるだけ減らす・分散する、
/// を追加してほしい」への対応。<see cref="TeacherCountPerDayPreference"/>（1日あたりに登場する
/// 講師の"人数"、日ごとの視点）とは異なり、こちらは講師1人あたりが何日出勤することになるか
/// （講師ごとの視点）。<see cref="StudentAttendanceDaysPreference"/>の講師版に相当する。</summary>
public enum TeacherAttendanceDaysPreference { None = 0, Spread = 1, Concentrate = 2 }

/// <summary>1コマ（同じ講師・日付・時間帯）あたりの生徒対応人数を、できるだけ多くする
/// （<see cref="SchedulingPolicy.MaxStudentsPerTeacher"/>まで詰める）か、少なくする
/// （1対1に近づける）かの方針。checkpoint93までは常時「多くする」で固定（weight 3,000、
/// ペア優遇機能）だった機能を、ユーザー要望によりトグル化した。③と同じ理由で既定は「考慮しない」。</summary>
public enum PairingSizePreference { None = 0, Maximize = 1, Minimize = 2 }

/// <summary>コマの時間帯（1日の中での時限順）を、できるだけ遅く/早くする方針。</summary>
public enum TimeOfDayPreference { None = 0, Late = 1, Early = 2 }

/// <summary>同一の生徒×講師の組み合わせが、同じ日の隣り合うコマへ続けて配置されることを
/// 優遇するかどうかの方針。ユーザー要望（checkpoint107）「同一講師が同一生徒を連続コマで担当する
/// のを避ける/優遇するについて、避ける意味はあまりないと思うので、『考慮しない』と『できるだけ
/// 連続にする』にして、新たな探索方針としてください」への対応。「避ける」選択肢は意図的に設けて
/// いない（他のPreference系enumと異なり2択）。</summary>
public enum TeacherStudentConsecutivePreference { None = 0, PreferConsecutive = 1 }

/// <summary>
/// ユーザー要望（checkpoint123）「自動作成のオプション機能（探索方針）について、この優先度を
/// 変えられるようにしたい。校舎によっては、一日に入るコマ数＞遅めの時間に入れることを優先の場合が
/// あり、その逆も同様である」への対応。7つのトグル可能な探索方針それぞれを識別するための列挙体。
/// <see cref="SchedulingPolicy.PreferenceOrder"/>がこの7値の並び替え（優先度の高い順）を保持する。
/// <see cref="SchedulingPolicy.MaxConcurrentSeats"/>（ハード制約）と
/// <see cref="SchedulingPolicy.ContinueBeyondNominalTimeIfIncomplete"/>（ソルバー制御フラグ）は
/// 目的関数の重み付けソフト制約ではないため、この並び替え対象に含まれない。
/// </summary>
public enum SchedulingPolicyDimension
{
    TeacherCountPerDay = 0,
    TeacherLoadBalance = 1,
    StudentAttendanceDays = 2,
    TeacherAttendanceDays = 3,
    PairingSize = 4,
    TimeOfDay = 5,
    TeacherStudentConsecutive = 6,
}

/// <summary>
/// プロジェクトごとの最適化探索の方針設定。ユーザー要望「担当する生徒の人数の既定値（1対2）を
/// 変更できるようにしたい（他校舎の1対3・1対4にも対応）」「一日当たりの講師人数・講師ごとのコマ数の
/// 偏り・生徒の授業日・1コマあたりの生徒対応人数・時間帯・同時に使える座席数を自由に選択できる
/// ようにしたい」への対応。
///
/// <see cref="MaxStudentsPerTeacher"/>の既定は2（Python版・従来のWinUI版と同じ1対2）。それ以外の
/// Preference系は既定すべて<c>None</c>（考慮しない）。ただし<see cref="StudentAttendanceDaysPreference"/>
/// （元の日程分散機能）と<see cref="PairingSizePreference"/>（元のペア優遇機能）は、checkpoint93までは
/// 常時ONの機能だったものをトグル化したため、既定をNoneにすると既存プロジェクトの挙動が変わる
/// （日程分散・ペア優遇が既定でOFFになる）。この点はユーザーへ確認の上、既定Noneで統一する方針とした。
///
/// <see cref="MaxConcurrentSeats"/>は「学校（プロジェクト）全体で同時刻に授業を受けられる生徒の
/// 合計人数の上限」（教室ごとの上限ではない）。0は「考慮しない」。
/// </summary>
public sealed record SchedulingPolicy
{
    public SchedulingPolicy(
        int maxStudentsPerTeacher = 2,
        TeacherCountPerDayPreference teacherCountPerDayPreference = TeacherCountPerDayPreference.None,
        TeacherLoadBalancePreference teacherLoadBalancePreference = TeacherLoadBalancePreference.None,
        StudentAttendanceDaysPreference studentAttendanceDaysPreference = StudentAttendanceDaysPreference.None,
        TeacherAttendanceDaysPreference teacherAttendanceDaysPreference = TeacherAttendanceDaysPreference.None,
        PairingSizePreference pairingSizePreference = PairingSizePreference.None,
        TimeOfDayPreference timeOfDayPreference = TimeOfDayPreference.None,
        TeacherStudentConsecutivePreference teacherStudentConsecutivePreference = TeacherStudentConsecutivePreference.None,
        int maxConcurrentSeats = 0,
        bool continueBeyondNominalTimeIfIncomplete = true,
        IReadOnlyList<SchedulingPolicyDimension>? preferenceOrder = null)
    {
        if (maxStudentsPerTeacher is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxStudentsPerTeacher), "1人の講師が同時に担当できる生徒数は1〜10で指定してください。");
        if (maxConcurrentSeats < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentSeats), "同時に使える座席数は0以上で指定してください（0は考慮しない）。");

        preferenceOrder ??= DefaultPreferenceOrder;
        if (preferenceOrder.Count != DefaultPreferenceOrder.Count || preferenceOrder.Distinct().Count() != DefaultPreferenceOrder.Count)
            throw new ArgumentException("探索方針の優先順位は、7つの方針それぞれをちょうど1回ずつ含む必要があります。", nameof(preferenceOrder));

        MaxStudentsPerTeacher = maxStudentsPerTeacher;
        TeacherCountPerDayPreference = teacherCountPerDayPreference;
        TeacherLoadBalancePreference = teacherLoadBalancePreference;
        StudentAttendanceDaysPreference = studentAttendanceDaysPreference;
        TeacherAttendanceDaysPreference = teacherAttendanceDaysPreference;
        PairingSizePreference = pairingSizePreference;
        TimeOfDayPreference = timeOfDayPreference;
        TeacherStudentConsecutivePreference = teacherStudentConsecutivePreference;
        MaxConcurrentSeats = maxConcurrentSeats;
        ContinueBeyondNominalTimeIfIncomplete = continueBeyondNominalTimeIfIncomplete;
        PreferenceOrder = preferenceOrder;
    }

    /// <summary>1人の講師が同時（同じ講師・日付・時間帯）に担当できる生徒数の上限。既定2（1対2）。
    /// 他校舎の1対3・1対4等に対応するため1〜10の範囲で変更可能。</summary>
    public int MaxStudentsPerTeacher { get; }
    public TeacherCountPerDayPreference TeacherCountPerDayPreference { get; }
    public TeacherLoadBalancePreference TeacherLoadBalancePreference { get; }
    public StudentAttendanceDaysPreference StudentAttendanceDaysPreference { get; }
    public TeacherAttendanceDaysPreference TeacherAttendanceDaysPreference { get; }
    public PairingSizePreference PairingSizePreference { get; }
    public TimeOfDayPreference TimeOfDayPreference { get; }
    public TeacherStudentConsecutivePreference TeacherStudentConsecutivePreference { get; }
    /// <summary>学校（プロジェクト）全体で同時刻に授業を受けられる生徒の合計人数の上限。0は考慮しない。</summary>
    public int MaxConcurrentSeats { get; }
    /// <summary>ユーザー要望「一応2倍の時間まで待つのは目安ではあるが、2倍以上の時間を待っても
    /// 別にいい。既定の時間になっても終了しなかった場合に、そのまま継続する、という項目を追加して
    /// ほしい」への対応（checkpoint97）。既定true: 名目時間の2倍（延長1回分）を使い切ってもなお
    /// 未配置が残る場合、3回目・4回目...と延長を繰り返し、完成するか「中断して現在の結果を採用」を
    /// 押すまで粘り続ける（上限なし）。falseにすると従来通り延長は1回のみで、2倍の時間で必ず
    /// 打ち切る。</summary>
    public bool ContinueBeyondNominalTimeIfIncomplete { get; }

    /// <summary>7つの探索方針を、優先度の高い順（目的関数での重みが大きい順）に並べたもの。
    /// ユーザー要望（checkpoint123）「校舎によって、どのオプションを優先するか変えられるように
    /// したい」への対応。既定値<see cref="DefaultPreferenceOrder"/>は、この機能を導入する以前に
    /// 内部的に固定されていた重み付けの大小関係と同じ順序にしてあるため、並び替えを一度も行って
    /// いない既存プロジェクト・新規プロジェクトの挙動は変わらない。</summary>
    public IReadOnlyList<SchedulingPolicyDimension> PreferenceOrder { get; }

    public static readonly IReadOnlyList<SchedulingPolicyDimension> DefaultPreferenceOrder =
    [
        SchedulingPolicyDimension.StudentAttendanceDays,
        SchedulingPolicyDimension.TeacherAttendanceDays,
        SchedulingPolicyDimension.PairingSize,
        SchedulingPolicyDimension.TeacherStudentConsecutive,
        SchedulingPolicyDimension.TeacherCountPerDay,
        SchedulingPolicyDimension.TeacherLoadBalance,
        SchedulingPolicyDimension.TimeOfDay,
    ];

    public static readonly SchedulingPolicy Default = new();

    // レコードが自動生成する既定のEqualsは、PreferenceOrder（IReadOnlyList<T>、実体はarray/List）を
    // 値ではなく参照で比較してしまい、同じ並び順でも別インスタンスなら不一致になる
    // （SqliteSchedulingPolicyRepositoryのGetAsyncは毎回新しい配列を作って返すため、保存→再読込の
    // 往復テストが本来等しいはずの値同士で失敗する）。PreferenceOrderだけSequenceEqualで比較する
    // よう、Equals/GetHashCodeを明示的に上書きする。
    public bool Equals(SchedulingPolicy? other) =>
        other is not null
        && MaxStudentsPerTeacher == other.MaxStudentsPerTeacher
        && TeacherCountPerDayPreference == other.TeacherCountPerDayPreference
        && TeacherLoadBalancePreference == other.TeacherLoadBalancePreference
        && StudentAttendanceDaysPreference == other.StudentAttendanceDaysPreference
        && TeacherAttendanceDaysPreference == other.TeacherAttendanceDaysPreference
        && PairingSizePreference == other.PairingSizePreference
        && TimeOfDayPreference == other.TimeOfDayPreference
        && TeacherStudentConsecutivePreference == other.TeacherStudentConsecutivePreference
        && MaxConcurrentSeats == other.MaxConcurrentSeats
        && ContinueBeyondNominalTimeIfIncomplete == other.ContinueBeyondNominalTimeIfIncomplete
        && PreferenceOrder.SequenceEqual(other.PreferenceOrder);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MaxStudentsPerTeacher);
        hash.Add(TeacherCountPerDayPreference);
        hash.Add(TeacherLoadBalancePreference);
        hash.Add(StudentAttendanceDaysPreference);
        hash.Add(TeacherAttendanceDaysPreference);
        hash.Add(PairingSizePreference);
        hash.Add(TimeOfDayPreference);
        hash.Add(TeacherStudentConsecutivePreference);
        hash.Add(MaxConcurrentSeats);
        hash.Add(ContinueBeyondNominalTimeIfIncomplete);
        foreach (var dimension in PreferenceOrder) hash.Add(dimension);
        return hash.ToHashCode();
    }
}
