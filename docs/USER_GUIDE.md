# Logix Version Control — Beginner User Guide

This guide is for a Studio 5000 user who is new to Git and version control.

You do **not** need to know Git commands to use the normal workflow.

## The three things you do

There are three normal actions:

```text
COMMIT
Save a version of your engineering work.

COMPARE
See what changed between an older saved version and the project you are working on now.

RESTORE
Make your working ACD look like an older saved version again.
```

---

# 1. The basic idea

Studio 5000 remains where you edit the controller project.

Version control keeps recoverable engineering history outside the ACD.

Think of it like:

```text
Studio 5000 = where I work
Git         = where versions are remembered
```

The tools take care of translating the Studio 5000 project into files Git can track.

---

# 2. One rule to remember

## Save the ACD first.

The version-control tools work from the ACD saved on disk.

If you changed a rung but did not save the Studio 5000 project, that unsaved change will **not** be included in Commit, Compare, or Restore decisions.

A safe habit is:

```text
Change
→ Save
→ Version-control action
```

---

# 3. First-time setup

For a new project, you can begin with only your ACD.

Example:

```text
C:\Projects\MachineA\
    MachineA.ACD
```

Choose:

```text
Tools > Commit
```

The first time, the tool asks where the version-control repository should live.

The suggested layout is:

```text
C:\Projects\MachineA\
    MachineA.ACD
    MachineA_L5xGit.yml

    MachineA-vcs\
        .git\
        exploded-content\
```

Usually, accept the suggested path and choose **Connect project**.

The tool creates the supporting folders/configuration.

You do not need to manually create a Git repository or YAML file.

---

# 4. Commit / Capture Version

Use Commit when you want to save a recoverable engineering checkpoint.

## Normal workflow

1. Make your change in Studio 5000.
2. Save the ACD.
3. Choose **Tools > Commit**.
4. Enter a useful description.

Good:

```text
Add pump permissive for low tank level
```

Less useful:

```text
changes
```

5. Choose **Capture version**.

The tool walks through:

```text
Copy saved project
Export engineering source
Build version-control files
Compare with Git
Create new version
```

If something really changed, you will see a result such as:

```text
V4 created
Add pump permissive for low tank level
```

The result also shows the Git identifier and how many source files changed.

## You can leave the Commit page open

After the first capture:

1. Go back to Studio 5000.
2. Make another change.
3. Save.
4. Return to the same browser page.
5. Choose **Capture another version**.

You do not need to reopen Tools > Commit for every capture.

## If nothing changed

Sometimes you save the ACD but the actual engineering content is unchanged.

The tool may say:

```text
Already up to date

No engineering changes were found.
The saved ACD already matches the current Git version.
No new version was needed.
```

That is normal.

Nothing is wrong and Git history was not changed.

---

# 5. Local version versus remote repository

A captured version is created locally first.

Think of:

```text
My laptop              Shared Git server
LOCAL VERSION   --->   REMOTE REPOSITORY
```

If no remote repository is configured, the version still exists locally.

If a remote is configured, **Publish to remote** sends that exact captured version to the shared repository.

Commit and Publish are deliberately separate actions.

The page may show:

- **No remote configured**
- **Ready to publish**
- **Published**
- **Remote has newer versions**
- **Diverged**

If the remote contains newer work that must be reconciled, the tool should not offer a blind push.

---

# 6. Compare Version

Use Compare when you want to answer:

> What changed between an older captured version and the ACD I am working on now?

First:

1. Save the current ACD.
2. Choose **Tools > Compare Version...**

The page shows captured versions such as:

```text
V3   third commit
V2   second commit
V1   initial version
```

Choose the version you want to compare against.

Then choose:

```text
Compare in Logix Designer
```

The tool creates temporary comparison files and opens Rockwell's **Logix Designer Compare Tool**.

## Left and right

The comparison is intentionally:

```text
LEFT                         RIGHT
Historical Git version      Current saved project
```

This makes it easy to read a ladder change as:

> This is what the old captured version had, and this is what my current saved project has now.

## What gets saved?

Nothing extra is permanently added to Git just for Compare.

The historical L5X and current L5X are temporary files generated for that comparison session.

Your Git history continues to store the normal version-controlled engineering source.

---

# 7. Restore Version

Restore means:

> Make my working ACD contain the engineering state from an older captured version.

Restore does **not** delete newer Git history.

Example:

```text
Git has:
V1 → V2 → V3

You restore V1.

Working ACD:
V1 content

Git still has:
V1 → V2 → V3
```

If you later decide the restored state should become the new engineering direction and capture it again:

```text
V1 → V2 → V3 → V4
```

V4 may contain engineering logic similar to V1, but the history still shows what happened.

## Restore workflow

1. Save or otherwise account for any work you care about.
2. Choose **Tools > Restore Working Project...**
3. Select the version to restore.
4. Choose **Restore selected version**.
5. The tool rebuilds the historical ACD in a temporary location first.

At this point your current working ACD is still untouched.

6. When instructed, return to Studio 5000.
7. Choose:

```text
File > Close
```

Close the project only.

**Leave Studio 5000 running.**

8. The restore tool waits for the ACD file to unlock.
9. It creates a timestamped backup of the current ACD.
10. It replaces the working ACD with the selected historical version.
11. It prepares Studio 5000's Open Project dialog with the correct project folder and filename.
12. Click **Open**.

## Backup

Restore creates a backup similar to:

```text
MachineA.RESTORE-BACKUP-20260923-171500.acd
```

That backup is made before the working ACD is replaced.

---

# 8. What Commit, Compare, and Restore do to history

| Action | Working ACD | Git history |
|---|---|---|
| Commit | Reads saved ACD | Adds a new version only if engineering content changed |
| Compare | Does not replace ACD | Does not change history |
| Restore | Replaces working ACD after backup | Does not remove or rewrite versions |

The important principle:

> **Restore the working project, not history.**

---

# 9. What are ACD, L5X, exploded-content, and Git?

You do not need these terms for normal use, but this is what they mean.

### ACD

The normal Studio 5000 project file you open and edit.

### L5X

A structured Logix export format.

The tools can temporarily convert/rebuild L5X when needed.

### exploded-content

A whole-project L5X broken into smaller source files that Git can track more meaningfully.

This is the main engineering representation kept in Git.

### Git

The version-history system.

It records each captured source revision, its description, and the relationship between versions.

---

# 10. Why the project has a separate -vcs folder

You may see:

```text
MachineA.ACD
MachineA_L5xGit.yml
MachineA-vcs/
```

This is intentional.

The ACD working folder itself is not made into a Git repository.

The `-vcs` child folder contains Git and the exploded engineering source.

That keeps temporary files, binary ACD files, and restore backups from accidentally becoming normal version-controlled source.

---

# 11. Common questions

## I saved the project and it says no changes. Is something broken?

No.

Saving can update the ACD file even if the actual engineering content is unchanged.

If the tool says **Already up to date**, it checked the engineering source and correctly avoided creating an empty version.

## Does Compare change my project?

No.

Compare creates temporary files and opens Rockwell Logix Designer Compare Tool.

## Does Restore erase newer versions?

No.

Restore changes the working ACD. Git history stays intact.

## Why do I have to close the project during Restore?

Studio 5000 locks the open ACD.

Restore waits until you close that project so it can safely back up and replace the file.

## Do I close Studio 5000?

No.

Use **File > Close** to close the current project and leave Studio 5000 running.

## Why doesn't Restore automatically click Open for me?

The workflow intentionally leaves the final Open action to the engineer.

The tool can prepare the existing Studio 5000 Open Project dialog, but forcing the final open action was not reliable enough to make part of the safe workflow.

## Why not use WinMerge for ladder logic?

Text/file diff tools remain useful for Git/source troubleshooting.

For normal engineering comparison, Rockwell's Logix Designer Compare Tool is much easier to understand because it understands Logix project objects and ladder logic.

---

# 12. Recommended daily habits

1. Write meaningful commit descriptions.
2. Save the ACD before every version-control action.
3. Capture logical engineering checkpoints rather than every tiny mouse click.
4. Compare before restoring when you are unsure what changed.
5. Treat Restore as changing the working project, not erasing history.
6. Keep the restore backup until you have verified the restored project.
7. Publish to the shared remote deliberately when collaboration or backup requires it.

---

# 13. Simple mental model

For a new user, remember this:

```text
COMMIT
"I want to remember this."

COMPARE
"What changed?"

RESTORE
"I want my working project to look like that older version."
```

Everything underneath those three actions is automation.
