namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー要望（checkpoint122）「上側のタブだけでなく、左側のタブにもこれらを選択できる
/// ようにしておきたい。設定を開いている間は右側に1.1, 1.2...のようにボタンを設置する。
/// 設定が開かれていないときはこれを表示しない」への対応。MainWindowの左ナビ「① 設定」配下の
/// 子項目（1.1〜1.8）とSetupPage上部のTabViewの選択状態を、この1箇所を経由して相互に同期する。
/// OptimizationRunStateと同じ思想で、Frame.Navigateのたびに新しいPageインスタンスが作られる
/// WinUIの制約を避けるため、Page外（アプリ全体）に状態を置く。
/// </summary>
internal static class SetupPageNavState
{
    public static event Action? Changed;

    public static bool IsActive { get; private set; }
    public static int SelectedTabIndex { get; private set; }

    // MainWindowがナビゲーションペインの子項目をクリックしたとき、まだSetupPageが開かれていない
    // 場合はここへ希望タブ番号を置いてからFrame.Navigateする。SetupPage.Page_Loadedが読み取って
    // 消費する（既にSetupPageが開かれている場合はFrame.Navigateを経由せず、MainWindow側で
    // SetupPage.SelectTab()を直接呼ぶためこの経路は通らない）。
    public static int? RequestedTabIndex { get; set; }

    public static void Activate(int tabIndex)
    {
        IsActive = true;
        SelectedTabIndex = tabIndex;
        Changed?.Invoke();
    }

    public static void Deactivate()
    {
        IsActive = false;
        Changed?.Invoke();
    }

    public static void SetSelectedTabIndex(int index)
    {
        SelectedTabIndex = index;
        if (IsActive) Changed?.Invoke();
    }
}
