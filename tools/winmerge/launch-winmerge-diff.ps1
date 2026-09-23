param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

function Show-Error([string]$Message) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        'Logix Compare Versions - WinMerge',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
}

try {
    $projectPath = [System.IO.Path]::GetFullPath($ProjectPath)
    $projectDir = Split-Path -Parent $projectPath
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $configPath = Join-Path $projectDir ($projectName + '_L5xGit.yml')

    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "Version-control configuration was not found: $configPath. Run Commit once to configure this project."
    }

    $destinationLine = Get-Content -LiteralPath $configPath | Where-Object { $_ -match '^\s*destination_path\s*:' } | Select-Object -First 1
    if (-not $destinationLine) {
        throw "destination_path was not found in $configPath."
    }

    $destinationPath = ($destinationLine -replace '^\s*destination_path\s*:\s*', '').Trim().Trim("'").Trim('"')
    if (-not (Test-Path -LiteralPath $destinationPath)) {
        throw "Configured Git content directory does not exist: $destinationPath"
    }

    $repoRoot = (& git -C $destinationPath rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
        throw "Could not resolve the Git repository containing: $destinationPath"
    }
    $repoRoot = $repoRoot.Trim()

    $count = (& git -C $repoRoot rev-list --count HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [int]$count -lt 2) {
        throw 'At least two commits are required before versions can be compared.'
    }

    $winMergeCandidates = @(
        'C:\Program Files\WinMerge\WinMergeU.exe',
        'C:\Program Files (x86)\WinMerge\WinMergeU.exe'
    )
    $winMerge = $winMergeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $winMerge) {
        throw 'WinMerge was not found. Install WinMerge, then try Compare Versions - WinMerge again.'
    }

    & git -C $repoRoot -c 'diff.tool=winmerge' -c "difftool.winmerge.path=$winMerge" -c 'difftool.prompt=false' difftool 'HEAD~1' 'HEAD' --dir-diff --no-prompt
    if ($LASTEXITCODE -ne 0) {
        throw "Git difftool exited with code $LASTEXITCODE."
    }
}
catch {
    Show-Error $_.Exception.Message
    exit 1
}
