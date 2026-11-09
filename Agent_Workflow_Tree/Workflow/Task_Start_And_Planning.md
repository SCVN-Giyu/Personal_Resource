# Phase 1: Task Start & Planning

[Back to Root Tree](../README.md)

## 1. Memory & Context Recall (Auto-Trigger: Every New Task)

- **MCP Tool:** `agentmemory` (`memory_recall` or `memory_smart_search`)
- **Action:** Query past decisions, architectural agreements, and recent fixes for the current project before asking the user to repeat context.
- **Feature Docs:** If the project tracks features in `Docs/` or `WorkLog`, check:
  - [worklog](../../Agent_Skills/gemini/worklog/SKILL.md) — when resuming or slicing a PM checklist into gradual steps.
  - [feature-context](../../Agent_Skills/gemini/feature-context/SKILL.md) — when restoring or updating living feature docs.

## 2. Step-by-Step Reasoning for Local / Non-Thinking Models

- **MCP Tool:** `sequential-thinking` (`sequentialthinking`)
- **Auto-Trigger:** When running on a local/non-thinking model OR designing a state machine, math solver, or multi-component interaction.
- **Action:** Externalize the state flow, edge cases, and component boundaries into discrete thought steps *before* writing code. Skip if the active model already performs native `<think>` reasoning and the task is straightforward.

## 3. Requirement Clarification & Architecture Boundaries

- **Ambiguous Scope or High Design Risk:**
  - Read [grill-me](../../Agent_Skills/gemini/grill-me/SKILL.md) (interactive questioning) or [spec-driven-development](../../Agent_Skills/gemini/spec-driven-development/SKILL.md) (when drafting a new spec/PRD).
- **Class / Component Structure Decision:**
  - Read [architecture-boundary](../../Agent_Skills/gemini/architecture-boundary/SKILL.md) to classify code into:
    1. `Generic Engine Utils`
    2. `Feature Mechanics` (rule-agnostic servants)
    3. `Domain Coordinators` (rule authority: Command Down, Event Up)
- **Delegation Check:**
  - Read [modular-execution](../../Agent_Skills/gemini/modular-execution/SKILL.md) — keep core coding in the main session by default; only spawn parallel workers for bounded, independent tasks.
