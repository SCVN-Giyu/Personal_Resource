---
name: playable-doubt-driven-development
description: >-
  Subjects every non-trivial Playable Ad or Unity decision/code change to an adversarial review and chaos stress-test
  before it stands. Tailored specifically for Unity & Playable Luna production: cross-examines assumptions against
  C# 7.3 / Bridge compiler limits, physics adapter parity, frame-time spikes, rapid input spamming,
  mid-transition lifecycle destruction, and architecture boundaries.
---

# Playable Doubt-Driven Development (Unity & Luna Chaos Edition)

## Overview

A confident answer is not a correct one—especially in game and Playable Ad development where **code that compiles and passes happy-path tests in the Unity Editor routinely breaks in the exported Luna JS bundle or under chaotic player input.**

Long sessions accumulate context that quietly turns assumptions into "facts" without anyone noticing. Doubt-driven development is the discipline of materializing a cold, adversarial review — biased to **disprove and break**, not approve — before any non-trivial output stands.

This is not `/review`. `/review` is a post-hoc verdict on a finished PR. This is an **in-flight battle posture**: non-trivial decisions get cross-examined and chaos-tested while course-correction is still cheap.

---

## When to Use

A decision or implementation is **non-trivial** when at least one of these is true:

- It introduces or modifies branching gameplay logic, state machines, or phase transitions
- It crosses a component or architecture boundary (`Utils` vs `Feature Mechanics` vs `Domain Coordinator`)
- It touches async/timed lifecycles (coroutines, delayed chains like TNT, sinking/breaking disposal, stage loading)
- It calls Unity/Physics APIs that must survive **Luna Bridge transpilation** or custom JS adapters (e.g., `rapier-adapter.js`)
- It asserts a property the compiler cannot verify (input gating, frame-rate independence, static reset under Domain Reload Disabled, win/loss counter invariants)

**When NOT to use:**

- Mechanical operations (renaming, formatting, file moves)
- Following a clear, unambiguous user instruction with obvious correctness
- Reading or summarizing existing code
- One-line inspector tweaks or pure tooling operations
- The user has explicitly asked for speed over verification

If you doubt every keystroke, you ship nothing. Apply this skill specifically to non-trivial decisions and mechanics.

---

## Execution Posture & Compatibility with `modular-execution`

To respect your [`modular-execution`](../modular-execution/SKILL.md) rules and keep playable iteration fast:

1. **Adversarial Self-Doubt vs. Fresh-Context Subagent:**
   - **High-Stakes / Complex Mechanics:** Spawn an isolated, read-only `research` subagent (or parallel worker) with **only** `ARTIFACT + CONTRACT` (no `CLAIM`, no prior reasoning) so it attacks the code with zero confirmation bias.
   - **Tightly-Coupled / Fast Iteration:** When spawning a subagent adds unnecessary wait overhead for a bounded local check, execute a **Hard-Separated Adversarial Audit** directly in the main session by explicitly loading the Playable Chaos Contract and walking Steps 1–5 to disprove your own code before presenting it.
2. **No Mandatory Cross-Model Nagging:**
   - Unlike stock `doubt-driven-development`, **do NOT interrupt the user on every cycle** with a mandatory prompt asking to run external CLIs (`codex` / `gemini`).
   - Only run or offer external cross-model CLI checks when the user explicitly asks for a second opinion, or when 2 doubt cycles fail to resolve a high-risk architectural question.

---

## The Process

Copy and walk this checklist when applying the skill:

```markdown
Doubt & Chaos Cycle:
- [ ] Step 1: CLAIM — state the exact technical claim + why failure hurts
- [ ] Step 2: EXTRACT — isolate ARTIFACT + CONTRACT (auto-include Playable/Unity constraints)
- [ ] Step 3: DOUBT (CHAOS ATTACK) — attack the artifact across compiler, lifecycle, and input chaos vectors
- [ ] Step 4: RECONCILE — classify findings (Actionable / Contract Gap / Trade-off / Noise) & fix
- [ ] Step 5: STOP & LOG PAIN — meet stop condition; log any new pitfall via `avoid-mistakes`
```

### Step 1: CLAIM — Surface What Stands

Name the decision or implementation guarantee in 2–3 lines:

```text
CLAIM: "The stage transition coordinator safely unloads sinking water blocks and resets
        target counters before spawning the next stage."
WHY THIS MATTERS: Late disposal callbacks or uncancelled coroutines from Stage 1 can
                  decrement Stage 2's target count or reference destroyed Rigidbodies in Luna.
```

If you can't write the claim that compactly, you have a vibe, not a decision. Surface it before scrutinizing it.

---

### Step 2: EXTRACT — Smallest Reviewable Unit + The Playable Contract

Isolate the **ARTIFACT** (the diff, method, or 3–5 sentence architectural proposal) and pair it with the **CONTRACT**.

#### Auto-Injected Playable & Unity Contract
Whenever working in a Unity or Playable Luna project, your `CONTRACT` **automatically includes**:
1. **[Playable Chaos Contract](references/playable-chaos-contract.md)** (DeltaTime spikes, multi-tap spamming, mid-transition destruction, static bleed, invariant latches).
2. **[Playable Luna Avoid List](../avoid-mistakes/references/playable-luna.md)** (C# <= 7.3 syntax, supported Bridge APIs, active physics adapter parity, flat coroutine iterators).
3. **[Unity Avoid List](../avoid-mistakes/references/unity.md)** (No `?.` on `UnityEngine.Object`, no `GetComponent`/`Find` in hot loops, Domain Reload Disabled static resets).
4. **[Architecture Boundary](../architecture-boundary/SKILL.md)** (Lifecycle cohesion, Command Down / Event Up, read-only state exposure).

**Strip your reasoning when handing to a subagent.** If you hand over your justification, you'll get back validation of your justification. Pass **only** `ARTIFACT + CONTRACT`.

---

### Step 3: DOUBT — The Adversarial Chaos Attack

Frame the review strictly to **break** the artifact. Use this adversarial prompt (for a fresh-context subagent or hard-separated audit):

```text
Adversarial Chaos Review. Find how this artifact will break in Unity or an exported Luna Playable.
Assume the author is overconfident and tested only the happy path. Attack the artifact across:

1. LUNA & COMPILER PARITY:
   - Any C# 8.0+ syntax, LINQ closures, reflection, or unsupported Unity/Bridge overloads?
   - Does every Physics/Rigidbody call exist in the active JS adapter?
   - Any pass-through nested coroutine (`IEnumerator`) wrappers that Luna's MoveNext will pop early?

2. CHAOS VECTOR A — FRAME-TIME SPIKES:
   - What happens if `Time.deltaTime` spikes to 0.5s on mobile WebGL, or drops to 0.001s?

3. CHAOS VECTOR B — ANGRY MULTI-TAP SPAMMING:
   - What happens if the player spams 20 taps/sec with 3 fingers right as this logic starts, finishes, or transitions?

4. CHAOS VECTOR C — MID-LIFECYCLE DESTRUCTION & ORDERING:
   - What happens if the target GameObject or Stage is destroyed/reset while a timer, tween, or callback is still in flight?
   - Any `?.` operator used on a `UnityEngine.Object`?

5. CHAOS VECTOR D — DOMAIN RELOAD & STATIC BLEED:
   - Does any static state, singleton, or event subscription survive across Play sessions without `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`?

6. CHAOS VECTOR E — INVARIANTS & ARCHITECTURE BOUNDARIES:
   - What happens at 0 items, negative counters, or simultaneous hits on the same frame?
   - Does this violate Command Down / Event Up or mix game rules into mechanical servants?

Do NOT validate. Do NOT praise. Output ONLY concrete failure scenarios, or state explicitly that none were found after testing all 6 vectors.

ARTIFACT: <paste code diff or design proposal>
CONTRACT: <paste project constraints + relevant sections of playable-chaos-contract.md>
```

---

### Step 4: RECONCILE — Fold Findings Back

The adversarial output is data, not automatic verdict. Re-read the artifact against each finding and classify in this precedence order:

1. **Contract Misread:** Flagged because the contract passed to the reviewer omitted a known guarantee. Clarify the contract.
2. **Valid + Actionable (Real Pain Found):** A real bug, Luna incompatibility, race condition, or chaos failure mode. **Fix the code/design immediately** (e.g., clamp `dt`, latch the win counter with `<= 0`, replace `?.` with `!= null`, flatten the coroutine, gate input during transition).
3. **Valid Trade-Off:** The edge case is real, but in a 15–30 second playable ad the complexity cost of fixing it outweighs the risk. Document the trade-off explicitly in 1 line for the user.
4. **Noise:** False positive that is already guarded by a caller or scene invariant. Note briefly and move on.

---

### Step 5: STOP & Persist the Lesson

Stop the cycle when:
- All actionable chaos/compiler findings are fixed and re-verified, **or**
- 3 cycles have run (escalate any remaining architectural tension to the user), **or**
- The user says "ship it".

#### Turn Pain into Permanent Immunity (`avoid-mistakes`)
If the doubt cycle (or subsequent testing) uncovers a **new, reusable Unity or Luna pitfall** that wasn't already in [`unity.md`](../avoid-mistakes/references/unity.md) or [`playable-luna.md`](../avoid-mistakes/references/playable-luna.md), invoke [`avoid-mistakes`](../avoid-mistakes/SKILL.md) to log it into the registry so future contracts inherit it automatically.

---

## Optional: Generating In-Engine "Pain Harnesses" (When Code Review Isn't Enough)

When the user wants **actual runtime pain** inside Unity rather than just static code doubt, offer to generate a lightweight `[Button]` (Alchemy-backed) debug/chaos test method directly on the coordinator or a scratch test script:
- **`[Button] Chaos_SpamInput()`**: Fires 10 rapid simulated inputs across the same frame and consecutive frames.
- **`[Button] Chaos_InterruptTransition()`**: Forces a stage reload halfway through an active destruction/sinking sequence.
- **`[Button] Chaos_SimultaneousBlast()`**: Triggers simultaneous destruction on all remaining targets on the exact same frame to verify win-condition latching.

*(Always follow user rules: never enter Unity Play Mode or trigger builds automatically without explicit user authorization.)*

---

## Red Flags (Anti-Patterns in Doubt-Driven Development)

- **Rubber-Stamping Happy Paths:** Claiming code is "safe" just because it compiles in C# without checking Luna Bridge C# 7.3 / JS adapter parity.
- **Doubt Theater:** Running a doubt block where every finding is dismissed as "Noise" when real race conditions or unguarded callbacks exist.
- **Over-Engineering Playables:** Adding enterprise-grade frameworks to fix a 1-line edge case when a simple boolean latch (`if (_isTransitioning) return;`) solves the chaos vector cleanly.
- **Passing Your Reasoning to a Subagent:** Handing a reviewer your `CLAIM` and justification instead of raw `ARTIFACT + CONTRACT`.
