# Setup.exe(Inno Setup)から呼び出される。同梱の.cerをCurrentUser\TrustedPeopleへ登録し、
# 同梱の.msixをサイドロードとしてインストールする。管理者権限は不要（CurrentUserストアへの
# 登録・per-userのAdd-AppxPackageはいずれも昇格なしで動作することを実機で確認済み）。
param(
    [Parameter(Mandatory = $true)][string]$CertPath,
    [Parameter(Mandatory = $true)][string]$MsixPath
)

$ErrorActionPreference = "Stop"

try {
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Add-AppxPackage -Path $MsixPath -ForceApplicationShutdown
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
