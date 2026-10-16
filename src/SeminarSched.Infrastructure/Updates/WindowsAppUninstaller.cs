using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SeminarSched.Application.Updates;

namespace SeminarSched.Infrastructure.Updates;

/// <summary>
/// installer/SeminarSched.WinUI.issのInno Setupインストーラーが、PrivilegesRequired=adminのため
/// HKEY_LOCAL_MACHINEの「アプリと機能」一覧（AppId + "_is1"）へ登録するUninstallStringを読み、
/// その先のunins000.exeを起動する。アンインストール本体（Remove-AppxPackage）はSetup側
/// （installer/Uninstall-Package.ps1）が行うため、このクラスは発見と起動だけを担う。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppUninstaller : IAppUninstaller
{
    // installer/SeminarSched.WinUI.issの[Setup] AppIdと同じ値（固定）。
    private const string AppId = "{18385982-E311-4223-B977-3544556BD4A8}";

    public string? FindUninstallCommand()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1");
        return key?.GetValue("UninstallString") as string;
    }

    public bool Launch(string uninstallCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uninstallCommand);
        // レジストリのUninstallStringは通常 "C:\...\unins000.exe" のように引用符付きで
        // 格納されているため、Process.Startのファイル名としてそのまま渡せるよう取り除く。
        var path = uninstallCommand.Trim().Trim('"');
        var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return started is not null;
    }
}
