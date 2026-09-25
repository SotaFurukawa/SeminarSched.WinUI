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
        int maxConcurrentSeats = 0,
        bool continueBeyondNominalTimeIfIncomplete = true)
    {
        if (maxStudentsPerTeacher is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxStudentsPerTeacher), "1人の講師が同時に担当できる生徒数は1〜10で指定してください。");
        if (maxConcurrentSeats < 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentSeats), "同時に使える座席数は0以上で指定してください（0は考慮しない）。");

        MaxStudentsPerTeacher = maxStudentsPerTeacher;
        TeacherCountPerDayPreference = teacherCountPerDayPreference;
        TeacherLoadBalancePreference = teacherLoadBalancePreference;
        StudentAttendanceDaysPreference = studentAttendanceDaysPreference;
        TeacherAttendanceDaysPreference = teacherAttendanceDaysPreference;
        PairingSizePreference = pairingSizePreference;
        TimeOfDayPreference = timeOfDayPreference;
        MaxConcurrentSeats = maxConcurrentSeats;
        ContinueBeyondNominalTimeIfIncomplete = continueBeyondNominalTimeIfIncomplete;
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
    /// <summary>学校（プロジェクト）全体で同時刻に授業を受けられる生徒の合計人数の上限。0は考慮しない。</summary>
    public int MaxConcurrentSeats { get; }
    /// <summary>ユーザー要望「一応2倍の時間まで待つのは目安ではあるが、2倍以上の時間を待っても
    /// 別にいい。既定の時間になっても終了しなかった場合に、そのまま継続する、という項目を追加して
    /// ほしい」への対応（checkpoint97）。既定true: 名目時間の2倍（延長1回分）を使い切ってもなお
    /// 未配置が残る場合、3回目・4回目...と延長を繰り返し、完成するか「中断して現在の結果を採用」を
    /// 押すまで粘り続ける（上限なし）。falseにすると従来通り延長は1回のみで、2倍の時間で必ず
    /// 打ち切る。</summary>
    public bool ContinueBeyondNominalTimeIfIncomplete { get; }

    public static readonly SchedulingPolicy Default = new();
}
