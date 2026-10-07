namespace SeminarSched.Domain.GroupLessons;

// Python版の集団授業機能はv1.6.0以降UI停止中で、かつ「1回の開講」を1行にする設計（同一クラスが
// 複数日程で開講される場合はgroup_codeを変えて複数行必要）だった。本版はユーザー指示に基づき、
// 「クラス」を1つの単位として先に登録し（Name・対象Grade）、その開講日程（複数可）を
// GroupLessonSession、受講生をGroupLessonEnrollmentとして別途ひも付ける設計にした
// （同じ学年で複数クラスを持てる、受講登録はクラス単位で1回で済む）。
public sealed record GroupLessonClass
{
    public GroupLessonClass(long id, string name, string grade, string subject, bool allowOtherGrades = false, bool active = true, long? teacherId = null, int minGapMinutes = 0)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(grade);
        Id = id;
        Name = name.Trim();
        Grade = grade.Trim();
        // UI（3.1画面）では必須入力として扱うが、列追加前に作成された既存クラス（値が空文字列）を
        // 読み戻せなくなるとGetClassesAsync全体が壊れるため、ドメイン型自体は空文字列を許容する。
        Subject = subject?.Trim() ?? "";
        AllowOtherGrades = allowOtherGrades;
        Active = active;
        TeacherId = teacherId;
        MinGapMinutes = minGapMinutes;
    }

    public long Id { get; init; }
    public string Name { get; }
    public string Grade { get; }

    // ①設定のSubjectマスタとは連動しない自由入力（プルダウンではなく手入力にしてほしいという
    // ユーザー指示のため）。集団授業のクラス名だけでは科目が分からない場合があるための表示用途。
    public string Subject { get; }

    // trueの場合、3.2の受講登録で対象学年による絞り込みを行わず全学年の生徒を選択対象にする
    // （先取り授業などクラスの対象学年以外の生徒も受講する場合向け）。
    public bool AllowOtherGrades { get; }
    public bool Active { get; }

    // ユーザー要望（checkpoint112）「集団授業のクラスに、担当講師（任意）チェックボックスを追加し、
    // チェックした場合は講師選択欄が表示される。ここで講師を割り当てると、その講師はその日時に
    // 個別授業を持てないようにブロックする」への対応。任意（null=未割り当て）。設定すると、この
    // クラスの全開講セッション（GroupLessonSession）と時間帯が重なるコマについて、この講師の
    // TeacherUnavailabilityが自動生成され、自動作成・手動配置の両方でブロックされる
    // （SqliteGroupLessonService.RecomputeTeacherBlocksForClassAsync参照）。
    public long? TeacherId { get; init; }

    // ユーザー要望（checkpoint155）「集団授業と個別指導の間の空き時間の最小値を指定させる。負数も
    // 入力可とする」への対応。担当講師（TeacherId）を割り当てた場合にだけ効果を持つ
    // （RecomputeTeacherBlocksForClassAsync参照）。正の値＝開講時間の前後にこの分の空き時間を
    // 要求する（ブロックする範囲を広げる）。負の値＝開講時間の前後この分までは個別指導との重なりを
    // 許容する（ブロックする範囲を狭める。例えば個別指導の開始・終了を5分ずらせば参加できる程度の
    // 重なりであれば、-5を設定することでその枠にも個別指導を配置できるようになる）。0＝開講時間
    // そのままの重なりだけをブロックする（既定）。
    public int MinGapMinutes { get; init; }
}
