# Architecture

## Design goals

The toolkit keeps the real engineering tools authoritative:

```text
Studio 5000
    |
    v
l5xgit + Git
    |
    +--> visual Commit workflow
    +--> native Logix Compare workflow
    +--> safe Restore workflow
```

The browser UIs explain operations in plain English; they do not simulate Git or Logix behavior.

## Three-host v0.1 architecture

The current release uses three independent loopback hosts:

```text
CommitHost   127.0.0.1:5187
CompareHost  127.0.0.1:5191
RestoreHost  127.0.0.1:5192
```

Reasons for keeping this architecture in v0.1:

- each workflow was developed and proofed independently;
- failures stay isolated;
- the Studio custom tool deep-links to exactly one operation;
- it avoids introducing consolidation risk immediately before sharing/demo.

A future branch may consolidate these pages into one VCS host. That should reuse the proven handlers rather than rewrite the behavior.

## Source representation

Git stores the exploded Logix representation produced by `l5xgit`.

Do not store an ACD or whole-project L5X for every commit.

Derived artifacts are created only when needed.

### Commit

```text
Saved ACD
→ temporary copy
→ L5X export
→ explode
→ compare/stage
→ Git commit
```

A saved timestamp does not prove an engineering change exists. Empty-commit/no-change cases are shown as **Already up to date**, not as failures.

### Compare

Historical side:

```text
selected Git commit
→ detached temp worktree
→ exploded-content
→ l5xgit implode
→ temporary historical L5X
```

Current side:

```text
saved working ACD
→ temp ACD copy
→ l5xgit acd2l5x
→ temporary current L5X
```

Then:

```text
historical L5X (left)
        +
current L5X (right)
        ↓
RSLCompare.exe
```

This gives engineers the native ladder-aware Logix comparison instead of asking them to interpret XML diffs.

### Restore Working Project

```text
selected Git commit
→ detached temp worktree
→ rebuild temporary ACD
→ wait for base ACD to unlock
→ timestamped backup current ACD
→ replace base-named ACD
→ prepare existing Studio Open Project dialog
→ engineer clicks Open
```

Restore changes the local working project. It does **not** move the Git branch pointer backward or delete later history.

## Native stderr rule

Git and other native Windows command-line tools may write normal progress to stderr even on success.

For native processes:

> stderr alone is not failure; process exit code is authoritative.

This matters particularly for `git worktree add`, which can print `Preparing worktree...` to stderr while succeeding.

## Project/repository boundary

Recommended layout:

```text
<ProjectFolder>/
    Project.ACD
    Project_L5xGit.yml

    Project-vcs/
        .git/
        exploded-content/
```

The ACD workspace is not itself a Git repository. This keeps binary ACD files and restore backups out of the normal source tree.

## Restore safety

Restore creates a backup immediately before replacing the base-named ACD:

```text
Project.ACD
Project.RESTORE-BACKUP-YYYYMMDD-HHMMSS.acd
```

After restore, `Project.ACD` is the canonical working project and contains the selected historical engineering state.

## Same-instance Studio reopen

The toolkit intentionally does not launch a second Logix Designer instance after restore.

It prepares the Open Project dialog in the existing Studio 5000 process and leaves the final Open click to the engineer.

## Future work

Not part of v0.1:

- one host / one port with `/commit`, `/compare`, `/restore`;
- branch creation/switching;
- CI/CD and test-console integration.

CI/CD should remain a separate product surface from the VCS workflow even if the systems integrate later.
