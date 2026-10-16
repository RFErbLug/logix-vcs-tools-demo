# Quick Start

> **SHOWCASE / DEMO ONLY — NOT A PRODUCT**
>
> This repository is an unofficial engineering showcase demonstrating possible Studio 5000 Logix Designer version-control workflows. It is **not** a Rockwell Automation product, official sample, supported offering, validated reference architecture, customer standard, or production deployment framework.
>
> The original material in this repository is **not offered as open-source software**, and no open-source license is granted for that original material. Public and third-party components used or referenced by the demo remain governed by their own licenses. References to Rockwell Automation products describe technologies used in the demonstration and do not imply official product status, endorsement, or support.

**New here? Start here.**

This is the shortest path for reproducing the VCS showcase on a demonstration workstation.

## 1. Prerequisites

You need:

- Windows
- Studio 5000 Logix Designer
- Git
- .NET 8 SDK
- Rockwell Automation `l5xgit.exe`

For native Logix Compare, you also need:

- Logix Designer Compare Tool (`RSLCompare.exe`)

Optional:

- WinMerge
- SourceTree

Rockwell Automation `l5xgit`:

https://github.com/RockwellAutomation/ra-logix-designer-vcs-custom-tools

---

## 2. Clone this repository

```powershell
git clone https://github.com/RFErbLug/logix-vcs-tools-demo.git
cd logix-vcs-tools-demo
```

---

## 3. Check prerequisites

Replace the path below with the location of your `l5xgit.exe`.

```powershell
.\scripts\check-prereqs.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe'
```

You should see the required components reported as found.

---

## 4. Install the demo tooling

Open **PowerShell as Administrator** in the repository folder.

Run:

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe'
```

If Logix Designer Compare Tool is installed in a nonstandard location:

```powershell
.\scripts\install.ps1 `
  -L5xGitPath 'C:\path\to\l5xgit.exe' `
  -RslComparePath 'C:\path\to\RSLCompare.exe'
```

The installer builds the three local hosts and installs them under:

```text
C:\ProgramData\LogixVcsTools
```

It also backs up your current Studio 5000 custom-tools configuration before changing it.

---

## 5. Restart Studio 5000

After installation, restart Studio 5000.

Under **Tools**, you should see:

```text
Commit
Difftool - WinMerge
Difftool - SourceTree
Compare Version...
Restore Working Project...
```

WinMerge and SourceTree are optional.

---

## 6. Start with a Logix project

Open or create an ACD and save it.

Example:

```text
C:\Projects\MachineA\
    MachineA.ACD
```

Then choose:

```text
Tools → Commit
```

The first time you use Commit, the tool will help create the version-control structure.

The recommended result is:

```text
C:\Projects\MachineA\
    MachineA.ACD
    MachineA_L5xGit.yml

    MachineA-vcs\
        .git\
        exploded-content\
```

The ACD folder itself is not the Git repository.

---

## 7. Capture your first version

In the Commit page:

1. Accept or choose the suggested `MachineA-vcs` repository folder.
2. Enter a useful description.
3. Click **Capture version**.

Example:

```text
Initial working machine sequence
```

That becomes your first captured engineering version.

---

## 8. Normal daily workflow

The normal pattern is:

```text
Make change in Studio 5000
→ Save the ACD
→ Tools → Commit
→ Capture version
```

Important:

> Unsaved Studio 5000 edits are not included.

If the saved ACD contains no engineering change, the tool will report that the project is already up to date and will not create an unnecessary version.

---

## 9. Compare an older version

First save the current ACD.

Then choose:

```text
Tools → Compare Version...
```

Select a captured Git version and start the comparison.

The toolkit opens Rockwell's Logix Designer Compare Tool with:

```text
LEFT                         RIGHT

Historical Git version      Current saved project
```

Temporary L5X files are generated for comparison only.

They are not stored as extra versions in Git.

---

## 10. Restore the working project

Choose:

```text
Tools → Restore Working Project...
```

Select the version you want to restore.

The toolkit first rebuilds that historical project in a temporary location.

When prompted:

```text
File → Close
```

in Studio 5000.

Leave Studio 5000 itself running.

The toolkit then:

1. creates a timestamped backup of the current ACD;
2. replaces the normal base-named ACD with the selected version;
3. prepares the existing Studio 5000 Open Project dialog;
4. leaves the final **Open** click to you.

Example:

```text
MachineA.ACD
MachineA.RESTORE-BACKUP-20260923-173037.acd
```

After restore, `MachineA.ACD` is still the normal working project filename.

Git history is not rewritten.

---

## 11. Local UI ports

The current demo release uses three separate local hosts:

```text
Commit   → http://127.0.0.1:5187
Compare  → http://127.0.0.1:5191
Restore  → http://127.0.0.1:5192
```

These are local-only loopback services.

---

## 12. Simple mental model

```text
COMMIT
"I want to remember this."

COMPARE
"What changed?"

RESTORE WORKING PROJECT
"Make my working ACD contain that captured version again."
```

---

## More documentation

For more detail:

- `README.md` — project overview
- `docs/USER_GUIDE.md` — beginner user guide
- `docs/INSTALL.md` — installation details
- `docs/ARCHITECTURE.md` — how the toolkit works internally
- `docs/PORTS.md` — current host/port layout
- `docs/VIDEO_DEMO.md` — demo recording walkthrough
- `SECURITY.md` — local-host and restore safety notes

---

## Important note

This repository is an unofficial engineering showcase/demo.

It is not a Rockwell Automation product, official sample, supported product feature, validated reference architecture, or production deployment framework.

The demonstrated workflow is for presentation and engineering exploration only; it should not be relied upon as a production recovery or support mechanism.
