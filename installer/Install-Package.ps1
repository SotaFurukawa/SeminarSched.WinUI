# Setup.exe(Inno Setup)から呼び出される。同梱の.cerをLocalMachine\TrustedPeopleへ登録し、
# 同梱の.msixをサイドロードとしてインストールする。
# ユーザー報告（v1.0.0公開直後、別マシンでの実機インストールで判明）: 以前はCurrentUser\
# TrustedPeopleへ登録していたが、Add-AppxPackageがこれを信頼せず「0x800B0109 証明書チェーンは
# 処理されましたが、信頼プロバイダーが信頼していないルート証明書で強制終了しました」で失敗する
# ことが確認された（docs/adr/0005-windows-distribution.mdのAmendment参照）。LocalMachine\
# TrustedPeopleへの登録には管理者権限が必要なため、installer\SeminarSched.WinUI.issの
# PrivilegesRequiredをadminへ変更し、このスクリプトも管理者権限での実行を前提にした。
param(
    [Parameter(Mandatory = $true)][string]$CertPath,
    [Parameter(Mandatory = $true)][string]$MsixPath
)

$ErrorActionPreference = "Stop"
# Setup.exeの[Code]セクション（MsgBox）は数値の終了コードしか表示できず、実際の失敗理由（証明書の
# インポート失敗かAdd-AppxPackageの失敗か、Windowsが返した具体的な理由）が分からず開発者への
# 問い合わせだけでは原因を特定できない、という問題があった。ここで失敗理由を固定パスのログへ書き出し、
# ユーザーがこのファイルの中身をそのまま共有できるようにする。
$logPath = Join-Path $env:TEMP "SeminarSched.WinUI-install-error.log"

try {
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown
    if (Test-Path $logPath) { Remove-Item $logPath -Force -ErrorAction SilentlyContinue }
    exit 0
}
catch {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`n$($_.Exception.Message)" | Out-File -FilePath $logPath -Encoding utf8 -Force
    Write-Error $_.Exception.Message
    exit 1
}
