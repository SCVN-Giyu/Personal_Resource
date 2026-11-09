---
name: grill-me
description: >-
  Grill the user relentlessly about a plan, decision, or idea to sharpen it before writing code.
  Use when the user wants to stress-test their thinking, aligns on architecture, or invokes 'grill me'.
---

# Grill Me Skill

Interview the user relentlessly until you reach a shared understanding. Map this as a **design tree**: every decision branches into the decisions that hang off it.

---

## Core Principles

1. **Design Tree Mapping:** Model the project or problem as a tree of decisions. Every decision branches into subsequent decisions that depend on it.
2. **Work the Frontier in Rounds:**
   - The **frontier** consists of every decision whose prerequisites are already settled (the questions you can ask *now* without guessing answers you haven't heard yet).
   - Ask the whole frontier in one round: number each question, explain options, and provide your recommended answer.
   - Wait for the user's answers before moving to the next round.
3. **Finding Facts vs. Decisions:**
   - Finding **facts** is your job, never the user's. When a frontier question needs a fact from the codebase or environment (files, dependencies, existing interfaces, APIs), inspect it yourself. Do NOT ask the user for anything you can look up in the project.
   - The **decisions** belong to the user: put each architectural choice, trade-off, or scope boundary to them and wait.
4. **No Assumptions:** The session is complete ONLY when the frontier is empty: every branch of the design tree is visited, and nothing is left silently assumed. Do not begin implementation until the user explicitly confirms alignment.

---

## Round Format

Format each round clearly with numbered questions and recommended defaults:

```markdown
❓ **Q1** - **<Question Title>**: <Question body, trade-offs, and choices>

➡️ **Recommendation:** <Your recommended answer and rationale>

---

❓ **Q2** - **<Question Title>**: <Question body, trade-offs, and choices>

➡️ **Recommendation:** <Your recommended answer and rationale>
```

---

## Procedure

1. **Initialize the Tree:**
   - Identify the user's core intent or proposal.
   - Inspect the local codebase/context to establish known facts.
   - Determine the initial frontier of open prerequisites.
2. **Conduct the Interview Rounds:**
   - Present the current frontier questions using the standard format.
   - Await the user's answers.
3. **Reshape & Advance the Frontier:**
   - As the user answers, mark prerequisites as settled and unlock downstream questions.
   - If an answer depends on another open question from this round, defer it to a subsequent round.
4. **Completion & Final Sign-Off:**
   - When all branches are resolved and the frontier is empty, present a clear, consolidated summary of the final design decisions.
   - Do not write code or execute changes until the user gives final confirmation.
