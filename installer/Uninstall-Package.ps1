# アンインストーラ(unins000.exe)から呼び出される。登録済みのAppXパッケージを削除する。
# 証明書のTrustedPeople登録は、同じ発行元の他のバージョン/他のsideloadアプリに影響しうるため
# 意図的に削除しない（Import-Certificateは再インストール時に無害に上書きされる）。
param(
    [Parameter(Mandatory = $true)][string]$IdentityName
)

$ErrorActionPreference = "Stop"

try {
    $package = Get-AppxPackage -Name $IdentityName
    if ($package) { $package | Remove-AppxPackage }
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
