# Installation

## Prerequisites

Required:

- Windows
- Studio 5000 Logix Designer
- Git
- .NET 8 SDK
- `l5xgit.exe`

Upstream `l5xgit` project:

https://github.com/RockwellAutomation/ra-logix-designer-vcs-custom-tools

For native Logix Compare:

- Logix Designer Compare Tool / `RSLCompare.exe`

Optional:

- WinMerge
- SourceTree

## Check prerequisites

From repository root:

```powershell
.\scripts\check-prereqs.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe'
```

## Build only

```powershell
.\scripts\build.ps1
```

Published hosts are written under:

```text
artifacts\publish\commit
artifacts\publish\compare
artifacts\publish\restore
```

## Install

Run **PowerShell as Administrator**:

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe'
```

Optional explicit Compare path:

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe' `
  -RslComparePath 'C:\Program Files (x86)\Rockwell Software\Logix Designer Tools\Logix Designer Compare Tool\RSLCompare.exe'
```

The compare host also knows common RSLCompare installation locations, so the explicit argument is not normally required.

## Installed files

Default root:

```text
C:\ProgramData\LogixVcsTools\
    CommitHost\
    CompareHost\
    RestoreHost\
    DiffWinMerge\
    DiffSourceTree\
    backups\
    install-state.json
```

## Studio 5000 menu

The installer backs up the current menu, removes the superseded generic VCS entries, and installs:

```text
Commit
Difftool - WinMerge
Difftool - SourceTree
Compare Version...
Restore Working Project...
```

Unrelated custom tools are preserved.

Restart Studio 5000 after install.

## Optional tools

To omit WinMerge:

```powershell
.\scripts\install.ps1 -L5xGitPath 'C:\path\to\l5xgit.exe' -SkipWinMerge
```

To omit SourceTree:

```powershell
.\scripts\install.ps1 -L5xGitPath 'C:\path\to\l5xgit.exe' -SkipSourceTree
```

## Uninstall / rollback

Run elevated:

```powershell
.\scripts\uninstall.ps1
```

The original `CustomToolsMenu.xml` captured at installation is restored.

Installed files are left in `C:\ProgramData\LogixVcsTools` so the backup and install record remain available.
