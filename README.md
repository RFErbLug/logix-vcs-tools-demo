# Logix VCS Workflow Showcase

> **SHOWCASE / DEMO ONLY — NOT A PRODUCT**
>
> This repository is an unofficial engineering showcase demonstrating possible Studio 5000 Logix Designer version-control workflows. It is **not** a Rockwell Automation product, official sample, supported offering, validated reference architecture, customer standard, or production deployment framework.
>
> The original material in this repository is **not offered as open-source software**, and no open-source license is granted for that original material. Public and third-party components used or referenced by the demo remain governed by their own licenses. References to Rockwell Automation products describe technologies used in the demonstration and do not imply official product status, endorsement, or support.

An unofficial Windows demonstration of how a simple, visual Git workflow could be presented inside **Studio 5000 Logix Designer**.

The demo tooling wraps real Git and Rockwell Automation `l5xgit` operations with browser-based presentation workflows for:

- **Commit** — capture the saved ACD as a recoverable engineering version.
- **Compare Version** — compare a historical Git version with the current saved project in **Logix Designer Compare Tool**.
- **Restore Working Project** — rebuild a historical Git version, back up the current ACD, and restore the selected engineering state under the normal project filename.

Two optional auxiliary tools are also included:

- **Difftool - WinMerge**
- **Difftool - SourceTree**

> **Status:** Demo snapshot v0.1.0 records the three-host workflow exercised in the development environment. This is demonstration evidence only and is not a product qualification, support statement, or compatibility guarantee.

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

## Demo installation

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

For the demonstration workstation, the installer:

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

## Repository boundary

This repository exists to preserve and demonstrate the showcase implementation.

It is not published as a reusable software distribution, supported installation
package, official reference architecture, or open-source project.

Any reuse outside the demonstration requires separate review of ownership,
licensing, security, compatibility, validation, deployment, and support
requirements.
## Project status

This is an unofficial engineering showcase, not a Rockwell Automation product, supported feature, official sample, or production recovery solution. The demonstrated workflows should not be interpreted as a support, compatibility, deployment, or lifecycle commitment.
