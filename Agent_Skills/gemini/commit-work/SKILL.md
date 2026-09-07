---
name: commit-work
description: >-
  Use this skill when asked to commit completed work, save progress to git, or create git commits.
  Enforces strictly file-by-file sequential commits, cross-checks staged files against session activity,
  writes Conventional Commit messages, and ensures changes are never pushed to remote.
---

# Commit Work Skill

A disciplined workflow that commits session work **file-by-file, one at a time**, with mandatory pre-commit verification against session history, standardized Conventional Commit messages, and strict local-only safety.

---

## 1. Core Principles & Guardrails

- **File-by-File Only (No Sweep Staging):** Never batch stage or sweep commit multiple files at once. Every file worked on in the session must be staged and committed individually, one by one. *(Exception: a Unity asset and its accompanying `.meta` file are committed together as a single atomic unit).*
- **Strict Session Scope (Strict Don't):** NEVER commit any files outside the work explicitly processed in this session. Existing uncommitted changes, untracked files, or workspace modifications from outside this session MUST NOT be touched.
- **Pre-Commit Double-Check:** Before issuing each `git commit`, run an explicit check on the staged index (`git diff --name-only --cached`) and verify that ONLY the target file from this session is staged.
- **Local Only:** Strictly **never** run `git push` or push changes to any remote repository.
- **Conventional Commits:** Every single commit message must follow the Conventional Commits specification tailored to that specific file's changes.

---

## 2. Commit Message Convention

Format:
```
<type>(<scope>): <concise description in imperative mood>

[optional body explaining why and what changed in this file]
```

### Types
- `feat`: A new feature or capability.
- `fix`: A bug fix.
- `refactor`: Code change that neither fixes a bug nor adds a feature.
- `perf`: Performance improvement.
- `style`: Formatting, whitespace, comments.
- `docs`: Documentation updates.
- `test`: Adding or correcting tests.
- `chore`: Maintenance tasks, config changes, meta files.

### Rules
- Subject line under 72 characters, lowercase type, imperative mood (e.g., `feat(combat): add knockback force calculation`).
- No trailing period in the subject line.

---

## 3. Execution Workflow (File-by-File)

### Step 1: Identify Session Files
Review the exact set of files created, modified, or deleted **during this session**:
```bash
git status -s
```
Compile an explicit list of only the files that were part of the session's work. Disregard any pre-existing or unrelated dirty files in the repository.

---

### Step 2: Sequential File-by-File Commit Loop
For **each file** identified from the session, execute the following steps sequentially:

#### 2a. Stage ONLY the Target File
```bash
git add <path/to/session-file>
```
*(If the file is a Unity asset with a newly created/modified `.meta` file, stage both together: `git add <path/to/asset> <path/to/asset.meta>`)*.

#### 2b. Mandatory Double-Check (Verification Step)
Verify the staging area before committing:
```bash
git diff --name-only --cached
```
**Strict Check:**
1. Does the output contain **only** the target file (and its `.meta` if Unity)?
2. Is this file **100% confirmed** to have been modified/created within this session?

If ANY unintended or extra file is staged, unstage it immediately before proceeding:
```bash
git restore --staged <unintended-file>
```

#### 2c. Commit the Single File
Formulate a Conventional Commit message describing the exact change in this specific file:
```bash
git commit -m "<type>(<scope>): <summary for this specific file>"
```

---

### Step 3: Final Verification & Report
After all session files have been committed one by one:
1. Run `git status -s` to verify the working tree state.
2. Present a clean, structured summary to the user:
   - List of commits created (hash + commit message).
   - Confirmation that all commits were made file-by-file.
   - Reminder that all commits remain local (no push executed).
