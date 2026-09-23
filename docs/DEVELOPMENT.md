# Development

## Repository layout

```text
src/
  commit/
    index.html
    host/
  compare/
    index.html
    host/
  restore/
    index.html
    restore-worker.ps1
    prepare-restored-open.ps1
    host/

tools/
  winmerge/
  sourcetree/

scripts/
  build.ps1
  check-prereqs.ps1
  install.ps1
  uninstall.ps1
  publish-github.ps1
```

## Default ports

The hosts currently own separate loopback ports:

- Commit: 5187
- Compare: 5191
- Restore: 5192

Do not casually change these in one component only. Update the host default, README, PORTS.md, installer output, and any demo documentation together.

## Build

```powershell
.\scripts\build.ps1
```

## Source-of-truth behavior

The browser is a presentation/orchestration layer.

- Git is authoritative for version history.
- `l5xgit` is authoritative for Logix export/explode/implode/restore conversion.
- RSLCompare is authoritative for native Logix project comparison.

Avoid duplicating those semantics in JavaScript.

## Important invariants

### Commit

- unsaved Studio edits are not captured;
- no engineering delta is a successful no-change outcome;
- user-facing V numbers count engineering-content commits, not arbitrary repository commits;
- publishing is separate from local capture.

### Compare

- historical Git version is the left side;
- current saved project is the right side;
- generated L5X files are temporary artifacts;
- successful stderr output is not automatically an error.

### Restore

- historical ACD is rebuilt before the working ACD is touched;
- backup current ACD immediately before replacement;
- restored content replaces the same base-named working ACD;
- Git history is not reset/re-written;
- keep Studio 5000 running while the current project is closed;
- prepare the existing Studio Open Project dialog and leave the final Open click to the engineer.

## Planned future branch

A later branch may consolidate the three hosts into one VCS application:

```text
/commit
/compare
/restore
```

Do not mix CI/CD/test-console pages into that VCS shell. CI/CD remains a separate product surface.
