using SeminarSched.Domain.MasterData;

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
