param(
    [Parameter(Mandatory = $true)]
    [string]$L5xGitPath,

    [string]$RslComparePath,

    [string]$CustomToolsMenuPath = 'C:\Program Files (x86)\Rockwell Software\RSLogix 5000\Common\CustomToolsMenu.xml',

    [string]$InstallRoot = 'C:\ProgramData\LogixVcsTools',

    [switch]$SkipBuild,
    [switch]$SkipWinMerge,
    [switch]$SkipSourceTree
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NodeText {
    param($Node, [string]$Name)
    $child = $Node.SelectSingleNode($Name)
    if ($child) { return [string]$child.InnerText }
    return ''
}

function New-CustomTool {
    param(
        [xml]$Menu,
        [string]$Name,
        [string]$Command,
        [string]$WorkingDirectory,
        [string]$Arguments,
        [string]$StatusBarText,
        [string]$IconPath
    )

    $tool = $Menu.CreateElement('CustomTool')

    foreach ($nodeName in @('Command','WorkingDirectory','Arguments','ToolName','StatusBarText','ToolbarIcon')) {
        $node = $Menu.CreateElement($nodeName)
        if ($nodeName -eq 'ToolbarIcon') {
            $node.SetAttribute('IconIndex', '0')
        }
        [void]$tool.AppendChild($node)
    }

    $tool.SelectSingleNode('Command').InnerText = $Command
    $tool.SelectSingleNode('WorkingDirectory').InnerText = $WorkingDirectory
    $tool.SelectSingleNode('Arguments').InnerText = $Arguments
    $tool.SelectSingleNode('ToolName').InnerText = $Name
    $tool.SelectSingleNode('StatusBarText').InnerText = $StatusBarText
    $tool.SelectSingleNode('ToolbarIcon').InnerText = $IconPath

    return $tool
}

if (-not (Test-IsAdministrator)) {
    throw 'Run this installer from an elevated PowerShell window (Run as Administrator).'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build.ps1'
$publishRoot = Join-Path $repoRoot 'artifacts\publish'

if (-not (Test-Path -LiteralPath $L5xGitPath)) {
    throw "l5xgit.exe was not found at '$L5xGitPath'."
}
$L5xGitPath = (Resolve-Path -LiteralPath $L5xGitPath).Path

if (-not (Test-Path -LiteralPath $CustomToolsMenuPath)) {
    throw "Studio 5000 CustomToolsMenu.xml was not found at '$CustomToolsMenuPath'."
}

if ($RslComparePath) {
    if (-not (Test-Path -LiteralPath $RslComparePath)) {
        throw "RSLCompare.exe was not found at '$RslComparePath'."
    }
    $RslComparePath = (Resolve-Path -LiteralPath $RslComparePath).Path
}

if (-not $SkipBuild) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }
}

foreach ($required in @(
    (Join-Path $publishRoot 'commit\VcsVisualHost.exe'),
    (Join-Path $publishRoot 'compare\LogixCompareHost.exe'),
    (Join-Path $publishRoot 'restore\RestoreVisualHost.exe')
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Published host was not found: $required. Run scripts\build.ps1 first."
    }
}

$commitInstall = Join-Path $InstallRoot 'CommitHost'
$compareInstall = Join-Path $InstallRoot 'CompareHost'
$restoreInstall = Join-Path $InstallRoot 'RestoreHost'
$winMergeInstall = Join-Path $InstallRoot 'DiffWinMerge'
$sourceTreeInstall = Join-Path $InstallRoot 'DiffSourceTree'
$backupDirectory = Join-Path $InstallRoot 'backups'

New-Item -ItemType Directory -Force -Path $InstallRoot, $backupDirectory | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $backupDirectory "CustomToolsMenu.$timestamp.xml"
Copy-Item -LiteralPath $CustomToolsMenuPath -Destination $backupPath -Force

function Install-Directory {
    param([string]$Source, [string]$Destination)

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

Install-Directory -Source (Join-Path $publishRoot 'commit') -Destination $commitInstall
Install-Directory -Source (Join-Path $publishRoot 'compare') -Destination $compareInstall
Install-Directory -Source (Join-Path $publishRoot 'restore') -Destination $restoreInstall

if (-not $SkipWinMerge) {
    New-Item -ItemType Directory -Force -Path $winMergeInstall | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\winmerge\launch-winmerge-diff.ps1') -Destination (Join-Path $winMergeInstall 'launch-winmerge-diff.ps1') -Force
}

if (-not $SkipSourceTree) {
    New-Item -ItemType Directory -Force -Path $sourceTreeInstall | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\sourcetree\launch-sourcetree-diff.ps1') -Destination (Join-Path $sourceTreeInstall 'launch-sourcetree-diff.ps1') -Force
}

[xml]$menu = Get-Content -LiteralPath $CustomToolsMenuPath -Raw
$root = $menu.CustomToolsContent
$existingTools = @($root.CustomTool)

$originalCommit = $existingTools | Where-Object { [string]$_.ToolName -eq 'Commit' } | Select-Object -First 1
$originalDiff = $existingTools | Where-Object { [string]$_.ToolName -eq 'Difftool' } | Select-Object -First 1
$originalRestore = $existingTools | Where-Object { ([string]$_.ToolName -eq 'Restore') -or ([string]$_.ToolName -eq 'RestoreCurrentFile') } | Select-Object -First 1

$commitIcon = if ($originalCommit) { Get-NodeText $originalCommit 'ToolbarIcon' } else { '' }
$diffIcon = if ($originalDiff) { Get-NodeText $originalDiff 'ToolbarIcon' } else { '' }
$restoreIcon = if ($originalRestore) { Get-NodeText $originalRestore 'ToolbarIcon' } else { '' }

$managedNames = @(
    'Commit',
    'Difftool',
    'Difftool - WinMerge',
    'Difftool - SourceTree',
    'Compare Version...',
    'Restore',
    'RestoreCurrentFile',
    'Restore Project...',
    'Restore Version...',
    'Restore Working Project...'
)

foreach ($tool in @($root.CustomTool)) {
    if ($managedNames -contains [string]$tool.ToolName) {
        [void]$root.RemoveChild($tool)
    }
}

$commitExe = Join-Path $commitInstall 'VcsVisualHost.exe'
$compareExe = Join-Path $compareInstall 'LogixCompareHost.exe'
$restoreExe = Join-Path $restoreInstall 'RestoreVisualHost.exe'

$commitArgs = '--project-path "{0}" --l5xgit-path "{1}"' -f '%project_path%', $L5xGitPath
$compareArgs = '--project-path "{0}" --l5xgit-path "{1}"' -f '%project_path%', $L5xGitPath
if ($RslComparePath) {
    $compareArgs += (' --rslcompare-path "{0}"' -f $RslComparePath)
}
$restoreArgs = '--project-path "{0}" --l5xgit-path "{1}"' -f '%project_path%', $L5xGitPath

$toolArgs = @{
    Menu = $menu
    Name = 'Commit'
    Command = $commitExe
    WorkingDirectory = $commitInstall
    Arguments = $commitArgs
    StatusBarText = 'Capture the saved Logix project as a Git version'
    IconPath = $commitIcon
}
[void]$root.AppendChild((New-CustomTool @toolArgs))

if (-not $SkipWinMerge) {
    $winMergeLauncher = Join-Path $winMergeInstall 'launch-winmerge-diff.ps1'
    $winMergeArgs = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -ProjectPath "{1}"' -f $winMergeLauncher, '%project_path%'
    $toolArgs = @{
        Menu = $menu
        Name = 'Difftool - WinMerge'
        Command = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
        WorkingDirectory = $winMergeInstall
        Arguments = $winMergeArgs
        StatusBarText = 'Compare the previous and current Git versions in WinMerge'
        IconPath = $diffIcon
    }
    [void]$root.AppendChild((New-CustomTool @toolArgs))
}

if (-not $SkipSourceTree) {
    $sourceTreeLauncher = Join-Path $sourceTreeInstall 'launch-sourcetree-diff.ps1'
    $sourceTreeArgs = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -ProjectPath "{1}"' -f $sourceTreeLauncher, '%project_path%'
    $toolArgs = @{
        Menu = $menu
        Name = 'Difftool - SourceTree'
        Command = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
        WorkingDirectory = $sourceTreeInstall
        Arguments = $sourceTreeArgs
        StatusBarText = 'Open this project repository in SourceTree history'
        IconPath = $diffIcon
    }
    [void]$root.AppendChild((New-CustomTool @toolArgs))
}

$toolArgs = @{
    Menu = $menu
    Name = 'Compare Version...'
    Command = $compareExe
    WorkingDirectory = $compareInstall
    Arguments = $compareArgs
    StatusBarText = 'Compare the saved ACD with a captured Git version in Logix Designer Compare Tool'
    IconPath = $diffIcon
}
[void]$root.AppendChild((New-CustomTool @toolArgs))

$toolArgs = @{
    Menu = $menu
    Name = 'Restore Working Project...'
    Command = $restoreExe
    WorkingDirectory = $restoreInstall
    Arguments = $restoreArgs
    StatusBarText = 'Restore a captured Git version into the base-named working ACD'
    IconPath = $restoreIcon
}
[void]$root.AppendChild((New-CustomTool @toolArgs))

$menu.Save($CustomToolsMenuPath)

$state = [ordered]@{
    installedAt = (Get-Date).ToString('o')
    version = '0.1.0'
    installRoot = $InstallRoot
    customToolsMenuPath = $CustomToolsMenuPath
    backupPath = $backupPath
    l5xgitPath = $L5xGitPath
    rslComparePath = $RslComparePath
    ports = [ordered]@{
        commit = 5187
        compare = 5191
        restore = 5192
    }
}

$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $InstallRoot 'install-state.json') -Encoding UTF8

Write-Host ""
Write-Host 'Logix VCS Tools installed successfully.' -ForegroundColor Green
Write-Host "Install root: $InstallRoot"
Write-Host "Menu backup:  $backupPath"
Write-Host ""
Write-Host 'Studio 5000 menu entries:' -ForegroundColor Cyan
Write-Host '  Commit'
if (-not $SkipWinMerge) { Write-Host '  Difftool - WinMerge' }
if (-not $SkipSourceTree) { Write-Host '  Difftool - SourceTree' }
Write-Host '  Compare Version...'
Write-Host '  Restore Working Project...'
Write-Host ""
Write-Host 'Local UI ports:' -ForegroundColor Cyan
Write-Host '  Commit:  http://127.0.0.1:5187'
Write-Host '  Compare: http://127.0.0.1:5191'
Write-Host '  Restore: http://127.0.0.1:5192'
Write-Host ""
Write-Host 'Restart Studio 5000 before testing the Tools menu.' -ForegroundColor Yellow
