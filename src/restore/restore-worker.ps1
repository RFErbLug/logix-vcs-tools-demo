param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$L5xGitPath,
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$DestinationPath,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [Parameter(Mandatory = $true)][string]$StatePath,
    [string]$Version = 'Selected version',
    [string]$PrepareOpenPath
)

$ErrorActionPreference = 'Stop'
$logPath = [System.IO.Path]::ChangeExtension($StatePath, '.log')
$tempRoot = $null
$worktree = $null
$script:backupPath = $null

function Write-State {
    param(
        [string]$Stage,
        [string]$Status,
        [string]$Message,
        [string]$Detail = ''
    )

    $payload = [ordered]@{
        stage = $Stage
        status = $Status
        message = $Message
        detail = $Detail
        timestamp = (Get-Date).ToString('o')
        project_path = $ProjectPath
        commit_sha = $CommitSha
        version = $Version
        backup_path = $script:backupPath
    }

    $tmp = "$StatePath.tmp"
    $payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tmp -Encoding UTF8
    Move-Item -LiteralPath $tmp -Destination $StatePath -Force
}

function Test-FileUnlocked {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $true
    }

    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
        $stream.Close()
        return $true
    }
    catch {
        return $false
    }
}

function Get-RelativeDestination {
    param([string]$Repository, [string]$Destination)

    $repoFull = [System.IO.Path]::GetFullPath($Repository).TrimEnd('\')
    $destFull = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')

    if ($destFull.Equals($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return ''
    }

    $prefix = $repoFull + '\'
    if (-not $destFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Configured destination path '$destFull' is not inside Git repository '$repoFull'."
    }

    return $destFull.Substring($prefix.Length)
}

function New-RestoreBackup {
    param([string]$Path)

    $dir = Split-Path -Parent $Path
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = Join-Path $dir "$name.RESTORE-BACKUP-$stamp.acd"
    Copy-Item -LiteralPath $Path -Destination $backup -Force
    return $backup
}

try {
    Write-State -Stage 'prepare' -Status 'running' -Message 'Preparing selected Git version...'

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project ACD was not found: $ProjectPath"
    }
    if (-not (Test-Path -LiteralPath $L5xGitPath)) {
        throw "l5xgit.exe was not found: $L5xGitPath"
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & git -C $RepoRoot cat-file -e "$CommitSha^{commit}" 2>$null
        $catFileExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($catFileExitCode -ne 0) {
        throw "Git commit '$CommitSha' could not be resolved."
    }

    $relativeDestination = Get-RelativeDestination -Repository $RepoRoot -Destination $DestinationPath
    $tempRoot = Join-Path $env:TEMP ("logix-restore-" + [guid]::NewGuid().ToString('N'))
    $worktree = Join-Path $tempRoot 'worktree'
    $buildDir = Join-Path $tempRoot 'build'
    New-Item -ItemType Directory -Force -Path $tempRoot, $buildDir | Out-Null

    Add-Content -LiteralPath $logPath -Value "Creating detached worktree for $CommitSha"

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $worktreeOutput = @(& git -C $RepoRoot worktree add --detach $worktree $CommitSha 2>&1)
        $worktreeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $worktreeOutput) {
        Add-Content -LiteralPath $logPath -Value ([string]$line)
    }

    if ($worktreeExitCode -ne 0) {
        throw "Could not create the temporary Git worktree for $CommitSha."
    }

    $tempDestination = if ([string]::IsNullOrWhiteSpace($relativeDestination)) {
        $worktree
    }
    else {
        Join-Path $worktree $relativeDestination
    }

    if (-not (Test-Path -LiteralPath $tempDestination)) {
        throw "The selected version does not contain the configured Logix content path: $relativeDestination"
    }

    Write-State -Stage 'rebuild' -Status 'running' -Message 'Rebuilding the selected Logix project...' -Detail $CommitSha

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $tempAcd = Join-Path $buildDir ($baseName + '.ACD')
    $tempConfig = Join-Path $buildDir ($baseName + '_L5xGit.yml')

    @(
        "destination_path: '$tempDestination'"
        'prompt_for_commit_message: false'
    ) | Set-Content -LiteralPath $tempConfig -Encoding UTF8

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'

        & $L5xGitPath restoreacd --acd $tempAcd 2>&1 | ForEach-Object {
            $line = [string]$_
            Add-Content -LiteralPath $logPath -Value $line

            if ($line -match 'Restoring L5x from') {
                Write-State -Stage 'rebuild' -Status 'running' -Message 'Reassembling version-controlled Logix source...'
            }
            elseif ($line -match 'Open project started') {
                Write-State -Stage 'rebuild' -Status 'running' -Message 'Building the ACD with the Logix Designer SDK...'
            }
            elseif ($line -match 'Save As succeeded') {
                Write-State -Stage 'rebuild' -Status 'running' -Message 'ACD rebuild succeeded.'
            }
        }

        $restoreExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($restoreExitCode -ne 0 -or -not (Test-Path -LiteralPath $tempAcd)) {
        throw "l5xgit restore failed. See $logPath"
    }

    Write-State -Stage 'waiting_close' -Status 'waiting' -Message 'Close the current Logix project to continue.' -Detail 'Leave Studio 5000 running. Use File -> Close so the current ACD unlocks; restore will continue automatically.'

    $deadline = (Get-Date).AddMinutes(15)
    while (-not (Test-FileUnlocked -Path $ProjectPath)) {
        if ((Get-Date) -gt $deadline) {
            throw 'Timed out waiting for the current Logix project to release the ACD file.'
        }
        Start-Sleep -Milliseconds 500
    }

    Write-State -Stage 'replace' -Status 'running' -Message 'Backing up and replacing the current ACD...'

    $script:backupPath = New-RestoreBackup -Path $ProjectPath
    Add-Content -LiteralPath $logPath -Value "Backup created: $script:backupPath"

    Copy-Item -LiteralPath $tempAcd -Destination $ProjectPath -Force

    Write-State -Stage 'reopen' -Status 'running' -Message 'Restored ACD is in place. Preparing Studio 5000 to reopen it...' -Detail "Backup: $script:backupPath"

    $reopenDetail = ''
    if ($PrepareOpenPath -and (Test-Path -LiteralPath $PrepareOpenPath)) {
        try {
            $reopenOutput = & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $PrepareOpenPath -ProjectPath $ProjectPath 2>&1
            foreach ($line in @($reopenOutput)) {
                Add-Content -LiteralPath $logPath -Value ([string]$line)
            }

            if ($LASTEXITCODE -eq 0) {
                $reopenDetail = 'Studio 5000 Open Project is prepared. Click Open to load the restored ACD.'
            }
            else {
                $reopenDetail = 'The ACD was restored successfully, but the Open Project dialog could not be prepared automatically. Reopen the project manually in Studio 5000.'
            }
        }
        catch {
            Add-Content -LiteralPath $logPath -Value ("Reopen helper warning: " + $_.Exception.Message)
            $reopenDetail = 'The ACD was restored successfully, but the Open Project dialog could not be prepared automatically. Reopen the project manually in Studio 5000.'
        }
    }
    else {
        $reopenDetail = 'The ACD was restored successfully. Reopen the project manually in Studio 5000.'
    }

    Write-State -Stage 'success' -Status 'success' -Message "$Version is now the working ACD. Git history was not rewritten." -Detail "$reopenDetail Backup: $script:backupPath"
}
catch {
    Add-Content -LiteralPath $logPath -Value ("ERROR: " + $_.Exception.ToString())
    Write-State -Stage 'failed' -Status 'failed' -Message 'Restore failed.' -Detail $_.Exception.Message
    exit 1
}
finally {
    if ($worktree -and (Test-Path -LiteralPath $worktree)) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $cleanupOutput = @(& git -C $RepoRoot worktree remove --force $worktree 2>&1)
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        foreach ($line in $cleanupOutput) {
            Add-Content -LiteralPath $logPath -Value ([string]$line)
        }
    }

    if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
