param(
    [string]$L5xGitPath,
    [string]$RslComparePath
)

$ErrorActionPreference = 'Stop'

function Find-FirstExistingPath {
    param([string[]]$Candidates)
    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$git = Get-Command git -ErrorAction SilentlyContinue

if (-not $RslComparePath) {
    $RslComparePath = Find-FirstExistingPath @(
        'C:\Program Files (x86)\Rockwell Software\Logix Designer Tools\Logix Designer Compare Tool\RSLCompare.exe',
        'C:\Program Files\Rockwell Software\Logix Designer Tools\Logix Designer Compare Tool\RSLCompare.exe',
        'C:\Program Files\Rockwell Software\RSLCompare\RSLCompare.exe',
        'C:\Program Files (x86)\Rockwell Software\RSLCompare\RSLCompare.exe'
    )
}

$winMerge = Find-FirstExistingPath @(
    'C:\Program Files\WinMerge\WinMergeU.exe',
    'C:\Program Files (x86)\WinMerge\WinMergeU.exe'
)

$sourceTree = Find-FirstExistingPath @(
    (Join-Path $env:LOCALAPPDATA 'SourceTree\SourceTree.exe'),
    'C:\Program Files\Atlassian\SourceTree\SourceTree.exe',
    'C:\Program Files (x86)\Atlassian\SourceTree\SourceTree.exe'
)

$customTools = 'C:\Program Files (x86)\Rockwell Software\RSLogix 5000\Common\CustomToolsMenu.xml'

$rows = @(
    [pscustomobject]@{ Component = '.NET SDK'; Status = if ($dotnet) { 'FOUND' } else { 'MISSING' }; Path = if ($dotnet) { $dotnet.Source } else { '' } },
    [pscustomobject]@{ Component = 'Git'; Status = if ($git) { 'FOUND' } else { 'MISSING' }; Path = if ($git) { $git.Source } else { '' } },
    [pscustomobject]@{ Component = 'l5xgit'; Status = if ($L5xGitPath -and (Test-Path -LiteralPath $L5xGitPath)) { 'FOUND' } else { 'REQUIRED' }; Path = $L5xGitPath },
    [pscustomobject]@{ Component = 'Studio 5000 CustomToolsMenu.xml'; Status = if (Test-Path -LiteralPath $customTools) { 'FOUND' } else { 'MISSING' }; Path = $customTools },
    [pscustomobject]@{ Component = 'Logix Designer Compare Tool'; Status = if ($RslComparePath) { 'FOUND' } else { 'OPTIONAL / NOT FOUND' }; Path = $RslComparePath },
    [pscustomobject]@{ Component = 'WinMerge'; Status = if ($winMerge) { 'FOUND' } else { 'OPTIONAL / NOT FOUND' }; Path = $winMerge },
    [pscustomobject]@{ Component = 'SourceTree'; Status = if ($sourceTree) { 'FOUND' } else { 'OPTIONAL / NOT FOUND' }; Path = $sourceTree }
)

$rows | Format-Table -AutoSize

Write-Host ""
Write-Host "Required for Commit/Restore: Studio 5000, Git, .NET 8 SDK, and l5xgit." -ForegroundColor Cyan
Write-Host "Native ladder-aware Compare additionally uses Logix Designer Compare Tool." -ForegroundColor Cyan
Write-Host "WinMerge and SourceTree are optional auxiliary views." -ForegroundColor Cyan
