# Video Demo Runbook

This runbook is designed for a short status/demo recording.

## Before recording

1. Use a small test ACD with at least two visibly different captured versions.
2. Save the current ACD.
3. Confirm the Studio 5000 Tools menu contains:
   - Commit
   - Difftool - WinMerge
   - Difftool - SourceTree
   - Compare Version...
   - Restore Working Project...
4. Close unrelated browser tabs and terminals.
5. Keep the three browser UIs at their default zoom.
6. Know which version contains the visible ladder/routine difference you plan to show.

## Suggested recording flow

### 1. Explain the idea

Say, in plain English:

> Studio 5000 remains the engineering environment. Git remembers the engineering versions. These custom tools make Commit, Compare, and Restore understandable without requiring an engineer to live in a terminal.

### 2. Commit

In Studio 5000:

1. make one obvious engineering change;
2. save the ACD;
3. choose **Tools → Commit**.

Show:

- project/repository/branch state;
- the result banner;
- CLI activity on the left;
- plain-English capture workflow on the right;
- local versus remote footer.

Enter a meaningful message and capture.

Point out that the result creates the next V number.

Optional quick proof:

- click Capture again without an engineering change;
- show **Already up to date** / **No new version was needed**.

### 3. Compare Version

Choose **Tools → Compare Version...**.

Show the version list and select the older version that visibly differs.

Explain:

> The historical side is rebuilt from Git as a temporary L5X. The current saved ACD is also converted to temporary L5X. Neither temporary L5X is stored as another Git copy.

Click **Compare in Logix Designer**.

When native Logix Compare opens:

- historical version is left;
- current saved project is right;
- drill into the ladder/routine difference.

This is the strongest visual proof that the workflow is controls-engineer friendly.

### 4. Restore Working Project

Return to Studio and choose **Tools → Restore Working Project...**.

Pick the known older version.

Show:

- horizontal restore-point picker;
- real CLI on the left;
- safe restore workflow on the right;
- footer explaining working project, backup, and unchanged Git history.

Start restore.

When the page reaches **Close current project**:

1. go to Studio 5000;
2. use **File → Close**;
3. leave Studio itself running.

Show the browser continue automatically:

- backup current ACD;
- replace the base-named working ACD;
- prepare reopen.

Then show Studio's Open Project dialog already pointed at the normal project filename.

Click **Open**.

Show that the visible engineering state is now the selected historical version.

Explain:

> The working ACD changed, but Git history did not go backward. The displaced ACD also has a timestamped safety backup.

## Closing summary

Use the three-sentence mental model:

> Commit means "remember this."  
> Compare means "what changed?"  
> Restore means "make my working project contain that captured version again."

## Things not to demo yet

For v0.1, avoid implying that these are already included:

- branch creation/switching;
- one-port VCS host;
- CI/CD/test-console pages inside this VCS UI;
- automatic final click of Studio 5000's Open dialog.
