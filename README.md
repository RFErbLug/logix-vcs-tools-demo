# Logix VCS Tools

A Windows companion toolkit for bringing a simple, visual Git workflow into **Studio 5000 Logix Designer**.

The toolkit wraps real Git and Rockwell Automation `l5xgit` operations with browser-based workflows for:

- **Commit** — capture the saved ACD as a recoverable engineering version.
- **Compare Version** — compare a historical Git version with the current saved project in **Logix Designer Compare Tool**.
- **Restore Working Project** — rebuild a historical Git version, back up the current ACD, and restore the selected engineering state under the normal project filename.

Two optional auxiliary tools are also included:

- **Difftool - WinMerge**
- **Difftool - SourceTree**

> **Status:** v0.1.0 packages the proven three-host architecture. Commit, native Logix Compare, and browser-based Restore have all been exercised end to end in the development environment.

## What the Studio 5000 Tools menu looks like

```text
Commit
Difftool - WinMerge
Difftool - SourceTree
Compare Version...
Restore Working Project...
```

The legacy generic `Difftool`, `Restore`, and `RestoreCurrentFile` entries are replaced by the clearer workflows above.

## Current architecture

v0.1.0 intentionally keeps the three browser workflows isolated:

| Workflow | Local address |
|---|---|
| Commit | `http://127.0.0.1:5187` |
| Compare Version | `http://127.0.0.1:5191` |
| Restore Working Project | `http://127.0.0.1:5192` |

All three hosts bind to loopback by default. They are not intended to be exposed to the network.

A future development branch may consolidate these into one VCS host with routes such as `/commit`, `/compare`, and `/restore`. That work is intentionally **not** part of v0.1.0.

## Mental model

```text
COMMIT
"I want to remember this."

COMPARE
"What changed?"

RESTORE WORKING PROJECT
"Make my local ACD contain that captured version again."
```

## Project layout

A normal project looks like:

```text
MachineA/
    MachineA.ACD
    MachineA_L5xGit.yml

    MachineA-vcs/
        .git/
        exploded-content/
```

The ACD working folder itself is not the Git repository. The child `-vcs` folder contains Git and the exploded Logix source.

Git stores the source-oriented exploded representation. Historical ACD/L5X copies are generated on demand when needed; they are not stored for every commit.

## Requirements

Required for the core workflow:

- Windows
- Studio 5000 Logix Designer
- Git
- .NET 8 SDK
- Rockwell Automation `l5xgit` from:
  - https://github.com/RockwellAutomation/ra-logix-designer-vcs-custom-tools

For native ladder-aware Compare:

- Logix Designer Compare Tool / `RSLCompare.exe`

Optional:

- WinMerge
- SourceTree

Run the prerequisite checker:

```powershell
.\scripts\check-prereqs.ps1 -L5xGitPath 'C:\path\to\l5xgit.exe'
```

## Install

Open **PowerShell as Administrator** from the repository root.

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe'
```

If Logix Designer Compare Tool is in a nonstandard location:

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe' `
  -RslComparePath 'C:\path\to\RSLCompare.exe'
```

The installer:

1. publishes all three .NET hosts;
2. installs them under `C:\ProgramData\LogixVcsTools`;
3. backs up `CustomToolsMenu.xml`;
4. installs the Studio 5000 menu entries;
5. preserves the current three-port architecture.

Restart Studio 5000 after installation.

See [docs/INSTALL.md](docs/INSTALL.md) for details.

## First use

For a new ACD:

1. Save the project in its own folder.
2. Use **Tools → Commit**.
3. Accept or choose the suggested repository folder.
4. The tool creates the Git repository, `exploded-content`, and the project `_L5xGit.yml`.
5. Capture V1.

After that:

```text
Edit in Studio
→ Save ACD
→ Commit
```

Unsaved Studio 5000 edits are never included.

## Compare Version

Use **Tools → Compare Version...**.

Choose a captured version.

The toolkit generates temporary comparison artifacts:

```text
Historical Git source
→ temporary L5X
                    \
                     > Logix Designer Compare Tool
                    /
Current saved ACD
→ temporary L5X
```

The historical version is shown on the **left** and the current saved project on the **right**.

The temporary L5X files are derived artifacts; they are not committed to Git.

## Restore Working Project

Restore does not rewrite Git history.

Example:

```text
Git:
V1 → V2 → V3

Restore V1

Working ACD:
V1 engineering content

Git:
V1 → V2 → V3
```

The selected historical state is rebuilt first. When ready, the user closes the current project with **File → Close** while leaving Studio 5000 running.

The toolkit then:

1. creates a timestamped safety backup of the current ACD;
2. replaces the base-named working ACD with the selected version;
3. prepares the existing Studio 5000 Open Project dialog;
4. leaves the final **Open** click to the engineer.

The normal project filename stays the same.

## Documentation

- [Beginner User Guide](docs/USER_GUIDE.md)
- [Installation](docs/INSTALL.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Ports and local hosts](docs/PORTS.md)
- [Video demo runbook](docs/VIDEO_DEMO.md)
- [Third-party dependencies](THIRD_PARTY.md)
- [Security notes](SECURITY.md)

## Uninstall / rollback

The installer stores the pre-install Studio 5000 menu as a backup.

Run elevated:

```powershell
.\scripts\uninstall.ps1
```

This restores the backed-up `CustomToolsMenu.xml`.

## Sharing this package as its own GitHub repository

This directory is intentionally self-contained.

If GitHub CLI is installed and authenticated:

```powershell
.\scripts\publish-github.ps1 `
  -Repository 'your-account/logix-vcs-tools' `
  -Visibility private
```

Use `-Visibility public` only after you have reviewed your organization/company requirements and selected an appropriate license.

## Project status

This is an engineering prototype/toolkit, not a Rockwell Automation product or supported product feature. Test it in an appropriate non-production environment before relying on it for production recovery.
