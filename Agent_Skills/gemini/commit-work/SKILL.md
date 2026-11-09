---
name: commit-work
description: >-
  Use this skill when asked to commit completed work, save progress to git, or create git commits.
  Prevents blind single-sweep commits, groups related changes into atomic logical commits,
  cross-checks staged files against session activity, writes Conventional Commit messages, and ensures changes are never pushed to remote.
---

# Commit Work Skill

A disciplined git workflow that creates **atomic, logical commits** by grouping directly related changes while strictly preventing blind single-sweep commits (`git add .`), verifying staged files against session history, using Conventional Commits, and keeping all operations local-only.

---

## 1. Core Principles & Guardrails

- **No Blind Sweep Staging (Strict Don't):** Never run sweeping staging commands like `git add .`, `git add -A`, or `git add -u`. Never commit the entire workspace in one giant sweep. Every file staged must be explicitly named by path.
- **Group Related Changes (Atomic Batches):** Files that share a direct logical relationship should be batched together into a single atomic commit. Examples:
  - An interface and its concrete implementation (e.g. `IHealth.cs` + `Health.cs`)
  - A component and its unit/integration test (e.g. `ScoreManager.cs` + `ScoreManagerTests.cs`)
  - A Unity asset and its accompanying `.meta` file (e.g. `Player.prefab` + `Player.prefab.meta`)
  - A schema change and its migration script
  - Directly coupled data models and their consumers modified in the same feature step
- **Separate Unrelated Changes:** Changes with distinct responsibilities (e.g. a refactor in audio vs. a bug fix in combat vs. a docs update) MUST be committed in separate, sequential commits.
- **Strict Session Scope (Strict Don't):** NEVER commit any files outside the work explicitly processed in this session. Existing uncommitted changes, untracked files, or workspace modifications from outside this session MUST NOT be touched.
- **Pre-Commit Double-Check:** Before issuing each `git commit`, run an explicit check on the staged index (`git diff --name-only --cached`) and verify that ONLY the intended files for that logical batch are staged.
- **Local Only:** Strictly **never** run `git push` or push changes to any remote repository.
- **Conventional Commits:** Every commit message must follow the Conventional Commits specification accurately describing that specific logical change.

---

## 2. Commit Message Convention

Format:
```
<type>(<scope>): <concise description in imperative mood>

[optional body explaining why and what changed across this batch]
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
- Subject line under 72 characters, lowercase type, imperative mood (e.g., `feat(combat): implement health component and tests`).
- No trailing period in the subject line.

---

## 3. Execution Workflow

### Step 1: Identify & Cluster Session Files
Review the exact set of files created, modified, or deleted **during this session**:
```bash
git status -s
```
1. Disregard any pre-existing or unrelated dirty files outside this session.
2. Cluster the session files into **logical batches** based on their dependency and purpose:
   - **Batch A (Atomic Pair/Cluster):** Closely coupled files that belong together (e.g., interface + implementation + `.meta` files).
   - **Batch B (Independent Change):** Files addressing a separate fix, config, or documentation.

---

### Step 2: Sequential Logical Batch Commit Loop
For **each logical batch**, execute the following steps:

#### 2a. Stage Explicitly Named Files
Stage only the files belonging to this specific logical batch by explicit path:
```bash
git add <path/to/file1> <path/to/file2>
```
*(Never use wildcards like `git add .` or directory sweeps).*

#### 2b. Mandatory Double-Check (Verification Step)
Verify the staging area before committing:
```bash
git diff --name-only --cached
```
**Strict Check:**
1. Does the output contain **only** the intended files for this logical batch?
2. Are all staged files **100% confirmed** to have been worked on in this session?

If ANY unintended or extra file is staged, unstage it immediately before proceeding:
```bash
git restore --staged <unintended-file>
```

#### 2c. Commit the Logical Batch
Formulate a Conventional Commit message describing the cohesive change made across this batch:
```bash
git commit -m "<type>(<scope>): <summary for this logical batch>"
```

---

### Step 3: Final Verification & Report
After all session batches have been committed:
1. Run `git status -s` to verify the working tree state.
2. Present a clean, structured summary to the user:
   - List of commits created (hash + commit message + files included).
   - Confirmation that related changes were batched atomically and sweep commits were prevented.
   - Reminder that all commits remain local (no push executed).
