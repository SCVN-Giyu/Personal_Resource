# Playable & Unity Chaos Contract (Adversarial Stress Vectors)

> Use this contract alongside `unity.md`, `playable-luna.md`, and `architecture-boundary` when running Step 2 (`EXTRACT`) and Step 3 (`DOUBT`) of `playable-doubt-driven-development` on Unity / Playable Luna features.

---

## 1. Compiler & Bridge Surface Traps (Luna Export Parity)

The reviewer must assume the code will be transpiled by **Luna Bridge** to JavaScript/WebGL and check for:
1. **C# 7.3 Language Ceiling:** Any C# 8.0+ syntax (`switch` expressions, `is not null`, nullable reference annotations `T?`, range/index operators `..` / `^`, local static functions, target-typed `new()`) will break the Luna build even if Unity Editor compiles it.
2. **Unsupported .NET / Unity Overloads:** Any reflection, LINQ closure allocations in hot paths, `SendMessage` 3-arg overloads, `Collider.sharedMaterial`, or unlisted Unity APIs.
3. **Active Physics Backend Gap (e.g., Rapier JS):** Calling a `Rigidbody` or `Physics` method in C# without verifying that the active JS physics adapter (such as `rapier-adapter.js`) implements that exact method and `ForceMode`.
4. **Coroutine Iterator Unwinding:** Pass-through `IEnumerator` wrappers (`yield return ChildCoroutine()`) that rely on deep native C++ iterator stack unwinding, which Luna's JS `Coroutine.MoveNext` pops prematurely.

---

## 2. Runtime Chaos Vectors (Battle-Driven Pain Matrix)

Assume the player, browser, and mobile hardware are actively trying to break the playable. Disprove the artifact against these 5 chaos vectors:

### Vector A: Frame-Time & Delta-Time Chaos (`dt` Spikes)
- **Scenario:** Mobile WebGL experiences a sudden **300ms–500ms GC/shader lag spike** (`Time.deltaTime = 0.5f`) followed immediately by a **0.004ms high-refresh frame**.
- **Failure Modes to Hunt:**
  - Does movement, projectile stepping, or custom gravity overshoot targets or tunnel through colliders?
  - Do timers, lerps, or cooldowns divide by zero, overshoot `1.0f` without clamping, or skip intermediate phase triggers?
  - Does physics velocity explode (`NaN` / `Infinity`) under large `dt`?

### Vector B: Multi-Touch & Rapid Input Spamming ("Angry Tapper")
- **Scenario:** The user spams **20 taps per second** with **3 fingers simultaneously**, including on the exact frame a stage clears, a tutorial hand appears, a projectile fires, or the CTA overlay opens.
- **Failure Modes to Hunt:**
  - Can rapid taps fire duplicate projectiles, double-spend ammo/currency, or trigger stage transition twice?
  - Is input explicitly gated during phase transitions (e.g., holder resizing, camera panning, win/lose delay)?
  - What happens if `OnPointerDown` fires, the target object is destroyed or disabled mid-touch, and `OnPointerUp` never arrives (or arrives on a new stage)?

### Vector C: Mid-Lifecycle Destruction & Stage Transition Interruption
- **Scenario:** A stage finishes or resets **while** blocks are still sinking in water, TNT chain timers are ticking, projectiles are in flight, or coroutines/tweens are running.
- **Failure Modes to Hunt:**
  - Does `UnityEngine.Object` check use `?.` (which bypasses Unity's destroyed native check) instead of explicit `!= null`?
  - Does a delayed callback or coroutine mutate a block, rigidbody, or UI element from the *previous* stage after the new stage has loaded?
  - Are gameplay destruction counts decoupled from delayed visual disposal (e.g., sinking/breaking presentation)?

### Vector D: Domain Reload Disabled & Static Bleed
- **Scenario:** Unity Editor has **Enter Play Mode -> Reload Domain Disabled**, or the playable loops/restarts without a page refresh.
- **Failure Modes to Hunt:**
  - Do `static` fields, singleton instances, cached lists, or `static event` subscriptions survive across sessions, causing duplicate event firing or stale references to destroyed scene objects?

### Vector E: Boundary & Invariant Violations
- **Scenario:** Extreme or degenerate state inputs (`0` blocks in stage, all blocks destroyed on frame 0 by a single blast, negative remaining count, missing Inspector reference).
- **Failure Modes to Hunt:**
  - Can a target counter drop below `0` and fail an `== 0` win check? (Always check `<= 0` with a one-shot latch).
  - Does a mechanical servant class silently mutate game rules, or does a coordinator reach into raw child transforms violating `architecture-boundary` (Command Down, Event Up)?
