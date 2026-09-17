# Creates (or reuses) the self-signed code-signing certificate used to sign local
# sideload MSIX builds. The certificate Subject must exactly match the <Identity Publisher>
# in src/SeminarSched.WinUI/Package.appxmanifest.
#
# The private key (.pfx) and its password never leave this machine and are never committed
# to git: they live under %LOCALAPPDATA%\SeminarSched.WinUI\packaging\. The public certificate
# (.cer) is copied to dist\ so it can be handed to end users, who must import it into their
# own machine's "Trusted People" store before a sideloaded .msix signed with it will install.
param(
    [string]$Subject = "CN=SotaFurukawa"
)

$ErrorActionPreference = "Stop"

$packagingDir = Join-Path $env:LOCALAPPDATA "SeminarSched.WinUI\packaging"
New-Item -ItemType Directory -Force -Path $packagingDir | Out-Null
$pfxPath = Join-Path $packagingDir "SeminarSched.WinUI.pfx"
$passwordPath = Join-Path $packagingDir "SeminarSched.WinUI.pfx.password.txt"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $repoRoot "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$cerPath = Join-Path $distDir "SeminarSched.WinUI.cer"

if ((Test-Path $pfxPath) -and (Test-Path $passwordPath) -and (Test-Path $cerPath)) {
    Write-Output "Existing signing certificate found at $pfxPath - reusing it."
    Write-Output $pfxPath
    exit 0
}

Write-Output "Generating a new self-signed signing certificate for $Subject ..."
$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject `
    -KeyUsage DigitalSignature -FriendlyName "SeminarSched.WinUI sideload signing certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(3) `
    -TextExtension @("2.5.29.19={text}false", "2.5.29.37={text}1.3.6.1.5.5.7.3.3")

$alphabet = (48..57) + (65..90) + (97..122) | ForEach-Object { [char]$_ }
$plainPassword = -join (1..40 | ForEach-Object { $alphabet | Get-Random })
$securePassword = ConvertTo-SecureString -String $plainPassword -Force -AsPlainText

# Export with the legacy TripleDES/SHA1 PFX protection: newer PowerShell defaults to an
# AES256/SHA256 protection mode that the MSIX packaging signer cannot read, which fails
# signing with a misleading "cannot import the key file" warning instead of a real error.
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword -CryptoAlgorithmOption TripleDES_SHA1 | Out-Null
Set-Content -Path $passwordPath -Value $plainPassword -NoNewline
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Output "Certificate created. Private key: $pfxPath (kept local, gitignored). Public certificate for end users: $cerPath"
Write-Output $pfxPath
