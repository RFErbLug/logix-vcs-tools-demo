# Logix VCS Tools — Static UI Demo

These files are standalone visual/demo versions of the Logix VCS Tools user interfaces.

They are intended for:

- sharing the UI with teammates;
- screenshots and presentations;
- design review;
- opening directly from GitHub Pages or a local browser.

## Files

- `commit.html` — Commit / Capture Version UI
- `compare.html` — native Logix Compare workflow UI
- `restore.html` — Restore Working Project UI

## Important

These pages are **static previews only**.

They do not:

- connect to Studio 5000;
- run Git;
- run `l5xgit`;
- launch Logix Designer Compare Tool;
- modify or restore an ACD.

The live runtime pages remain under `src/commit`, `src/compare`, and `src/restore` in the main toolkit repository.

## Use locally

Double-click any `.html` file, or open it in a browser.

## Suggested repo location

Add this folder as:

```text
demo-ui/
    README.md
    commit.html
    compare.html
    restore.html
```

That keeps the shareable static UI separate from the real runtime implementation.
