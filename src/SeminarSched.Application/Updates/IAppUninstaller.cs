namespace SeminarSched.Application.Updates;

/// <summary>
/// ユーザー要望（「こういう不具合が発生したときのためにアンインストールボタンを追加したい」）への
/// 対応。既存のインストーラー（Inno Setup）がWindowsの「アプリと機能」へ登録した、このアプリ専用の
/// アンインストーラー（unins000.exe）を見つけて起動する。本体のアンインストール処理自体は、
/// インストール時と同じくSetup側（installer/Uninstall-Package.ps1のRemove-AppxPackage）が行う。
/// </summary>
public interface IAppUninstaller
{
    /// <summary>
    /// 登録済みのアンインストールコマンド（レジストリのUninstallString）を返す。インストーラー
    /// 経由ではなくdotnet run等で起動している場合や、別ユーザーでインストールした場合など、
    /// 見つからない場合はnullを返す。
    /// </summary>
    string? FindUninstallCommand();

    bool Launch(string uninstallCommand);
}
