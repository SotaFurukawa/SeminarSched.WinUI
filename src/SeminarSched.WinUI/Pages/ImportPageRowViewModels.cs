using SeminarSched.Domain.MasterData;

namespace SeminarSched_WinUI.Pages;

/// <summary>ComboBoxの選択肢用の小さな表示ラッパー。ImportPage.xamlの「受講希望」一覧では、
/// 行ごとのComboBox（生徒・科目・通常担当講師・第1〜3希望講師）がその行自身の
/// <see cref="LessonRequestRowViewModel.StudentOptions"/>等へx:Bindするため、ページ側の
/// private MasterItem&lt;T&gt;（SetupPage/ImportPageそれぞれが個別に持つ、ComboBox表示専用の
/// 既存の仕組み）には依存しない独立した型として用意した。</summary>
public sealed record NamedOption<T>(T Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// ユーザー要望（checkpoint142）「アンケート取込後の一覧についても、設定の生徒、講師、科目と
/// 同様に追加できるようにする。最大連続コマ数と第○○希望講師、空きコマ許可についても、右側に
/// 列を追加して、一覧に表示」への対応。<see cref="EditableRowViewModel"/>と同じ行内編集
/// パターンだが、<see cref="LessonRequest"/>は外部キー（生徒・科目・通常担当講師・第1〜3希望
/// 講師）を多く持つため、ComboBoxの選択肢（Student/Subject/Teacher?の一覧）を行オブジェクト
/// 自身に持たせる（<see cref="StudentOptions"/>等）。フィールド数が多く1行に収まらないため、
/// 編集パネルはStudent/Teacher/SubjectRowViewModelと異なり複数行のレイアウト（旧フォームと
/// 同じ構成）になる。
/// </summary>
public sealed class LessonRequestRowViewModel : EditableRowViewModel
{
    public LessonRequest? Value { get; private set; }
    public string StudentName { get; private set; } = "";
    public string Grade { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public string RegularTeacherName { get; private set; } = "";
    public string Preferred1Name { get; private set; } = "";
    public string Preferred2Name { get; private set; } = "";
    public string Preferred3Name { get; private set; } = "";

    public IReadOnlyList<NamedOption<Student>> StudentOptions { get; private init; } = [];
    public IReadOnlyList<NamedOption<Subject>> SubjectOptions { get; private init; } = [];
    public IReadOnlyList<NamedOption<Teacher?>> TeacherOptions { get; private init; } = [];

    // ユーザー要望（checkpoint148）「空きコマ上書きについて、指定なしの場合は許可か不許可かを
    // 生徒個人の設定を表示してください。最大連続コマ数も同様。上書きという言葉を用いず、数字と
    // 許可不許可で書くこと」への対応。この受講希望のStudentId上書きが無い場合に実際に使われる値
    // （生徒本人のDefaultMaxConsecutiveSlots/AllowGap）をReloadLessonRequestsAsyncから渡しておき、
    // 「既定値」「指定なし」といったプレースホルダーではなく、実際に効く数値・許可/不許可を
    // そのまま表示する（SqliteScheduleRunService.csのCOALESCE(r.MaxConsecutiveSlotsOverride,
    // s.DefaultMaxConsecutiveSlots)等と同じ解決順）。
    public int StudentDefaultMaxConsecutiveSlots { get; private init; } = 2;
    public bool StudentDefaultAllowGap { get; private init; }

    public string OneToOneText => Value is { OneToOneRequired: true } ? "○" : "";
    public string MaxConsecutiveText => (Value?.MaxConsecutiveSlotsOverride ?? StudentDefaultMaxConsecutiveSlots).ToString();
    public string AllowGapText => (Value?.AllowGapOverride ?? StudentDefaultAllowGap) ? "許可" : "不許可";

    private LessonRequestRowViewModel() { }

    public static LessonRequestRowViewModel ForExisting(
        LessonRequest value, string studentName, string grade, string subjectName, string regularTeacherName,
        string preferred1Name, string preferred2Name, string preferred3Name,
        int studentDefaultMaxConsecutiveSlots, bool studentDefaultAllowGap,
        IReadOnlyList<NamedOption<Student>> studentOptions, IReadOnlyList<NamedOption<Subject>> subjectOptions, IReadOnlyList<NamedOption<Teacher?>> teacherOptions) =>
        new()
        {
            Value = value, StudentName = studentName, Grade = grade, SubjectName = subjectName, RegularTeacherName = regularTeacherName,
            Preferred1Name = preferred1Name, Preferred2Name = preferred2Name, Preferred3Name = preferred3Name,
            StudentDefaultMaxConsecutiveSlots = studentDefaultMaxConsecutiveSlots, StudentDefaultAllowGap = studentDefaultAllowGap,
            StudentOptions = studentOptions, SubjectOptions = subjectOptions, TeacherOptions = teacherOptions,
        };

    public static LessonRequestRowViewModel CreateNew(
        IReadOnlyList<NamedOption<Student>> studentOptions, IReadOnlyList<NamedOption<Subject>> subjectOptions, IReadOnlyList<NamedOption<Teacher?>> teacherOptions) =>
        (LessonRequestRowViewModel)ApplyNew(new LessonRequestRowViewModel
        {
            StudentOptions = studentOptions, SubjectOptions = subjectOptions, TeacherOptions = teacherOptions,
        });

    private NamedOption<Student>? _draftStudent;
    public NamedOption<Student>? DraftStudent { get => _draftStudent; set { if (Equals(_draftStudent, value)) return; _draftStudent = value; OnPropertyChanged(); } }
    private NamedOption<Subject>? _draftSubject;
    public NamedOption<Subject>? DraftSubject { get => _draftSubject; set { if (Equals(_draftSubject, value)) return; _draftSubject = value; OnPropertyChanged(); } }
    private double _draftRequiredSessions = 1;
    public double DraftRequiredSessions { get => _draftRequiredSessions; set { if (_draftRequiredSessions == value) return; _draftRequiredSessions = value; OnPropertyChanged(); } }
    private NamedOption<Teacher?>? _draftRegularTeacher;
    public NamedOption<Teacher?>? DraftRegularTeacher
    {
        get => _draftRegularTeacher;
        set
        {
            if (Equals(_draftRegularTeacher, value)) return;
            _draftRegularTeacher = value;
            OnPropertyChanged();
            // ユーザー指示: 第1希望講師は普通、通常担当講師と同じになるため、通常担当講師を選ぶと
            // 第1希望講師が未設定（指定なし）のままであれば自動的に同じ講師を初期値として補う
            // （旧フォームのRequestRegularTeacher_SelectionChangedと同じ挙動）。第1希望を
            // すでに選んでいる場合（既存データの編集時含む）は上書きしない。
            if (value?.Value is not null && DraftPreferred1?.Value is null) DraftPreferred1 = value;
        }
    }
    private double _draftRegularPriority = 3;
    public double DraftRegularPriority { get => _draftRegularPriority; set { if (_draftRegularPriority == value) return; _draftRegularPriority = value; OnPropertyChanged(); } }
    private NamedOption<Teacher?>? _draftPreferred1;
    public NamedOption<Teacher?>? DraftPreferred1 { get => _draftPreferred1; set { if (Equals(_draftPreferred1, value)) return; _draftPreferred1 = value; OnPropertyChanged(); } }
    private NamedOption<Teacher?>? _draftPreferred2;
    public NamedOption<Teacher?>? DraftPreferred2 { get => _draftPreferred2; set { if (Equals(_draftPreferred2, value)) return; _draftPreferred2 = value; OnPropertyChanged(); } }
    private NamedOption<Teacher?>? _draftPreferred3;
    public NamedOption<Teacher?>? DraftPreferred3 { get => _draftPreferred3; set { if (Equals(_draftPreferred3, value)) return; _draftPreferred3 = value; OnPropertyChanged(); } }
    private int _draftOneToOneIndex;
    public int DraftOneToOneIndex { get => _draftOneToOneIndex; set { if (_draftOneToOneIndex == value) return; _draftOneToOneIndex = value; OnPropertyChanged(); } }
    private double _draftMaxConsecutiveOverride;
    public double DraftMaxConsecutiveOverride { get => _draftMaxConsecutiveOverride; set { if (_draftMaxConsecutiveOverride == value) return; _draftMaxConsecutiveOverride = value; OnPropertyChanged(); } }
    private int _draftAllowGapOverrideIndex;
    public int DraftAllowGapOverrideIndex { get => _draftAllowGapOverrideIndex; set { if (_draftAllowGapOverrideIndex == value) return; _draftAllowGapOverrideIndex = value; OnPropertyChanged(); } }
    private string _draftNote = "";
    public string DraftNote { get => _draftNote; set { if (_draftNote == value) return; _draftNote = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftStudent = Value is null ? null : StudentOptions.FirstOrDefault(o => o.Value.Id == Value.StudentId);
        DraftSubject = Value is null ? null : SubjectOptions.FirstOrDefault(o => o.Value.Id == Value.SubjectId);
        DraftRequiredSessions = Value?.RequiredSessions ?? 1;
        DraftRegularTeacher = TeacherOptions.FirstOrDefault(o => o.Value?.Id == Value?.RegularTeacherId) ?? TeacherOptions.FirstOrDefault();
        DraftRegularPriority = Value?.RegularTeacherPriority ?? 3;
        DraftPreferred1 = TeacherOptions.FirstOrDefault(o => o.Value?.Id == Value?.PreferredTeacher1Id) ?? TeacherOptions.FirstOrDefault();
        DraftPreferred2 = TeacherOptions.FirstOrDefault(o => o.Value?.Id == Value?.PreferredTeacher2Id) ?? TeacherOptions.FirstOrDefault();
        DraftPreferred3 = TeacherOptions.FirstOrDefault(o => o.Value?.Id == Value?.PreferredTeacher3Id) ?? TeacherOptions.FirstOrDefault();
        DraftOneToOneIndex = Value is { OneToOneRequired: true } ? 1 : 0;
        DraftMaxConsecutiveOverride = Value?.MaxConsecutiveSlotsOverride ?? 0;
        DraftAllowGapOverrideIndex = Value?.AllowGapOverride switch { true => 1, false => 2, _ => 0 };
        DraftNote = Value?.Note ?? "";
    }

    public void CopyDraftFrom(LessonRequestRowViewModel other)
    {
        DraftStudent = other.DraftStudent; DraftSubject = other.DraftSubject; DraftRequiredSessions = other.DraftRequiredSessions;
        DraftRegularTeacher = other.DraftRegularTeacher; DraftRegularPriority = other.DraftRegularPriority;
        DraftPreferred1 = other.DraftPreferred1; DraftPreferred2 = other.DraftPreferred2; DraftPreferred3 = other.DraftPreferred3;
        DraftOneToOneIndex = other.DraftOneToOneIndex; DraftMaxConsecutiveOverride = other.DraftMaxConsecutiveOverride;
        DraftAllowGapOverrideIndex = other.DraftAllowGapOverrideIndex; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        $"{StudentName}{SubjectName}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
