---
name: modular-execution
description: >-
  Keeps coding, refactoring, and feature implementation in the main session by default.
  Uses workers only for bounded, independent tasks that can run in parallel while the main
  agent continues useful work, when the expected benefit outweighs delegation overhead.
---

# Modular Execution: Main Session First, Parallel Help When Useful

Keep the main agent responsible for the core task and anchored to the user's original problem. Do inspection, implementation, and diagnostics in the main session by default. Workers are optional parallel help: use them when independent work can meaningfully reduce completion time after accounting for startup, duplicated context, coordination, and integration costs.

## 1. Decide Whether to Delegate

- Keep decisions about scope, behavior, architecture, and trade-offs with the user in the main conversation. Record the original problem and the agreed outcome in a short task contract.
- Delegate only when the worker's task is bounded, independent of unresolved decisions and ongoing edits, and substantial enough to justify the overhead. Identify useful work the main agent can do concurrently before spawning the worker.
- Keep sequential, tightly coupled, and small tasks in the main session. If the main agent would simply wait for a worker, do the work directly. Do not delegate merely to keep tool output or implementation details out of the main context.
- Use the fewest workers that provide a clear parallel benefit. Launch them in the background using the available tool's supported arguments, then continue the main task. Do not poll for progress when completion notifications are available.
- Give each worker known paths, relevant constraints, and the smallest useful contract. Avoid a separate investigation just to prepare delegation or duplicating the worker's assigned work in the main session.
- Do not dispatch an independent validator or test-writing worker unless the user explicitly requests one.

For example, the main agent can implement an agreed change while a worker researches a separate compatibility question that does not block that implementation. Handing the entire implementation to a worker and waiting for it provides no parallel benefit.

## 2. Worker Boundaries

- Assign explicit ownership of files or symbols. Workers and the main agent must not make overlapping edits or concurrently mutate shared editor state. Keep work with shared dependencies sequential unless it can be split safely.
- Start from the named files or symbols. The worker may read or search directly related code and the relevant project/API documentation needed to implement the contract. It must not roam the repository or expand the task into unrelated cleanup.
- Edit only what the agreed outcome requires. Fix routine mistakes caused by the edit (such as a typo or a local compile error) without asking the user to decide them.
- If a newly discovered constraint changes the data structure, logic flow, feature pipeline, or other agreed behavior, stop before making that design change. Return the blocked approach, the relevant constraint, and one or two viable directions with trade-offs. The main session discusses the decision with the user.
- If the worker cannot locate the needed context, repeated attempts do not advance the task, or a tool appears stuck, stop and return partial progress and the blocker. Do not keep layering fixes onto symptoms or silently chase byproduct problems.
- Follow the project's coding and tool rules. Run compilation or tests only when requested or required by applicable project instructions. When checking an edit, distinguish errors introduced by the work from pre-existing errors; do not claim a check passed if it was not run.
- Prefer concise tool outputs and a short final report. Internal worker reads and diagnostics should not be copied into the main session unless necessary for a decision.

## 3. Dispatch Contract

Use an available subagent tool and its actual supported arguments. Follow applicable model and effort instructions; verify availability rather than guessing aliases. If a required model or variant is unavailable, report the blocker rather than silently substituting another.

Give the worker a prompt like this, adapted to the task:

```markdown
You are a bounded parallel worker. Keep the original problem as the reference point.

Original problem and agreed outcome: <brief contract>
Independent assignment: <specific task and expected deliverable>
Starting files/symbols: <known paths or signatures; omit unknown details>
Edit ownership: <files/symbols you may edit, or read-only>
Concurrent main-session work: <work happening alongside yours; avoid overlap>
Constraints: <only relevant project rules and user decisions>

Inspect directly relevant code and documentation as needed. Make only the agreed change.
Fix routine mistakes from your edit. Do not pursue unrelated issues or redesign the
solution. If a project/API constraint requires changing the agreed structure or behavior,
stop and report the constraint plus 1–2 directions and trade-offs. If progress stalls,
stop and report partial progress instead of repeatedly trying speculative fixes.
Run checks only when requested or required by project instructions.

Return a concise report, with no code dump:
- Status: completed | needs decision | blocked
- Files changed: <paths, or none>
- Outcome: <how this addresses the original problem; partial progress if blocked>
- Checks: <what ran and result, or not run>
- Decision/blocker: <only if needed: constraint, options/trade-offs, or why stalled>
```

## 4. Integrate Results

- The main agent remains responsible for integrating worker results and completing the original task. Review the relevant findings or changes without repeating the worker's entire investigation.
- Relate the worker's result back to the original problem, not merely its last edit or error.
- For `needs decision`, present the constraint and options to the user; do not send another worker to guess the design. Resume implementation only after the user decides.
- For `blocked`, make the blocker and partial progress clear. Do not automatically launch repeated workers without a new direction.
- For `completed`, give a brief outcome and accurate check status. Avoid importing the worker's full investigation into the main conversation.
