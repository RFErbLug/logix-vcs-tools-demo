param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot 'artifacts\publish'

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

$projects = @(
    @{ Name = 'commit';  Project = 'src\commit\host\VcsVisualHost.csproj' },
    @{ Name = 'compare'; Project = 'src\compare\host\LogixCompareHost.csproj' },
    @{ Name = 'restore'; Project = 'src\restore\host\RestoreVisualHost.csproj' }
)

foreach ($item in $projects) {
    $projectPath = Join-Path $repoRoot $item.Project
    $outputPath = Join-Path $publishRoot $item.Name

    Write-Host ""
    Write-Host "Publishing $($item.Name) host..." -ForegroundColor Cyan

    & dotnet publish $projectPath -c $Configuration -o $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($item.Name)."
    }
}

Write-Host ""
Write-Host "All VCS hosts published successfully." -ForegroundColor Green
Write-Host "Output: $publishRoot"
