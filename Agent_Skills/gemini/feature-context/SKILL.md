---
name: feature-context
description: >-
  Use this skill to record, update, or restore feature context across chat sessions.
  Maintains versioned, living markdown documents in Docs/<feature_name>.md following Agile principles
  (soft rules, session-end pruning, keeping agreed items, eliminating discarded ideas, and logging WIP).
---

# Feature Context Skill

A lightweight, iterative workflow to maintain living feature documentation in `Docs/<feature_name>.md`. Designed for high productivity across multiple AI sessions by keeping context fresh, concise, and focused on current realities.

---

## 1. Core Principles (Agile Living Document)

- **Soft Rules Only:** Nothing in this document is a rigid or permanent specification. All decisions, structures, and behaviors are soft rules that can change as the feature evolves through Agile iteration.
- **Loop Record, Not Waterfall:** This document serves as a working memory ledger and handoff note, not an unchangeable PRD.
- **Session-End Curation & Pruning:**
  - **Keep Agreed:** Retain only verified, agreed-upon architecture, scripts, and data contracts.
  - **Eliminate Discarded:** Purge dead-end experiments and discarded ideas so future context is not polluted.
  - **Mark Under Consideration:** Explicitly flag active questions, ongoing debates, and WIP next steps.
- **Single Source of Truth per Feature:** Lives in `Docs/<feature_name>.md` within the project repository.

---

## 2. Document Template (`Docs/<feature_name>.md`)

Each feature document starts with a brief overview followed by chronological, versioned session blocks:

```markdown
# Feature: <Feature Name>

> Brief 1-2 sentence high-level concept of the feature.

---

## Version <N> - Session <N> (<YYYY-MM-DD>)

### 1. Scope & Objective
- What was the primary focus of this session?
- What problem was solved or what milestone was reached?

### 2. Concept & Mechanics (Agreed)
- Core gameplay/system behavior agreed upon.
- High-level rules and constraints (soft rules).

### 3. Scripts & Responsibilities (Agreed)
| Script / Component | Role & Responsibility | Key Data / Interfaces |
| :--- | :--- | :--- |
| `PlayerMovement.cs` | Handles input-driven locomotion | Reads `IInputProvider`, exposes `OnMoved` |
| `PlayerConfig.cs` | ScriptableObject holding tunables | Speeds, acceleration curves |

### 4. Currently Under Consideration (Open Questions / WIP)
- [ ] Active idea or trade-off being weighed.
- [ ] Potential refactor or unverified mechanic.
- [ ] Next immediate step for incoming session.

### 5. Pruned / Discarded (Omit or Brief Log)
- *Discarded approach X because of constraint Y (kept briefly so future sessions don't repeat dead ends).*
```

---

## 3. Workflow Procedures

### Procedure A: Session Start (Context Restoration)
1. When starting work on an existing feature, check for `Docs/<feature_name>.md`.
2. Read the latest `Version <N>` block to instantly understand:
   - Current agreed scope and concepts.
   - Script map and data responsibilities.
   - Active open questions and next steps (`Currently Under Consideration`).
3. Re-align with the user on today's target before diving into code.

---

### Procedure B: Session Wrap-Up (Recording & Pruning)
When wrapping up a session or when asked to update the feature context:
1. Increment the version number (`Version <N+1>`).
2. **Curate the Content:**
   - **Agreed:** Write down the code structure, script roles, and data flow actually settled during the session.
   - **Eliminate:** Remove stale or replaced assumptions from prior versions that no longer apply.
   - **Consideration:** Note open questions, partially completed tasks, or design dilemmas for the next session.
3. Save the document to `Docs/<feature_name>.md`.
4. Inform the user of the new version entry and highlight the open considerations for next time.
