namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー要望（checkpoint122）「上側のタブだけでなく、左側のタブにもこれらを選択できる
/// ようにしておきたい。設定を開いている間は右側に1.1, 1.2...のようにボタンを設置する。
/// 設定が開かれていないときはこれを表示しない」への対応。MainWindowの左ナビ「1 プロジェクト設定」
/// 配下の子項目（1.1〜1.8）とSetupPage上部のTabViewの選択状態を、この1箇所を経由して相互に同期する。
/// OptimizationRunStateと同じ思想で、Frame.Navigateのたびに新しいPageインスタンスが作られる
/// WinUIの制約を避けるため、Page外（アプリ全体）に状態を置く。
/// ユーザー要望（「生徒・講師・講師指導可能科目・通常授業担当設定・科目をプロジェクトごとの管理
/// ではなく一括管理にしたい。ホームと1プロジェクト設定の間に0基本情報設定を作る」）への対応で、
/// SetupPageは「0 基本情報設定」（共通正本DBを対象、プロジェクト不要）と「1 プロジェクト設定」
/// （開校日・コマ・スケジュール・出力、プロジェクトのDBを対象）の2つのモードを1つのインスタンスで
/// 共有するため、選択中のタブ番号だけでなく、どちらのモードかも合わせて同期する。
/// </summary>
internal static class SetupPageNavState
{
    public static event Action? Changed;

    public static bool IsActive { get; private set; }
    public static int SelectedTabIndex { get; private set; }
    public static bool IsBasicInfoMode { get; private set; }

    // MainWindowがナビゲーションペインの子項目をクリックしたとき、まだSetupPageが開かれていない
    // 場合はここへ希望タブ番号・モードを置いてからFrame.Navigateする。SetupPage.Page_Loadedが
    // 読み取って消費する（既にSetupPageが開かれている場合はFrame.Navigateを経由せず、MainWindow側で
    // SetupPage.ActivateMode()を直接呼ぶためこの経路は通らない）。
    public static int? RequestedTabIndex { get; set; }
    public static bool? RequestedBasicInfoMode { get; set; }

    public static void Activate(int tabIndex, bool isBasicInfoMode)
    {
        IsActive = true;
        SelectedTabIndex = tabIndex;
        IsBasicInfoMode = isBasicInfoMode;
        Changed?.Invoke();
    }

    public static void Deactivate()
    {
        IsActive = false;
        Changed?.Invoke();
    }

    public static void SetSelectedTabIndex(int index, bool isBasicInfoMode)
    {
        SelectedTabIndex = index;
        IsBasicInfoMode = isBasicInfoMode;
        if (IsActive) Changed?.Invoke();
    }
}
