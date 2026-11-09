# Phase 3: Adversarial Chaos Testing & Permanent Immunity

[Back to Root Tree](../README.md)

## 1. In-Flight Doubt & Chaos Gate (Auto-Trigger: Non-Trivial Decisions & Mechanics)

Before a non-trivial implementation, state machine, or bug fix stands, subject it to adversarial disproof (`CLAIM → EXTRACT → DOUBT → RECONCILE → STOP`):

- **If in a Unity / Playable Luna Project:**
  - Read [playable-doubt-driven-development](../../Agent_Skills/gemini/playable-doubt-driven-development/SKILL.md) and its leaf contract [playable-chaos-contract.md](../../Agent_Skills/gemini/playable-doubt-driven-development/references/playable-chaos-contract.md).
  - Attack the code across all 6 vectors:
    1. **Luna & Bridge Parity** (C# <= 7.3, active JS physics adapter coverage, flat coroutine iterators)
    2. **Frame-Time Spikes** (`dt = 0.5s` lag spike vs `0.004s` frame)
    3. **Angry Multi-Tap Spamming** (20 taps/sec across stage/UI transitions)
    4. **Mid-Lifecycle Destruction** (stage reloads while objects sink, explode, or tween; no `?.` on `UnityEngine.Object`)
    5. **Domain Reload Disabled Static Bleed** (`SubsystemRegistration` resets)
    6. **Counter & Boundary Invariants** (`<= 0` one-shot win latches, Command Down / Event Up)

- **If in a Personal / Non-Playable Project:**
  - Read [doubt-driven-development](../../Agent_Skills/gemini/doubt-driven-development/SKILL.md).
  - Isolate `ARTIFACT + CONTRACT` (without passing your `CLAIM` or justification) and run an adversarial review to expose unstated assumptions, race conditions, and edge cases.

## 2. Systematic Bug Triage & Pain-to-Immunity Loop (Auto-Trigger: Whenever a Bug, Pitfall, or User Correction Occurs)

- **Root-Cause Debugging:** Read [debugging-and-error-recovery](../../Agent_Skills/gemini/debugging-and-error-recovery/SKILL.md) when diagnosing failures or test/build errors so you trace the root cause instead of layering speculative fixes onto symptoms.
- **Skill Leaf:** [avoid-mistakes](../../Agent_Skills/gemini/avoid-mistakes/SKILL.md)
- **MCP Tool:** `agentmemory` (`memory_save`)
- **Action:**
  1. Log the anti-pattern (`❌ Avoid` vs `✅ Use Instead` + root cause) into the matching domain file under [avoid-mistakes/references/](../../Agent_Skills/gemini/avoid-mistakes/references/INDEX.md).
  2. Call `memory_save` with concise, reusable facts so future sessions and future `CONTRACT` extractions inherit the lesson automatically.

## 3. Atomic Git Commits & Session Handoff (When Requested)

- **Committing Work:** Read [commit-work](../../Agent_Skills/gemini/commit-work/SKILL.md) (group into logical atomic commits, Conventional Commits format, never push unless asked).
- **Session Handoff:** Read [handoff](../../Agent_Skills/gemini/handoff/SKILL.md) (produce a compact portable handoff markdown and output its full absolute path).
