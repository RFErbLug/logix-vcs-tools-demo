# Changelog

## 0.1.0 — 2026-09-23

First shareable three-host package.

### Commit

- browser-based Studio 5000 Commit workflow;
- first-run Git/YAML setup;
- repeat capture from a long-lived page;
- saved-ACD detection;
- clean no-change / Already up to date result;
- local versus remote publish state;
- readable CLI + plain-English workflow.

### Compare

- version selector;
- rebuild historical Git source to temporary L5X;
- convert current saved ACD to temporary L5X;
- launch native Logix Designer Compare Tool;
- historical version left, current saved project right;
- temporary L5X files are not stored in Git.

### Restore

- browser-based restore-point picker;
- real CLI activity + plain-English workflow;
- rebuild selected historical ACD before touching current project;
- timestamped safety backup;
- restore selected content to the same base-named ACD;
- prepare the existing Studio 5000 Open Project dialog;
- preserve Git history.

### Packaging

- one all-in-one installer;
- safe menu backup/uninstall;
- generic `C:\ProgramData\LogixVcsTools` paths;
- default ports 5187 / 5191 / 5192;
- optional WinMerge and SourceTree menu tools.
