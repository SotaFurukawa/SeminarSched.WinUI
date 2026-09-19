namespace SeminarSched.Domain.GroupLessons;

// Python版の集団授業機能はv1.6.0以降UI停止中で、かつ「1回の開講」を1行にする設計（同一クラスが
// 複数日程で開講される場合はgroup_codeを変えて複数行必要）だった。本版はユーザー指示に基づき、
// 「クラス」を1つの単位として先に登録し（Name・対象Grade）、その開講日程（複数可）を
// GroupLessonSession、受講生をGroupLessonEnrollmentとして別途ひも付ける設計にした
// （同じ学年で複数クラスを持てる、受講登録はクラス単位で1回で済む）。
public sealed record GroupLessonClass
{
    public GroupLessonClass(long id, string name, string grade, bool allowOtherGrades = false, bool active = true)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(grade);
        Id = id;
        Name = name.Trim();
        Grade = grade.Trim();
        AllowOtherGrades = allowOtherGrades;
        Active = active;
    }

    public long Id { get; init; }
    public string Name { get; }
    public string Grade { get; }

    // trueの場合、3.2の受講登録で対象学年による絞り込みを行わず全学年の生徒を選択対象にする
    // （先取り授業などクラスの対象学年以外の生徒も受講する場合向け）。
    public bool AllowOtherGrades { get; }
    public bool Active { get; }
}
