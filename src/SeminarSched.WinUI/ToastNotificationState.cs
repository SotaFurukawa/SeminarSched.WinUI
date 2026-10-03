namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー要望「保存などの成功通知を、iPhoneの通知のように上部からスライドインさせたい」への
/// 対応。各ページはこのクラス経由でMainWindowのトースト表示を要求するだけで、実際の表示・
/// アニメーションはMainWindow側（ウィンドウに1つだけ存在するオーバーレイ）が行う。
/// OptimizationRunStateなどと同じ「Page外の状態をstaticイベントで通知する」パターン。
/// </summary>
public static class ToastNotificationState
{
    public static event Action<string>? Requested;

    public static void ShowSuccess(string message) => Requested?.Invoke(message);
}
