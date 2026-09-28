# Setup.exe(Inno Setup)から呼び出される。同梱の.cerをCurrentUser\TrustedPeopleへ登録し、
# 同梱の.msixをサイドロードとしてインストールする。管理者権限は不要（CurrentUserストアへの
# 登録・per-userのAdd-AppxPackageはいずれも昇格なしで動作することを実機で確認済み）。
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
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown
    if (Test-Path $logPath) { Remove-Item $logPath -Force -ErrorAction SilentlyContinue }
    exit 0
}
catch {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`n$($_.Exception.Message)" | Out-File -FilePath $logPath -Encoding utf8 -Force
    Write-Error $_.Exception.Message
    exit 1
}
