---
name: architecture-boundary
description: >-
  Analyze, classify, and structure software boundaries by lifecycle cohesion and data ownership.
  Use when designing new systems, refactoring existing code, untangling monolithic classes,
  or determining whether code belongs to generic utils, feature-specific mechanics, or game rules.
---

# Architecture Boundary Skill

Clarify boundaries, data ownership, and component lifecycles before writing code. Prevent accidental coupling and unnecessary abstraction overhead.

---

## The Core Question: Lifecycle Cohesion

> **"What comes and goes together?"**

If two pieces of state or logic:
- Are born at different times,
- Live for different durations, or
- Serve different concerns (engine execution vs. game rules),

**they do not belong in the same class.**

---

## The Three Tiers of Classification

When evaluating a class, method, or responsibility, classify it into one of three distinct tiers:

### 1. Generic Engine Utilities (`Utils`)
* **Definition:** Pure capabilities agnostic to any game, scene, or domain.
* **Examples:** `MathHelper`, `ObjectPool<T>`, `TransformExtensions`.
* **Characteristics:** Zero knowledge of game entities (no blocks, stages, health, or scores). Reusable across different projects.

### 2. Feature/Application Mechanics (`Game-Specific Mechanics`)
* **Definition:** The mechanical servants of a specific game feature.
* **Examples:** `StageInstanceLoader`, `RocketProjectileMotor`, `StageTransitionPresenter`.
* **Characteristics:**
  * Bound to the game's topology (e.g. scene hierarchy, specific mount transforms, local poses).
  * Performs mechanical execution (spawning, moving, tweening).
  * **Rule-Agnostic:** Does not know or enforce win/loss rules, score validation, or progression logic.
  * Dedicated to one feature, separating mechanical complexity from state coordination.

### 3. Gameplay Domain & Orchestration (`Game Rules & Coordination`)
* **Definition:** The authority on game rules, invariants, and phase state machines.
* **Examples:** `StageLifecycleCoordinator`, `BlockStageShadowTracker`, `GameController`.
* **Characteristics:**
  * Defines *meaning* (counts destructible blocks, verifies win/loss conditions).
  * Commands mechanics downward (commands loader to spawn, commands physics to settle).
  * Exposes read-only status and events upward (`IStageReadModel`).

---

## Architectural Principles to Apply

1. **Rule #1: Read-Only Component Communication**
   * External consumers only observe state through read-only interfaces or events.
   * No direct mutation of internal fields across components.

2. **Rule #2: Command Down, Event Up**
   * High-level coordinators issue commands down to mechanical components.
   * Child components report completion or changes upward via events/callbacks.
   * Avoid circular or peer-to-peer side effects.

3. **Pragmatic Boundary Check (Beware of Over-Abstraction):**
   * If a mechanical class is only 3–5 lines of standard engine calls that will never change or hold state, inline it unless:
     * It has its own independent lifecycle (e.g., pooling, caching).
     * It will soon grow with specialized loading/spawning logic.
     * Separating it significantly protects the coordinator from engine hierarchy churn.

---

## Boundary Evaluation Checklist

When evaluating or refactoring a feature, walk through these 4 checks:

1. [ ] **What is the true lifetime of each piece of data?**
   * Session-wide? Round-wide? Or transient (lasting only for a 0.2s transition)?
2. [ ] **Is this component enforcing a rule or performing a mechanic?**
   * Enforcing a rule $\rightarrow$ Domain Coordinator.
   * Performing a mechanic $\rightarrow$ Feature Component.
   * Universal math/data $\rightarrow$ Engine Util.
3. [ ] **Who is the single authority of state?**
   * Ensure only one component owns and mutates each core truth (e.g., target counts, active instance).
4. [ ] **Does the consumer need write access?**
   * If not, expose an interface with read-only properties and events.
