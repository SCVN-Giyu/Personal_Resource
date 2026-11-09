---
name: avoid-mistakes
description: >-
  Record, curate, and enforce modular "should-be-avoided" anti-pattern registries across frameworks and tech stacks.
  Use when the user corrects an error, says to avoid or never use a pattern again,
  wants to log a mistake into a curated list (e.g. Unity, Playable Luna, React, etc.),
  or when auditing code against known pitfalls.
---

# Avoid Mistakes Skill (Modular Anti-Pattern Registry)

A systematic, modular system to capture recurring mistakes, deprecated APIs, forbidden patterns, and developer constraints into **domain-specific registries** (e.g., Unity, Playable Luna, React, Python) located inside `references/`.

Designed for seamless, reliable shared knowledge across any AI assistant (**Gemini**, **Claude**, **GPT / Cursor**).

---

## 1. Core Architecture

### Modular Registries (Under `references/`)
Instead of one massive, monolithic file that pollutes context windows, anti-patterns are split by technology/framework:

```
avoid-mistakes/
├── SKILL.md                          # Main workflow instructions for agents
└── references/
    ├── INDEX.md                      # Catalog of all active registries & trigger tags
    ├── unity.md                      # Unity C# anti-patterns (hot loops, ?. operator, etc.)
    ├── playable-luna.md              # Luna Playable Ads constraints (C# 7.0 limit, Bridge APIs)
    └── <framework>.md                # Additional domains created on-demand
```

### Context-Aware Project Linking (Zero-Waste Context)
Rather than forcing agents to read all registries on every prompt:
1. Projects declare only their active tech stacks in their root instruction file (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, or `GEMINI.md`):
   ```markdown
   ## Active Constraints & Avoid Lists
   - [Unity Anti-Patterns](<PERSONAL_RESOURCES_ROOT>/Agent_Skills/gemini/avoid-mistakes/references/unity.md)
   - [Playable Luna Constraints](<PERSONAL_RESOURCES_ROOT>/Agent_Skills/gemini/avoid-mistakes/references/playable-luna.md)
   ```
2. Any AI agent entering the workspace reads the link, opens only the relevant avoid files, and skips everything else.

---

## 2. Standard Entry Specification

Every recorded entry must be actionable and structured:

```markdown
### <Category / Scope>: <Concise Title of the Anti-Pattern>

- ❌ **Avoid:** <Exact pattern, function, or syntax to NOT use>
- ✅ **Use Instead:** <Exact replacement, pattern, or method to use>
- 💡 **Why / Context:** <Root cause: error message, compiler error, performance penalty, deprecation>
- 🏷️ **Scope:** `<tags>` (e.g. `#unity #csharp #performance`, `#luna #csharp #compiler`)
- 📅 **Logged:** <YYYY-MM-DD>

```<language>
// ❌ Bad
<bad code snippet or command>

// ✅ Good
<good code snippet or command>
```
```

---

## 3. Recording Workflow (When a Mistake Occurs)

When a user says *"Don't do that"*, *"In Luna you can't use C# 8"*, or *"Add this to our avoid list"*:

### Step 1: Identify the Domain & Framework
Determine the target technology stack (e.g., Unity, Playable Luna, React, TypeScript, Python, Git).

### Step 2: Locate or Create the Registry File
Check `references/INDEX.md` and `references/`:
1. **If `references/<domain>.md` exists:** Open it and check for existing entries on the topic.
   - If an entry already exists, refine or extend it with the new nuance.
   - If it's a new anti-pattern, append it under the appropriate section.
2. **If `references/<domain>.md` does NOT exist:**
   - Create `references/<domain>.md` with a clean markdown header.
   - Add the new domain entry to `references/INDEX.md`.

### Step 3: Check Project Context Linking
If working inside a project repository:
1. Check if the project's instruction file (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, or `GEMINI.md`) already links to this `references/<domain>.md`.
2. If not, add or recommend adding the file link so all future sessions across any AI agent automatically inherit this rule.

### Step 4: Confirm to User
Present a concise summary card of the recorded entry (`❌ Avoid` vs `✅ Use Instead`) and the exact file path updated.

---

## 4. Audit Workflow (Pre-Flight & Post-Edit Verification with `roslyn-mcp`)

Before writing code or after performing edits/refactors in a specific stack:
1. Check the project's linked avoid files or `references/INDEX.md`.
2. Load only the relevant domain files for the active project (e.g. `unity.md` for standalone Unity; `unity.md` + `playable-luna.md` for Playable Luna; skip both in non-Unity projects).
3. **For C# / Unity Projects (`roslyn-mcp` + `serena` Gate):**
   - Use `roslyn-mcp` (`~/.dotnet/tools/roslyn-mcp`) for solution-wide C# compiler diagnostics, type checking, interface extraction/implementation, and complexity/dead-code checks.
   - Then audit the modified code against all `❌ Avoid` rules in the active domain markdown files (catching valid-C# runtime traps like `?.` on `UnityEngine.Object`, `GetComponent` in hot loops, or Luna JS adapter gaps).
4. If a conflict or compiler diagnostic is found, self-correct before presenting the solution to the user.
