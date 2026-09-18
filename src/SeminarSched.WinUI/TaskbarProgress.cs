using System.Runtime.InteropServices;

namespace SeminarSched_WinUI;

/// <summary>
/// Windowsタスクバーのアプリアイコンに、⑤時間割自動作成の進行状況を反映する。緑の進捗バーは
/// <c>ITaskbarList3.SetProgressState/SetProgressValue</c>（標準COM、追加パッケージ不要）。完了通知は
/// <c>SetProgressState</c>の既定色（緑/赤/黄）だけでは「オレンジ」を表現できないため、代わりに
/// <c>SetOverlayIcon</c>でオレンジの丸バッジをタスクバーアイコンへ重ね描きする（アイコンは実行時に
/// AND/XORマスクから直接生成し、追加の画像アセットは不要）。バッジはこのウィンドウがフォアグラウンドへ
/// 戻った時点（<see cref="MainWindow"/>の<c>Activated</c>）でクリアする。
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

    public static void Clear(IntPtr hwnd)
    {
        var taskbar = GetTaskbar();
        taskbar?.SetProgressState(hwnd, TbpFlag.NoProgress);
        taskbar?.SetOverlayIcon(hwnd, IntPtr.Zero, "");
    }

    public static void NotifyCompleted(IntPtr hwnd)
    {
        var taskbar = GetTaskbar();
        if (taskbar is not null)
        {
            // 進捗バー自体はここで終了（緑は「実行中」だけの意味にする）。完了はオレンジのバッジで示す。
            taskbar.SetProgressState(hwnd, TbpFlag.NoProgress);
            var icon = CreateOrangeBadgeIcon();
            if (icon != IntPtr.Zero)
            {
                taskbar.SetOverlayIcon(hwnd, icon, "時間割の自動作成が完了しました");
                DestroyIcon(icon);
            }
        }
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

    /// <summary>ウィンドウがフォアグラウンドへ戻ったときに呼び、通知目的だけのオレンジバッジを消す。</summary>
    public static void ClearCompletionBadge(IntPtr hwnd) => GetTaskbar()?.SetOverlayIcon(hwnd, IntPtr.Zero, "");

    // AND/XORマスクから16x16の円形バッジアイコンを生成する。CreateIconの1bit ANDマスク＋32bit XORマスクは
    // どのWindowsバージョンでも確実に透過が効く古典的な方式（アルファ付きアイコン専用のCreateIconIndirect等
    // より依存が少ない）。色はDarkOrange(#FF8C00)。
    private static IntPtr CreateOrangeBadgeIcon()
    {
        const int size = 16;
        const int andRowBytes = size / 8;
        var and = new byte[andRowBytes * size];
        var xor = new byte[size * size * 4];
        const double center = (size - 1) / 2.0;
        const double radius = size / 2.0 - 0.5;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center; var dy = y - center;
                var inside = dx * dx + dy * dy <= radius * radius;
                var xorOffset = (y * size + x) * 4;
                if (inside)
                {
                    xor[xorOffset + 0] = 0x00; // B
                    xor[xorOffset + 1] = 0x8C; // G
                    xor[xorOffset + 2] = 0xFF; // R  -> #FF8C00 DarkOrange
                    xor[xorOffset + 3] = 0xFF; // A
                }
                else
                {
                    var andByteIndex = y * andRowBytes + x / 8;
                    var bit = 7 - (x % 8);
                    and[andByteIndex] |= (byte)(1 << bit);
                }
            }
        }
        return CreateIcon(IntPtr.Zero, size, size, 1, 32, and, xor);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIcon(IntPtr hInstance, int nWidth, int nHeight, byte cPlanes, byte cBitsPixel, byte[] lpbAndBits, byte[] lpbXorBits);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
