# Dev certificate for rnotify MSIX (ASCII by convention: PS 5.1 breaks UTF-8
# without BOM, see canon C:\devs\docs\win11-notifications-control.md 10a).
#
# Subject MUST equal the Package Publisher from docs/store-identity.md:
#   CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC
# Same subject => dev build installs with the same identity as the Store build.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/dev-cert.ps1           create if missing
#   powershell -ExecutionPolicy Bypass -File tools/dev-cert.ps1 -Reset    recreate
param([switch]$Reset)

$ErrorActionPreference = 'Stop'
$subject = 'CN=6F4182DB-0063-4273-87C8-59E15AFFCBCC'
$certsDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'certs'
$pfxPath = Join-Path $certsDir 'rnotify-dev.pfx'
$password = ConvertTo-SecureString -String 'rnotify-dev' -Force -AsPlainText

New-Item -ItemType Directory -Force -Path $certsDir | Out-Null

$existing = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject })
if ($existing.Count -gt 0 -and -not $Reset) {
    Write-Host "Cert already exists (thumbprint $($existing[0].Thumbprint)); use -Reset to recreate."
} else {
    $existing | Remove-Item
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
        -KeyUsage DigitalSignature -FriendlyName 'rnotify dev' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    Write-Host "Created cert $($cert.Thumbprint)"
}

$cert = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject })[0]
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
Write-Host "PFX exported: $pfxPath"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    $trusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq $subject })
    if ($trusted.Count -eq 0) {
        Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password | Out-Null
        Write-Host 'Imported to LocalMachine\TrustedPeople.'
    } else {
        Write-Host 'Already trusted in LocalMachine\TrustedPeople.'
    }

    $unlock = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    if (-not (Test-Path $unlock)) { New-Item -Path $unlock -Force | Out-Null }
    Set-ItemProperty -Path $unlock -Name AllowDevelopmentWithoutDevLicense -Type DWord -Value 1
    Write-Host 'Developer mode enabled.'
} else {
    Write-Warning 'Not elevated: TrustedPeople import skipped. Re-run from an elevated shell before Add-AppxPackage.'
}

Write-Host ''
Write-Host 'Next steps:'
Write-Host '  dotnet build -c Release src/rnotify'
Write-Host '  Add-AppxPackage <repo>\src\rnotify\bin\Release\net10.0-windows10.0.22621.0\win-x64\AppPackages\rnotify_0.1.0.0_x64\rnotify_0.1.0.0_x64.msix'
