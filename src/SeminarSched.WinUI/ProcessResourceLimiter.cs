using System.Runtime.InteropServices;

namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー報告「CPUにかなり負荷がかかってしまう」への対応（checkpoint88でのnum_search_workers半減
/// だけでは実機で改善が足りなかった、との追加報告を受けての強化）。Windows Job Object のCPU rate
/// control（Docker Desktop等が実際に使う仕組みと同じ）で、プロセス全体のCPU使用率を実測OS値として
/// 上限50%へハード制限する。num_search_workers（OR-Tools側の並列探索ワーカー数）を絞るだけでは、
/// OR-Toolsが内部的に追加で立てるスレッド分を含めた実際のCPU使用率までは保証できないため、
/// OSレベルでの制限と併用する。⑤時間割自動作成の実行中だけ有効にし、終了後は解除する（他の操作
/// （帳票出力等）まで巻き込んで遅くしないため）。
///
/// メモリ使用率の同様のハード制限（JOBOBJECT_EXTENDED_LIMIT_INFORMATIONのJobMemoryLimit）は
/// 意図的に実装していない: 超過時はOSがネイティブ側（OR-Tools、P/Invoke越しのC++ライブラリ）の
/// メモリ確保を失敗させるが、その失敗は.NET側で安全に捕捉できず、探索の途中でプロセスごと
/// クラッシュする恐れがある（CPU rate controlは超過時にスレッドを一時停止させるだけで、確保
/// failureを起こさない点が本質的に異なる。安全に緩やかに絞れるCPUと違い、メモリは「硬く縛ると
/// 落ちる」）。この機能ではCPU使用率の抑制（＝探索スレッドの実行機会そのものを減らす）を主な
/// 手段とし、メモリはNumSearchWorkersを絞ること（並列探索ツリーの本数を減らす）による間接的な
/// 抑制にとどめる。
/// </summary>
internal static class ProcessResourceLimiter
{
    private const int CpuRatePercent = 50;

    private static IntPtr _job = IntPtr.Zero;
    private static bool _unavailable;

    /// <summary>初回呼び出し時に自プロセスを専用Job Objectへ割り当てる（MSIXパッケージのプロセスは
    /// 既に別のJob Objectに入っていることがあるが、Windows 8以降はJob Objectのネストがサポートされて
    /// いるため、通常は問題なく追加のJobへ割り当てられる）。以降の呼び出しは同じJobの制限値を
    /// 更新するだけで、都度作り直しはしない。失敗した場合は以降の呼び出しを黙って諦める
    /// （この機能自体は「あれば嬉しい」もので、失敗してもアプリ本体の動作を妨げてはいけない）。</summary>
    public static void SetCpuLimit(bool enabled)
    {
        if (_unavailable) return;
        try
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObjectW(IntPtr.Zero, null);
                if (_job == IntPtr.Zero) { _unavailable = true; return; }
                if (!AssignProcessToJobObject(_job, GetCurrentProcess())) { _unavailable = true; return; }
            }

            var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = enabled ? JobObjectCpuRateControlEnable | JobObjectCpuRateControlHardCap : 0,
                CpuRate = CpuRatePercent * 100, // 1/100パーセント単位（5000=50.00%）。CPU全体（全論理コア合計）に対する割合。
            };
            var size = Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(_job, JobObjectCpuRateControlInformation, ptr, (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            _unavailable = true;
        }
    }

    private const int JobObjectCpuRateControlInformation = 15;
    private const uint JobObjectCpuRateControlEnable = 0x1;
    private const uint JobObjectCpuRateControlHardCap = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
