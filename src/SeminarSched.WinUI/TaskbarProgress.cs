using System.Runtime.InteropServices;

namespace SeminarSched_WinUI;

/// <summary>
/// Windowsタスクバーのアプリアイコンに、⑤時間割自動作成の進行状況を反映する。緑の進捗バーは
/// <c>ITaskbarList3.SetProgressState/SetProgressValue</c>（標準COM、追加パッケージ不要）。完了通知は
/// <c>FlashWindowEx</c>によるタスクバーボタンの点滅のみを使う（オーバーレイアイコンによる常駐バッジは
/// ユーザー指示により廃止済み。点滅はユーザーがウィンドウをフォアグラウンドへ戻すと自動的に止まる）。
/// </summary>
internal static class TaskbarProgress
{
    public static void SetRunning(IntPtr hwnd, double percent)
    {
        var taskbar = GetTaskbar();
        if (taskbar is null) return;
        taskbar.SetProgressState(hwnd, TbpFlag.Normal);
        taskbar.SetProgressValue(hwnd, (ulong)Math.Clamp(percent, 0, 100), 100);
    }

    public static void Clear(IntPtr hwnd) => GetTaskbar()?.SetProgressState(hwnd, TbpFlag.NoProgress);

    public static void NotifyCompleted(IntPtr hwnd)
    {
        // 進捗バー自体はここで終了する（緑は「実行中」だけの意味にする）。完了はタスクバーボタンの
        // 点滅だけで示し、常駐するオーバーレイバッジは表示しない（ユーザー指示）。
        GetTaskbar()?.SetProgressState(hwnd, TbpFlag.NoProgress);
        var info = new FlashWInfo
        {
            CbSize = (uint)Marshal.SizeOf<FlashWInfo>(),
            Hwnd = hwnd,
            Flags = FlashwAll | FlashwTimerNoFg,
            Count = uint.MaxValue,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private static ITaskbarList3? _instance;
    private static bool _unavailable;

    private static ITaskbarList3? GetTaskbar()
    {
        if (_unavailable) return null;
        if (_instance is not null) return _instance;
        try
        {
            var taskbar = (ITaskbarList3)new TaskbarInstance();
            taskbar.HrInit();
            _instance = taskbar;
            return _instance;
        }
        catch (COMException)
        {
            _unavailable = true;
            return null;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarInstance;

    private enum TbpFlag
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    // vtableの先頭からSetOverlayIconまでを宣言順どおりに並べる必要がある（ITaskbarList3が継承する
    // ITaskbarList/ITaskbarList2のメソッドを省略できないため）。RegisterTab〜ThumbBarSetImageListは
    // 実際には呼ばないが、vtableの位置合わせのためだけに宣言している。
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TbpFlag state);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private const uint FlashwAll = 3;
    private const uint FlashwTimerNoFg = 12;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWInfo pwfi);
}
