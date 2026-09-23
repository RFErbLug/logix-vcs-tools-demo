param(
    [Parameter(Mandatory = $true)][string]$ProjectPath
)

$ErrorActionPreference = 'Stop'

Write-Host 'Preparing the existing Studio 5000 window to reopen the restored ACD...' -ForegroundColor Cyan

$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$projectDir = Split-Path -Parent $ProjectPath
$projectFile = Split-Path -Leaf $ProjectPath

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project not found: $ProjectPath"
}

function Get-LogixProcesses {
    @(
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.MainWindowHandle -ne 0 -and (
                    $_.ProcessName -match '(?i)(RSLogix5000|LogixDesigner|Studio5000)' -or
                    $_.MainWindowTitle -match '(?i)(Logix Designer|Studio 5000|RSLogix 5000)'
                )
            }
    )
}

function Test-ExclusiveOpen {
    param([string]$Path)
    try {
        $s = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
        $s.Close()
        return $true
    }
    catch {
        return $false
    }
}

$logix = @(Get-LogixProcesses)
if ($logix.Count -eq 0) {
    throw 'No running Logix Designer window was detected. Start Studio 5000 first and leave the project closed.'
}
if ($logix.Count -gt 1) {
    Write-Host 'More than one Logix Designer window is running; refusing to guess.' -ForegroundColor Yellow
    $logix | Select-Object ProcessName,Id,MainWindowTitle | Format-Table -AutoSize
    exit 2
}

$target = $logix[0]
$originalPid = $target.Id
$originalHandle = [IntPtr]$target.MainWindowHandle

Write-Host "Target Logix PID: $originalPid"
Write-Host "Target window:    $($target.MainWindowTitle)"
Write-Host "Project folder:   $projectDir"
Write-Host "Project file:     $projectFile"
Write-Host ''

if (-not (Test-ExclusiveOpen -Path $ProjectPath)) {
    Write-Host 'STOP: ACD is still locked.' -ForegroundColor Yellow
    Write-Host 'Use File -> Close in Studio 5000, leave Studio itself running, then run this test again.'
    exit 3
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class PrepareOpenNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    public const int SW_RESTORE = 9;
}
'@

function Get-TopWindowsForProcess {
    param([int]$ProcessId)

    $items = [System.Collections.ArrayList]::new()
    $callback = [PrepareOpenNative+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)

        [uint32]$windowProcessId = 0
        [void][PrepareOpenNative]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId)

        if ($windowProcessId -eq $ProcessId -and [PrepareOpenNative]::IsWindowVisible($hWnd)) {
            $title = New-Object System.Text.StringBuilder 1024
            $class = New-Object System.Text.StringBuilder 256
            [void][PrepareOpenNative]::GetWindowText($hWnd, $title, $title.Capacity)
            [void][PrepareOpenNative]::GetClassName($hWnd, $class, $class.Capacity)

            [void]$items.Add([pscustomobject]@{
                Handle = $hWnd
                Title = $title.ToString()
                Class = $class.ToString()
            })
        }

        return $true
    }

    [void][PrepareOpenNative]::EnumWindows($callback, [IntPtr]::Zero)
    return $items.ToArray()
}

function ConvertTo-SendKeysLiteral {
    param([string]$Text)

    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        switch ($ch) {
            '+' { [void]$sb.Append('{+}') }
            '^' { [void]$sb.Append('{^}') }
            '%' { [void]$sb.Append('{%}') }
            '~' { [void]$sb.Append('{~}') }
            '(' { [void]$sb.Append('{(}') }
            ')' { [void]$sb.Append('{)}') }
            '[' { [void]$sb.Append('{[}') }
            ']' { [void]$sb.Append('{]}') }
            '{' { [void]$sb.Append('{{}') }
            '}' { [void]$sb.Append('{}}') }
            default { [void]$sb.Append($ch) }
        }
    }
    return $sb.ToString()
}

Write-Host '[1/4] Activating existing Logix window...' -ForegroundColor Cyan
[void][PrepareOpenNative]::ShowWindow($originalHandle, [PrepareOpenNative]::SW_RESTORE)
[void][PrepareOpenNative]::SetForegroundWindow($originalHandle)
Start-Sleep -Milliseconds 400

Write-Host '[2/4] Opening Logix Open Project dialog...' -ForegroundColor Cyan
[System.Windows.Forms.SendKeys]::SendWait('^o')

$dialog = $null
$deadline = (Get-Date).AddSeconds(12)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250

    $dialog = @(
        Get-TopWindowsForProcess -ProcessId $originalPid |
            Where-Object {
                $_.Handle -ne $originalHandle -and
                $_.Class -eq '#32770' -and
                $_.Title -match '(?i)open project'
            }
    ) | Select-Object -First 1

    if ($dialog) { break }
}

if (-not $dialog) {
    Write-Host 'FAIL: Logix Open Project dialog was not detected in the existing PID.' -ForegroundColor Red
    exit 4
}

Write-Host "  Open Project dialog found in PID $originalPid" -ForegroundColor Green

Write-Host '[3/4] Navigating dialog to restored ACD folder...' -ForegroundColor Cyan
[void][PrepareOpenNative]::SetForegroundWindow([IntPtr]$dialog.Handle)
Start-Sleep -Milliseconds 250

# Windows common dialogs use Alt+D to focus the address/location field.
[System.Windows.Forms.SendKeys]::SendWait('%d')
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait('^a')
[System.Windows.Forms.SendKeys]::SendWait((ConvertTo-SendKeysLiteral -Text $projectDir))
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Milliseconds 900

# Re-resolve the File name hierarchy after navigation.
$fileNameContainer = [PrepareOpenNative]::GetDlgItem([IntPtr]$dialog.Handle, 1148)
if ($fileNameContainer -eq [IntPtr]::Zero) {
    Write-Host 'STOPPED SAFELY: File name container 1148 not found.' -ForegroundColor Yellow
    exit 5
}

$comboHwnd = [PrepareOpenNative]::FindWindowEx(
    $fileNameContainer,
    [IntPtr]::Zero,
    'ComboBox',
    $null
)
if ($comboHwnd -eq [IntPtr]::Zero) {
    Write-Host 'STOPPED SAFELY: File name ComboBox child not found.' -ForegroundColor Yellow
    exit 5
}

$editHwnd = [PrepareOpenNative]::FindWindowEx(
    $comboHwnd,
    [IntPtr]::Zero,
    'Edit',
    $null
)
if ($editHwnd -eq [IntPtr]::Zero) {
    Write-Host 'STOPPED SAFELY: File name Edit child not found.' -ForegroundColor Yellow
    exit 5
}

if (-not [PrepareOpenNative]::SetWindowText($editHwnd, $projectFile)) {
    Write-Host 'STOPPED SAFELY: Could not put the ACD filename into the File name box.' -ForegroundColor Yellow
    exit 5
}

Start-Sleep -Milliseconds 300

$verify = New-Object System.Text.StringBuilder 1024
[void][PrepareOpenNative]::GetWindowText($editHwnd, $verify, $verify.Capacity)
if ($verify.ToString() -ne $projectFile) {
    Write-Host "STOPPED SAFELY: filename box contains '$($verify.ToString())' instead of '$projectFile'." -ForegroundColor Yellow
    exit 5
}

Write-Host "  Folder requested: $projectDir"
Write-Host "  File name set:    $projectFile" -ForegroundColor Green

Write-Host '[4/4] Checking that the ACD is visible in the dialog...' -ForegroundColor Cyan

$dialogRoot = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$dialog.Handle)
$visible = $false
if ($null -ne $dialogRoot) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $projectFile
    )
    $match = $dialogRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $nameCondition
    )
    $visible = ($null -ne $match)
}

Write-Host ''
if ($visible) {
    Write-Host 'PASS: OPEN PROJECT DIALOG IS READY.' -ForegroundColor Green
    Write-Host "  Existing Logix PID: $originalPid"
    Write-Host "  Folder:             $projectDir"
    Write-Host "  ACD:                $projectFile"
    Write-Host ''
    Write-Host 'Click Open in the Logix dialog.' -ForegroundColor Green
}
else {
    Write-Host 'PARTIAL PASS: filename is prepared, but the ACD was not detected in the visible file list.' -ForegroundColor Yellow
    Write-Host "  Existing Logix PID: $originalPid"
    Write-Host "  Requested folder:   $projectDir"
    Write-Host "  File name:          $projectFile"
    Write-Host ''
    Write-Host 'Inspect the Logix dialog. The restored ACD filename has been entered for you.'
}

Write-Host ''
Write-Host 'The script intentionally leaves the Open Project dialog open and does NOT click Open.' -ForegroundColor Cyan
