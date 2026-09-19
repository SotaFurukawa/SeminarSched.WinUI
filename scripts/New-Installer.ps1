# dist\配下の署名済み.msix/.cer(New-MsixPackage.ps1が生成)から、Python版と同じ体裁の
# 1ファイルSetup.exe(Inno Setup)を作る。存在しなければ.msix/.cerも先に生成する。
param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $repoRoot "dist"

$version = (Select-Xml -Path (Join-Path $repoRoot "Directory.Build.props") -XPath "//*[local-name()='VersionPrefix']").Node.InnerText
$msixPath = Join-Path $distDir "SeminarSched.WinUI-$version-$Platform.msix"
$cerPath = Join-Path $distDir "SeminarSched.WinUI.cer"

if (-not (Test-Path $msixPath) -or -not (Test-Path $cerPath)) {
    Write-Output "MSIX/certificate not found for version $version - building them first..."
    & (Join-Path $PSScriptRoot "New-MsixPackage.ps1") -Platform $Platform
}

$iscc = & (Join-Path $PSScriptRoot "Install-InnoSetup.ps1")

$issPath = Join-Path $repoRoot "installer\SeminarSched.WinUI.iss"
Write-Output "Compiling installer with Inno Setup ($iscc) ..."
& $iscc "/DMyAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed with exit code $LASTEXITCODE" }

$setupPath = Join-Path $distDir "SeminarSched.WinUI-Setup-$version.exe"
if (-not (Test-Path $setupPath)) { throw "Expected installer was not produced: $setupPath" }

Write-Output ""
Write-Output "Installer ready: $setupPath"
