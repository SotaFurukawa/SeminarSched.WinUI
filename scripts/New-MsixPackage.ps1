# Builds a signed, sideload-only MSIX package for SeminarSched.WinUI and copies it (plus the
# public .cer end users need to trust it) into dist\. Run from the repository root or anywhere;
# paths are resolved relative to this script.
param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",
    [string]$Subject = "CN=SotaFurukawa"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\SeminarSched.WinUI\SeminarSched.WinUI.csproj"
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$certScript = Join-Path $PSScriptRoot "New-SigningCertificate.ps1"
$pfxPath = (& $certScript -Subject $Subject | Select-Object -Last 1).Trim()

# The packaging tooling signs far more reliably from a certificate already in the current
# user's personal store (referenced by thumbprint) than from an external .pfx + password pair,
# which can fail to import silently and fall back to an unsigned/test-signed package. Re-import
# from the .pfx only if this machine's store doesn't already have it (e.g. a fresh checkout).
$storeCert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $storeCert) {
    $passwordPath = Join-Path $env:LOCALAPPDATA "SeminarSched.WinUI\packaging\SeminarSched.WinUI.pfx.password.txt"
    $securePassword = ConvertTo-SecureString -String (Get-Content -Path $passwordPath -Raw) -Force -AsPlainText
    $storeCert = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $securePassword
}
$thumbprint = $storeCert.Thumbprint

$distDir = Join-Path $repoRoot "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Package.appxmanifest's Identity/Version is a plain static XML attribute; the single-project
# MSIX packaging tooling used here (GenerateAppxPackageOnBuild) packages it as-is and does not
# substitute it from any MSBuild property (confirmed by inspecting a built .msix's manifest,
# which still read "0.9.0.0" across v0.9.1 through v0.13.1 despite Directory.Build.props moving
# on). Keep it in sync with Directory.Build.props here so every packaged .msix's actual identity
# version matches what ships (checkpoint113).
$version = (Select-Xml -Path (Join-Path $repoRoot "Directory.Build.props") -XPath "//*[local-name()='VersionPrefix']").Node.InnerText
$manifestPath = Join-Path $repoRoot "src\SeminarSched.WinUI\Package.appxmanifest"
$manifestVersion = "$version.0"
[xml]$manifestXml = Get-Content $manifestPath -Raw
if ($manifestXml.Package.Identity.Version -ne $manifestVersion) {
    $manifestXml.Package.Identity.Version = $manifestVersion
    $manifestXml.Save($manifestPath)
    Write-Output "Synced Package.appxmanifest Identity/Version to $manifestVersion"
}

Write-Output "Building and signing the $Platform sideload MSIX package (cert thumbprint $thumbprint) ..."
& $dotnet build $csproj `
    -c Release -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateThumbprint=$thumbprint `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:AppxBundle=Never `
    -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$appPackagesRoot = Join-Path $repoRoot "src\SeminarSched.WinUI\AppPackages"
$msix = Get-ChildItem -Path $appPackagesRoot -Filter "*.msix" -Recurse |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "No .msix file was produced under $appPackagesRoot. Check the build output above." }

$destination = Join-Path $distDir "SeminarSched.WinUI-$version-$Platform.msix"
Copy-Item -Path $msix.FullName -Destination $destination -Force

Write-Output ""
Write-Output "Package ready: $destination"
Write-Output "Public certificate for end users: $(Join-Path $distDir 'SeminarSched.WinUI.cer')"
