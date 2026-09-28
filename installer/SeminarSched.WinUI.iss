; Python版(seminarSched)のinstaller\SummerCourseScheduler.issと同じ体裁(日本語ウィザード、
; 管理者権限不要、任意のデスクトップアイコン、インストール後に起動)を踏襲した、MSIXサイドロードの
; ラッパーインストーラ。実体はPackage.appxmanifestで定義された署名済み.msixであり、Setup.exeは
; 「.cerをCurrentUser\TrustedPeopleへ信頼登録 → Add-AppxPackageでインストール」を裏側で行う。
; CurrentUserストアへの登録・per-userインストールはいずれも管理者権限なしで動作することを
; 実機で確認済み（PrivilegesRequired=lowestが成立する）。
#define MyAppName "SeminarSched.WinUI"
#define MyAppPublisher "SotaFurukawa"
#define MyIdentityName "F70149DC-0D08-4E3F-B67F-189A3A5E1C51"
; Package Family Name = IdentityName + "_" + Publisher(CN=SotaFurukawa)から決まるhash。
; Identity Name/Publisherを変更しない限り既存ビルドと一致することを実機で確認済み。
#define MyPackageFamilyName "F70149DC-0D08-4E3F-B67F-189A3A5E1C51_47fbr72rn8fp2"

#ifndef MyAppVersion
  #define MyAppVersion "0.3.1"
#endif

#ifndef SourceDirectory
  #define SourceDirectory "..\dist"
#endif

#ifndef OutputDirectory
  #define OutputDirectory "..\dist"
#endif

#define MyMsixFileName "SeminarSched.WinUI-" + MyAppVersion + "-x64.msix"

[Setup]
AppId={{18385982-E311-4223-B977-3544556BD4A8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\SeminarSched.WinUI
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDirectory}
OutputBaseFilename=SeminarSched.WinUI-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\SeminarSched.WinUI\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AppIcon.ico
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} セットアップ
VersionInfoProductName={#MyAppName}
VersionInfoProductTextVersion={#MyAppVersion}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加アイコン:"; Flags: unchecked

[Files]
Source: "{#SourceDirectory}\{#MyMsixFileName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDirectory}\SeminarSched.WinUI.cer"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\SeminarSched.WinUI\Assets\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "Install-Package.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Uninstall-Package.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{win}\explorer.exe"; Parameters: "shell:AppsFolder\{#MyPackageFamilyName}!App"; IconFilename: "{app}\AppIcon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{win}\explorer.exe"; Parameters: "shell:AppsFolder\{#MyPackageFamilyName}!App"; IconFilename: "{app}\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{win}\explorer.exe"; Parameters: "shell:AppsFolder\{#MyPackageFamilyName}!App"; Description: "{#MyAppName} を起動する"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-Package.ps1"" -IdentityName ""{#MyIdentityName}"""; Flags: runhidden; RunOnceId: "RemoveAppxPackage"

; User data lives under %LOCALAPPDATA%\SeminarSched.WinUI, outside {app} and outside the AppX
; package folder. There is deliberately no [UninstallDelete] section for it: uninstall and
; in-place upgrades preserve projects, settings, and logs (same policy as the Python版installer).

[Code]
function RunPowerShellScript(ScriptPath, ScriptParams: String; var ResultCode: Integer): Boolean;
begin
  Result := Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' + ScriptParams,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ScriptPath, CertPath, MsixPath: String;
  Ok: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    ScriptPath := ExpandConstant('{app}\Install-Package.ps1');
    CertPath := ExpandConstant('{app}\SeminarSched.WinUI.cer');
    MsixPath := ExpandConstant('{app}\{#MyMsixFileName}');

    Ok := RunPowerShellScript(ScriptPath,
      '-CertPath "' + CertPath + '" -MsixPath "' + MsixPath + '"',
      ResultCode);

    if (not Ok) or (ResultCode <> 0) then
      MsgBox('アプリケーション本体のインストールに失敗しました。' + #13#10 +
        'エラーコード: ' + IntToStr(ResultCode) + #13#10#13#10 +
        '詳しい原因は次のログファイルに記録されています。' + #13#10 +
        ExpandConstant('{%TEMP}\SeminarSched.WinUI-install-error.log') + #13#10#13#10 +
        'お手数ですが、このファイルの中身を添えて開発者にご連絡ください。', mbCriticalError, MB_OK);
  end;
end;
