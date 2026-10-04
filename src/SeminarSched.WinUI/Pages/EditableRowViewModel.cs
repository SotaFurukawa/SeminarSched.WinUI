using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace SeminarSched_WinUI.Pages;

/// <summary>
/// ユーザー要望（checkpoint142）「生徒・講師・科目について、上側で入力させる形式ではないように
/// したい」「アンケート取込後の一覧についても...追加できるようにする」への対応。常時表示の
/// 入力フォーム＋下部一覧という構成をやめ、一覧の行そのものを「追加」ボタンで末尾に出す新規行、
/// または既存行への「変更」ボタンで、その場で編集できる形に変える（SetupPage: 生徒・講師・科目、
/// ImportPage: 受講希望、共通の基底クラス）。SetupPage.xamlの探索方針並び替えリスト
/// （<see cref="SchedulingPolicyRowViewModel"/>）と同じ「可変なINotifyPropertyChangedクラス＋
/// x:Bindでプロパティ変更を反映」という流儀を転用する。1つのDataTemplate内に表示用パネルと
/// 編集用パネルを両方置き、<see cref="IsEditing"/>から導出する<see cref="DisplayVisibility"/>/
/// <see cref="EditVisibility"/>で出し分ける（DataTemplateSelectorは使わない＝ListViewの
/// 仮想化でコンテナが使い回されても安全）。
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
