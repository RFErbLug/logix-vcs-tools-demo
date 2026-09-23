param(
    [string]$InstallRoot = 'C:\ProgramData\LogixVcsTools'
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw 'Run this uninstaller from an elevated PowerShell window (Run as Administrator).'
}

$statePath = Join-Path $InstallRoot 'install-state.json'
if (-not (Test-Path -LiteralPath $statePath)) {
    throw "Install state was not found at '$statePath'."
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$menuPath = [string]$state.customToolsMenuPath
$backupPath = [string]$state.backupPath

if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Original CustomToolsMenu backup was not found: $backupPath"
}

Copy-Item -LiteralPath $backupPath -Destination $menuPath -Force

Get-Process VcsVisualHost, LogixCompareHost, RestoreVisualHost -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host 'Studio 5000 custom-tool menu restored from the installation backup.' -ForegroundColor Green
Write-Host "Restored: $backupPath"
Write-Host ""
Write-Host "Installed files remain at '$InstallRoot' so the backup and install record are preserved."
Write-Host 'Restart Studio 5000 before checking the Tools menu.' -ForegroundColor Yellow
