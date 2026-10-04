using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using SeminarSched.Domain.MasterData;

namespace SeminarSched_WinUI.Pages;

/// <summary>
/// ユーザー要望（checkpoint142）「生徒・講師・科目について、上側で入力させる形式ではないように
/// したい」への対応。常時表示の入力フォーム＋下部一覧という構成をやめ、一覧の行そのものを
/// 「追加」ボタンで末尾に出す新規行、または既存行への「変更」ボタンで、その場で編集できる形に
/// 変える。SetupPage.xamlの探索方針並び替えリスト（<see cref="SchedulingPolicyRowViewModel"/>）と
/// 同じ「可変なINotifyPropertyChangedクラス＋x:Bindでプロパティ変更を反映」という流儀を転用する。
/// 1つのDataTemplate内に表示用Gridと編集用Gridを両方置き、<see cref="IsEditing"/>から導出する
/// <see cref="DisplayVisibility"/>/<see cref="EditVisibility"/>で出し分ける
/// （DataTemplateSelectorは使わない＝ListViewの仮想化でコンテナが使い回されても安全）。
/// </summary>
public abstract class EditableRowViewModel : INotifyPropertyChanged
{
    /// <summary>新規追加の行かどうか。true の間は保存成功までレコードIDを持たない。</summary>
    public bool IsNew { get; private set; }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            if (value) ResetDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayVisibility));
            OnPropertyChanged(nameof(EditVisibility));
        }
    }

    public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>編集開始時（新規行の初期生成も含む）に、Draft*プロパティを現在の保存済み値
    /// （新規行なら既定値）へ戻す。保存済みの不変recordは直接編集できないため、編集中はDraft側の
    /// 可変プロパティをTextBox等へ双方向バインドし、保存時にだけ新しいrecordを組み立てる。</summary>
    protected abstract void ResetDraft();

    protected static EditableRowViewModel ApplyNew(EditableRowViewModel row)
    {
        row.IsNew = true;
        row.IsEditing = true;
        return row;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

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

    private string _draftName = "";
    public string DraftName { get => _draftName; set { if (_draftName == value) return; _draftName = value; OnPropertyChanged(); } }
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
        DraftName = Value?.Name ?? ""; DraftGrade = Value?.Grade ?? "";
        DraftMaxConsecutiveSlots = Value?.DefaultMaxConsecutiveSlots ?? 2;
        DraftAllowGap = Value?.AllowGap ?? false; DraftActive = Value?.Active ?? true; DraftNote = Value?.Note ?? "";
    }

    /// <summary>ReloadAsync後、編集中だった行のDraft値を新しいインスタンスへ引き継ぐときに使う。</summary>
    public void CopyDraftFrom(StudentRowViewModel other)
    {
        DraftName = other.DraftName; DraftGrade = other.DraftGrade; DraftMaxConsecutiveSlots = other.DraftMaxConsecutiveSlots;
        DraftAllowGap = other.DraftAllowGap; DraftActive = other.DraftActive; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        (Value is { } v && $"{v.ExternalId}{v.Name}{v.Grade}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
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

    private string _draftName = "";
    public string DraftName { get => _draftName; set { if (_draftName == value) return; _draftName = value; OnPropertyChanged(); } }
    private bool _draftAllowGap;
    public bool DraftAllowGap { get => _draftAllowGap; set { if (_draftAllowGap == value) return; _draftAllowGap = value; OnPropertyChanged(); } }
    private bool _draftActive = true;
    public bool DraftActive { get => _draftActive; set { if (_draftActive == value) return; _draftActive = value; OnPropertyChanged(); } }
    private string _draftNote = "";
    public string DraftNote { get => _draftNote; set { if (_draftNote == value) return; _draftNote = value; OnPropertyChanged(); } }

    protected override void ResetDraft()
    {
        DraftName = Value?.Name ?? ""; DraftAllowGap = Value?.AllowGap ?? false;
        DraftActive = Value?.Active ?? true; DraftNote = Value?.Note ?? "";
    }

    public void CopyDraftFrom(TeacherRowViewModel other)
    {
        DraftName = other.DraftName; DraftAllowGap = other.DraftAllowGap; DraftActive = other.DraftActive; DraftNote = other.DraftNote;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        (Value is { } v && $"{v.ExternalId}{v.Name}".Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
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
