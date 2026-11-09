---
name: handoff
description: >-
  Compact the current conversation into a portable handoff document for another agent or fresh session to pick up.
  Use when transferring context across sessions, harnesses (Claude, Cursor, Codex, Gemini), or branching a side task.
argument-hint: "What will the next session be used for?"
---

# Handoff Skill

Write a focused, portable handoff document summarizing the current conversation so a fresh agent or different harness can continue the work without re-explaining context or losing in-flight state.

---

## When to Use

Invoke this via `/handoff` (or when asking to hand off / prepare a handoff document):
- **Swapping harness:** Moving between agents (e.g. Gemini $\leftrightarrow$ Claude $\leftrightarrow$ Cursor $\leftrightarrow$ Codex) where the new harness cannot see the previous session's memory.
- **Session boundary / Fresh start:** The current conversation is getting long, context is degrading, and you want to start a clean session focused on the next step.
- **Branching / Forking a side task:** You stay in the current design session while dispatching a second agent to prototype or investigate a sub-problem in parallel.
- **Handoff to a colleague:** Creating an actionable briefing for someone else picking up the task.

*(Note: If you are staying in the exact same session on the same task without changing context, normal compacting/continuation is preferred. Use `/handoff` whenever context needs to **travel**).*

---

## Core Rules

1. **Save to OS Temp Directory:**
   - Save the file to the OS temporary directory (e.g., `/tmp/handoff-<topic>-<timestamp>.md` on macOS/Linux), **never inside the project repository workspace**.
   - Output the exact absolute path prominently so the user can hand it directly to the next agent.
2. **Tailor to the Next Session:**
   - If the user provided an argument or note (e.g., `/handoff "Implement the health bar UI"`), optimize the document specifically for that goal.
3. **Reference, Don't Duplicate:**
   - Do NOT copy code blocks, diffs, plans, ADRs, or specs that already exist on disk.
   - Reference them by exact path (e.g., `Docs/combat-system.md`, `git diff HEAD~1`).
4. **Distinguish Facts from Assumptions:**
   - Clearly label what has been **verified** (tests ran, compilation passed, files created) vs. what is **assumed or pending**. The incoming agent treats handoffs as authoritative contracts; unverified beliefs become false premises.
5. **Redact Sensitive Information:**
   - Never include API tokens, passwords, keys, or personal credentials.
6. **Include "Suggested Skills":**
   - Explicitly list which skills or slash commands the incoming agent should reach for (e.g., `modular-execution`, `commit-work`, `avoid-mistakes`, `grill-me`).

---

## Handoff Document Template

When writing the file to `/tmp/handoff-<topic>-<YYYYMMDD-HHMMSS>.md`, use this structure:

```markdown
# Handoff: <Topic / Feature Name>
- **Created:** <YYYY-MM-DD HH:MM:SS>
- **Target Goal for Next Session:** <What the incoming agent should achieve>

---

## 1. Current State & In-Flight Work
- **Completed:** <Brief list of settled work and verified facts>
- **In-Flight / Half-Done:** <Exact point where work stopped, active blockers, or WIP changes>
- **Key Decisions Made:** <Architectural choices settled in this session>

## 2. Relevant Artifacts & Files (Paths Only)
- Working directory: `<repo_root>`
- Modified files / branch: `<branch_name>` or `<list of paths>`
- Relevant docs / specs: `[Path to docs or issues]`

## 3. Immediate Next Steps (For the Incoming Agent)
1. <First action item>
2. <Second action item>

## 4. Suggested Skills & Tools
- `<skill-name>`: <Why the next agent should reach for this skill>

## 5. Gotchas & Assumptions
- ⚠️ <Any edge case, constraint, or unverified assumption to double-check>
```

---

## Execution Workflow

1. **Gather State & Arguments:**
   - Identify the user's intended focus for the next session.
   - Run `git status -s` or inspect active changes to get accurate file paths and git branch state.
2. **Draft & Save to Temp:**
   - Formulate the markdown document following the template above.
   - Write to `/tmp/handoff-<topic>-<timestamp>.md`.
3. **Report Back to User:**
   - Confirm the file has been written.
   - Provide the exact path and a 1-line command/instruction on how to prompt the next agent (e.g., *"In your next session, say: `Read /tmp/handoff-...md and continue the task.`"*).
