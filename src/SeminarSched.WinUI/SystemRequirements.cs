using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SeminarSched_WinUI;

/// <summary>
/// ユーザー指示「性能の最低条件みたいなのをアプリの情報に書いておいてください。もし下回る場合は
/// アプリの情報のところに警告を出してください。CPUの性能などは10年ほど前の型からリスト作っておき
/// (ノートPCも含む)、そこからその基準を作ってください」への対応。
///
/// 最低条件は、10年ほど前（2015〜2016年頃、Windows 10発売前後）に一般的だったCPU（デスクトップ・
/// ノートPC問わず、当時の普及帯）のうち最も控えめな構成を下回らないことを目安に決めた:
///   デスクトップ: Intel Pentium G4400（2コア/2スレッド・3.3GHz）、Core i3-6100（2コア/4スレッド・3.7GHz）
///   ノートPC:     Intel Celeron N3050（2コア・ベース1.6GHz）、Core i5-6200U（2コア/4スレッド・ベース2.3GHz）
/// このうち最も緩い値（コア数2・クロック1.6GHz）を最低条件の基準とした。メモリはこの世代の
/// 一般的な最小搭載量（4GB）を基準にした。CP-SATによる最適化計算はCPU・メモリともに相応に使うため、
/// 実際に軽快に動く保証をする基準ではなく、「これを下回るとかなり厳しい」という下限の目安である。
///
/// CPU名からのクロック抽出はレジストリ文字列（末尾の「@ X.XXGHz」）のベストエフォート解析であり、
/// 取得できない機種（表記が無い・ARM等）では判定をスキップする（誤って警告を出さないため）。
/// </summary>
internal static class SystemRequirements
{
    public const int MinimumLogicalProcessors = 2;
    public const double MinimumBaseClockGhz = 1.6;
    public const long MinimumMemoryGb = 4;

    public static SystemRequirementsCheck Evaluate()
    {
        var processorName = GetProcessorName();
        var logicalProcessors = Environment.ProcessorCount;
        var memoryGb = GetInstalledMemoryGb();
        var baseClockGhz = GetBaseClockGhz(processorName);

        var reasons = new List<string>();
        if (logicalProcessors < MinimumLogicalProcessors)
            reasons.Add($"論理プロセッサ数が{logicalProcessors}個です（推奨: {MinimumLogicalProcessors}個以上）。");
        if (memoryGb is { } gb && gb < MinimumMemoryGb)
            reasons.Add($"搭載メモリが約{gb}GBです（推奨: {MinimumMemoryGb}GB以上）。");
        if (baseClockGhz is { } ghz && ghz < MinimumBaseClockGhz)
            reasons.Add($"CPUのベースクロックが約{ghz:F1}GHzです（推奨: {MinimumBaseClockGhz:F1}GHz以上）。");

        return new SystemRequirementsCheck(processorName, logicalProcessors, memoryGb, baseClockGhz, reasons.Count == 0, reasons);
    }

    private static string? GetProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex ClockPattern = new(@"@\s*([\d.]+)\s*GHz", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static double? GetBaseClockGhz(string? processorName)
    {
        if (processorName is null) return null;
        var match = ClockPattern.Match(processorName);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var ghz) ? ghz : null;
    }

    private static long? GetInstalledMemoryGb()
    {
        try
        {
            if (!GetPhysicallyInstalledSystemMemory(out var kilobytes)) return null;
            return (long)Math.Round(kilobytes / 1024.0 / 1024.0);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}

internal sealed record SystemRequirementsCheck(
    string? ProcessorName,
    int LogicalProcessors,
    long? InstalledMemoryGb,
    double? BaseClockGhz,
    bool MeetsMinimum,
    IReadOnlyList<string> Reasons);
