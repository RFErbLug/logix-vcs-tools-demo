param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

function Show-Error([string]$Message) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        'Logix Compare Versions - SourceTree',
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

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'SourceTree\SourceTree.exe'),
        'C:\Program Files\Atlassian\SourceTree\SourceTree.exe',
        'C:\Program Files (x86)\Atlassian\SourceTree\SourceTree.exe'
    )

    $sourceTree = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if (-not $sourceTree) {
        foreach ($root in @((Join-Path $env:LOCALAPPDATA 'SourceTree'), (Join-Path $env:LOCALAPPDATA 'Atlassian'))) {
            if (Test-Path -LiteralPath $root) {
                $sourceTree = Get-ChildItem -LiteralPath $root -Filter SourceTree.exe -File -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 1 -ExpandProperty FullName
                if ($sourceTree) { break }
            }
        }
    }

    if (-not $sourceTree) {
        throw 'SourceTree.exe could not be found. Install SourceTree or open it once, then try again.'
    }

    Start-Process -FilePath $sourceTree -ArgumentList @('-f', $repoRoot, 'log')
}
catch {
    Show-Error $_.Exception.Message
    exit 1
}
