using SeminarSched.Domain.CourseSettings;
using SeminarSched.Domain.MasterData;
using SeminarSched_WinUI;

namespace SeminarSched_WinUI.Pages;

// EditableRowViewModel（共通基底）はEditableRowViewModel.csへ切り出した
// （ImportPage.xamlの受講希望一覧でも同じ行内編集パターンを使うため）。

public sealed class StudentRowViewModel : EditableRowViewModel
{
    public Student? Value { get; private set; }

    private StudentRowViewModel() { }
    public static StudentRowViewModel ForExisting(Student value) => new() { Value = value };
    public static StudentRowViewModel CreateNew(string previewExternalId) =>
        (StudentRowViewModel)ApplyNew(new StudentRowViewModel { PreviewExternalId = previewExternalId });

    /// <summary>新規行で、保存するまで確定しない採番予定の生徒ID（表示専用、編集不可）。</summary>
    public string PreviewExternalId { get; private init; } = "";
    public string ExternalIdText => Value?.ExternalId ?? PreviewExternalId;

    public string StatusText => Value is { Active: true } ? "在籍中" : "卒業・無効";
    public string AllowGapText => Value is { AllowGap: true } ? "あり" : "なし";

    private string _draftFamilyName = "";
    public string DraftFamilyName { get => _draftFamilyName; set { if (_draftFamilyName == value) return; _draftFamilyName = value; OnPropertyChanged(); } }
    private string _draftGivenName = "";
    public string DraftGivenName { get => _draftGivenName; set { if (_draftGivenName == value) return; _draftGivenName = value; OnPropertyChanged(); } }
    private string _draftGrade = "";
    public string DraftGrade { get => _draftGrade; set { if (_draftGrade == value) return; _draftGrade = value; OnPropertyChanged(); } }
    private double _draftMaxConsecutiveSlots = 2;
    public double DraftMaxConsecutiveSlots { get => _draftMaxConsecutiveSlots; set { if (_draftMaxConsecutiveSlots == value) return; _draftMaxConsecutiveSlots = value; OnPropertyChanged(); } }
    private bool _draftAllowGap;
    public bool DraftAllowGap { get => _draftAllowGap; set { if (_draftAllowGap == value) return; _draftAllowGap = value; OnPropertyChanged(); } }
    private bool _draftActive = true;
    public bool DraftActive { get => _draftActive; set { if (_draftActive == value) return; _draftActive = value; OnPropertyChanged(); } }
    private string _draftNote = "";
    public string DraftNote { get => _draftNote; set { if (_draftNote == value) return; _draftNote = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftFamilyName = Value?.FamilyName ?? ""; DraftGivenName = Value?.GivenName ?? ""; DraftGrade = Value?.Grade ?? "";
        DraftMaxConsecutiveSlots = Value?.DefaultMaxConsecutiveSlots ?? 2;
        DraftAllowGap = Value?.AllowGap ?? false; DraftActive = Value?.Active ?? true; DraftNote = Value?.Note ?? "";
    }

    /// <summary>ReloadAsync後、編集中だった行のDraft値を新しいインスタンスへ引き継ぐときに使う。</summary>
    public void CopyDraftFrom(StudentRowViewModel other)
    {
        DraftFamilyName = other.DraftFamilyName; DraftGivenName = other.DraftGivenName; DraftGrade = other.DraftGrade; DraftMaxConsecutiveSlots = other.DraftMaxConsecutiveSlots;
        DraftAllowGap = other.DraftAllowGap; DraftActive = other.DraftActive; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        (Value is { } v && $"{v.ExternalId}{v.FullName}{v.Grade}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

public sealed class TeacherRowViewModel : EditableRowViewModel
{
    public Teacher? Value { get; private set; }

    private TeacherRowViewModel() { }
    public static TeacherRowViewModel ForExisting(Teacher value) => new() { Value = value };
    public static TeacherRowViewModel CreateNew(string previewExternalId) =>
        (TeacherRowViewModel)ApplyNew(new TeacherRowViewModel { PreviewExternalId = previewExternalId });

    public string PreviewExternalId { get; private init; } = "";
    public string ExternalIdText => Value?.ExternalId ?? PreviewExternalId;

    public string StatusText => Value is { Active: true } ? "在籍中" : "卒業・無効";
    public string AllowGapText => Value is { AllowGap: true } ? "あり" : "なし";

    private string _draftFamilyName = "";
    public string DraftFamilyName { get => _draftFamilyName; set { if (_draftFamilyName == value) return; _draftFamilyName = value; OnPropertyChanged(); } }
    private string _draftGivenName = "";
    public string DraftGivenName { get => _draftGivenName; set { if (_draftGivenName == value) return; _draftGivenName = value; OnPropertyChanged(); } }
    private bool _draftAllowGap;
    public bool DraftAllowGap { get => _draftAllowGap; set { if (_draftAllowGap == value) return; _draftAllowGap = value; OnPropertyChanged(); } }
    private bool _draftActive = true;
    public bool DraftActive { get => _draftActive; set { if (_draftActive == value) return; _draftActive = value; OnPropertyChanged(); } }
    private string _draftNote = "";
    public string DraftNote { get => _draftNote; set { if (_draftNote == value) return; _draftNote = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftFamilyName = Value?.FamilyName ?? ""; DraftGivenName = Value?.GivenName ?? ""; DraftAllowGap = Value?.AllowGap ?? false;
        DraftActive = Value?.Active ?? true; DraftNote = Value?.Note ?? "";
    }

    public void CopyDraftFrom(TeacherRowViewModel other)
    {
        DraftFamilyName = other.DraftFamilyName; DraftGivenName = other.DraftGivenName; DraftAllowGap = other.DraftAllowGap; DraftActive = other.DraftActive; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        (Value is { } v && $"{v.ExternalId}{v.FullName}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

// ユーザー要望（checkpoint145/146で承認されたPlanのStage 3）「通常授業担当設定とコマについても、
// 生徒・講師ページと同様の仕様にしてほしい」への対応。ImportPageRowViewModels.csの
// LessonRequestRowViewModelと同じ考え方で、ComboBoxの選択肢（生徒・科目・通常担当講師）を
// 行オブジェクト自身に持たせる。SaveRegularLessonAsyncのUPSERTキーは(StudentId, SubjectId)で
// Idではないため（Idは常に0で送られる）、既存行の編集中に生徒・科目を変更すると別レコードが
// 新規作成されてしまう。これを避けるため、既存行（IsNew=false）では生徒・科目のComboBoxを
// IsEnabled="{x:Bind IsNew}"で読み取り専用にし、新規追加時のみ選択可能にする（SetupPage.xaml参照）。
public sealed class RegularLessonRowViewModel : EditableRowViewModel
{
    public RegularLessonProfile? Value { get; private set; }
    public string StudentName { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public string TeacherName { get; private set; } = "";

    public IReadOnlyList<NamedOption<Student>> StudentOptions { get; private init; } = [];
    public IReadOnlyList<NamedOption<Subject>> SubjectOptions { get; private init; } = [];
    public IReadOnlyList<NamedOption<Teacher?>> TeacherOptions { get; private init; } = [];

    public string OneToOneText => Value is { OneToOneRequired: true } ? "1対1" : "通常";

    private RegularLessonRowViewModel() { }

    public static RegularLessonRowViewModel ForExisting(
        RegularLessonProfile value, string studentName, string subjectName, string teacherName,
        IReadOnlyList<NamedOption<Student>> studentOptions, IReadOnlyList<NamedOption<Subject>> subjectOptions, IReadOnlyList<NamedOption<Teacher?>> teacherOptions) =>
        new()
        {
            Value = value, StudentName = studentName, SubjectName = subjectName, TeacherName = teacherName,
            StudentOptions = studentOptions, SubjectOptions = subjectOptions, TeacherOptions = teacherOptions,
        };

    public static RegularLessonRowViewModel CreateNew(
        IReadOnlyList<NamedOption<Student>> studentOptions, IReadOnlyList<NamedOption<Subject>> subjectOptions, IReadOnlyList<NamedOption<Teacher?>> teacherOptions) =>
        (RegularLessonRowViewModel)ApplyNew(new RegularLessonRowViewModel
        {
            StudentOptions = studentOptions, SubjectOptions = subjectOptions, TeacherOptions = teacherOptions,
        });

    private NamedOption<Student>? _draftStudent;
    public NamedOption<Student>? DraftStudent { get => _draftStudent; set { if (Equals(_draftStudent, value)) return; _draftStudent = value; OnPropertyChanged(); } }
    private NamedOption<Subject>? _draftSubject;
    public NamedOption<Subject>? DraftSubject { get => _draftSubject; set { if (Equals(_draftSubject, value)) return; _draftSubject = value; OnPropertyChanged(); } }
    private NamedOption<Teacher?>? _draftTeacher;
    public NamedOption<Teacher?>? DraftTeacher { get => _draftTeacher; set { if (Equals(_draftTeacher, value)) return; _draftTeacher = value; OnPropertyChanged(); } }
    private double _draftPriority = 3;
    public double DraftPriority { get => _draftPriority; set { if (_draftPriority == value) return; _draftPriority = value; OnPropertyChanged(); } }
    private int _draftOneToOneIndex;
    public int DraftOneToOneIndex { get => _draftOneToOneIndex; set { if (_draftOneToOneIndex == value) return; _draftOneToOneIndex = value; OnPropertyChanged(); } }
    private string _draftNote = "";
    public string DraftNote { get => _draftNote; set { if (_draftNote == value) return; _draftNote = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftStudent = Value is null ? null : StudentOptions.FirstOrDefault(o => o.Value.Id == Value.StudentId);
        DraftSubject = Value is null ? null : SubjectOptions.FirstOrDefault(o => o.Value.Id == Value.SubjectId);
        DraftTeacher = TeacherOptions.FirstOrDefault(o => o.Value?.Id == Value?.RegularTeacherId) ?? TeacherOptions.FirstOrDefault();
        DraftPriority = Value?.RegularTeacherPriority ?? 3;
        DraftOneToOneIndex = Value is { OneToOneRequired: true } ? 1 : 0;
        DraftNote = Value?.Note ?? "";
    }

    public void CopyDraftFrom(RegularLessonRowViewModel other)
    {
        DraftStudent = other.DraftStudent; DraftSubject = other.DraftSubject; DraftTeacher = other.DraftTeacher;
        DraftPriority = other.DraftPriority; DraftOneToOneIndex = other.DraftOneToOneIndex; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        $"{StudentName}{SubjectName}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

public sealed class SubjectRowViewModel : EditableRowViewModel
{
    public Subject? Value { get; private set; }

    private SubjectRowViewModel() { }
    public static SubjectRowViewModel ForExisting(Subject value) => new() { Value = value };
    public static SubjectRowViewModel CreateNew(int nextSortOrder) => (SubjectRowViewModel)ApplyNew(new SubjectRowViewModel { _draftSortOrder = nextSortOrder });

    public string StatusText => Value is { Active: true } ? "有効" : "停止";

    private string _draftCode = "";
    public string DraftCode { get => _draftCode; set { if (_draftCode == value) return; _draftCode = value; OnPropertyChanged(); } }
    private string _draftName = "";
    public string DraftName { get => _draftName; set { if (_draftName == value) return; _draftName = value; OnPropertyChanged(); } }
    private string _draftShortName = "";
    public string DraftShortName { get => _draftShortName; set { if (_draftShortName == value) return; _draftShortName = value; OnPropertyChanged(); } }
    private string _draftSchoolLevel = "";
    public string DraftSchoolLevel { get => _draftSchoolLevel; set { if (_draftSchoolLevel == value) return; _draftSchoolLevel = value; OnPropertyChanged(); } }
    private double _draftSortOrder = 1;
    public double DraftSortOrder { get => _draftSortOrder; set { if (_draftSortOrder == value) return; _draftSortOrder = value; OnPropertyChanged(); } }
    private bool _draftActive = true;
    public bool DraftActive { get => _draftActive; set { if (_draftActive == value) return; _draftActive = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        if (Value is null) return; // 新規行はCreateNewで設定済みのSortOrderを保つ（既定値へ戻さない）
        DraftCode = Value.Code; DraftName = Value.DisplayName; DraftShortName = Value.ShortName;
        DraftSchoolLevel = Value.SchoolLevel; DraftSortOrder = Value.SortOrder; DraftActive = Value.Active;
    }

    public void CopyDraftFrom(SubjectRowViewModel other)
    {
        DraftCode = other.DraftCode; DraftName = other.DraftName; DraftShortName = other.DraftShortName;
        DraftSchoolLevel = other.DraftSchoolLevel; DraftSortOrder = other.DraftSortOrder; DraftActive = other.DraftActive;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        (Value is { } v && $"{v.Code}{v.DisplayName}{v.SchoolLevel}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
}

// ユーザー要望（checkpoint145/146で承認されたPlanのStage 3）「コマについても、生徒・講師ページと
// 同様の仕様にしてほしい」への対応。ただし▲▼（並び替え）・×（削除）ボタンは元々このタブの
// 一覧で常時表示になっており、ホバー表示方式へ無理に合わせず既存の挙動を維持する（新設の「変更」
// ボタンも常時表示）。並び順（SortOrder）は▲▼ボタンで別経路（MoveSlotAsync）から直接更新する
// ため、このクラスのDraftプロパティには含めない（既存行は保存時にValue.SortOrderをそのまま
// 使い、新規行はCreateNewで渡された次の順序を使う＝SubjectRowViewModelと同じ考え方）。
public sealed class TimeSlotRowViewModel : EditableRowViewModel
{
    public TimeSlot? Value { get; private set; }
    private int _newSortOrder;
    public int EffectiveSortOrder => Value?.SortOrder ?? _newSortOrder;

    public string StartText => Value is null ? "" : TimeOfDayOptions.Format(Value.StartTime);
    public string EndText => Value is null ? "" : TimeOfDayOptions.Format(Value.EndTime);
    public string StatusText => Value is { Active: true } ? "有効" : "停止";
    public IReadOnlyList<string> TimeOfDayValues => TimeOfDayOptions.Values;

    private TimeSlotRowViewModel() { }
    public static TimeSlotRowViewModel ForExisting(TimeSlot value) => new() { Value = value };
    public static TimeSlotRowViewModel CreateNew(int nextSortOrder) => (TimeSlotRowViewModel)ApplyNew(new TimeSlotRowViewModel { _newSortOrder = nextSortOrder });

    private string _draftCode = "";
    public string DraftCode { get => _draftCode; set { if (_draftCode == value) return; _draftCode = value; OnPropertyChanged(); } }
    private string _draftDisplayName = "";
    public string DraftDisplayName { get => _draftDisplayName; set { if (_draftDisplayName == value) return; _draftDisplayName = value; OnPropertyChanged(); } }
    private string _draftStartText = "09:00";
    public string DraftStartText { get => _draftStartText; set { if (_draftStartText == value) return; _draftStartText = value; OnPropertyChanged(); } }
    private string _draftEndText = "10:00";
    public string DraftEndText { get => _draftEndText; set { if (_draftEndText == value) return; _draftEndText = value; OnPropertyChanged(); } }
    private bool _draftActive = true;
    public bool DraftActive { get => _draftActive; set { if (_draftActive == value) return; _draftActive = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftCode = Value?.Code ?? ""; DraftDisplayName = Value?.DisplayName ?? "";
        DraftStartText = Value is null ? "09:00" : TimeOfDayOptions.Format(Value.StartTime);
        DraftEndText = Value is null ? "10:00" : TimeOfDayOptions.Format(Value.EndTime);
        DraftActive = Value?.Active ?? true;
    }

    public void CopyDraftFrom(TimeSlotRowViewModel other)
    {
        DraftCode = other.DraftCode; DraftDisplayName = other.DraftDisplayName;
        DraftStartText = other.DraftStartText; DraftEndText = other.DraftEndText; DraftActive = other.DraftActive;
    }
}
