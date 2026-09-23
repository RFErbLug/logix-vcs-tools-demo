param(
    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [ValidateSet('private','public')]
    [string]$Visibility = 'private'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git was not found on PATH.'
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) was not found. Install it and run gh auth login first.'
}

$work = Join-Path $env:TEMP ("logix-vcs-tools-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    Copy-Item -Path (Join-Path $repoRoot '*') -Destination $work -Recurse -Force
    Remove-Item -LiteralPath (Join-Path $work 'artifacts') -Recurse -Force -ErrorAction SilentlyContinue

    & git -C $work init -b main
    & git -C $work add .
    & git -C $work commit -m 'Initial Logix VCS Tools package'
    if ($LASTEXITCODE -ne 0) {
        throw 'Initial git commit failed. Configure git user.name/user.email if needed.'
    }

    $visibilityFlag = if ($Visibility -eq 'public') { '--public' } else { '--private' }
    & gh repo create $Repository $visibilityFlag --source $work --remote origin --push
    if ($LASTEXITCODE -ne 0) {
        throw 'gh repo create failed.'
    }

    Write-Host ""
    Write-Host "Created and pushed $Repository." -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
