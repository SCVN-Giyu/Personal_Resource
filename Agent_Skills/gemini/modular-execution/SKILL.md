---
name: modular-execution
description: >-
  Use this skill when executing coding, refactoring, or feature implementation tasks on a module.
  Orchestrates modular task breakdown, dispatches isolated subagent workers to write and self-verify code,
  enforces project architecture and C# 7.0 guardrails, and protects the main conversation context.
---

# Modular Execution Skill (Implementer → Validator Pipeline)

A structured orchestrator-worker workflow designed to execute Unity coding tasks through isolated subagents. Adopts the **Maker–Checker pattern** (Implementer $\to$ Validator) to prevent context pollution, remove confirmation bias, and ensure high architectural and numerical accuracy.

---

## 1. Workflow Architecture

```
[ User + Main Agent (Orchestrator) ]
  └─ Define Module Scope, Contracts & Invariants (Main Chat)
       │
       ├─ Phase 1: Implementer Agent (Type: "self")
       │    ├─ Implements C# 7.0 code & adheres to project rules
       │    ├─ Self-verifies compilation via Unity MCP (read_console)
       │    └─ Reports clean code summary back to Orchestrator
       │
       ├─ Phase 2: Validator Agent (Type: "self")
       │    ├─ Audits code independently against contracts & edge cases
       │    ├─ Verifies invariants (bounds, no NaNs/Infinities, null safety)
       │    ├─ Writes & runs automated tests (EditMode/PlayMode) if requested
       │    └─ Reports pass/fail audit back to Orchestrator
       │
       ▼
[ Main Agent ] ──> Deliver Clean, Audited Summary & Next Decisions to User
```

---

## 2. Phase-by-Phase Procedure

### Phase 1: Planning & Contract Definition (Main Chat)
Before invoking any subagent:
1. **Clarify Scope & Interfaces**: Agree on data structures, interfaces, and expected behaviors with the user.
2. **Define Invariants**: Note boundary conditions, physical limits, or numerical constraints (e.g., clamped velocities, normalized directions, zero-allocation requirements).
3. **Partitioning**: For small/cohesive modules, dispatch 1 Implementer. For large decoupled modules, dispatch parallel Implementers along agreed interface boundaries.

---

### Phase 2: Implementer Worker (Maker)
Dispatch an isolated subagent (`TypeName: "self"`) with the role `Module Implementer`.

**Implementer Prompt Requirements**:
- **Task & Files**: Exact scope of files to create or modify.
- **C# 7.0 Target**: No modern syntax (switch expressions, records, nullable reference types, target-typed `new`). Explicit types and `default(T)` only.
- **Architecture Rules**:
  - *Rule #1*: Read-Only Component Communication via interfaces.
  - *Rule #2*: Command Down, Event Up.
  - *Rule #3 & #4*: Simplicity first, pragmatic performance (no allocations/`GetComponent` in `Update()`).
  - *Luna Tooling*: Reference `Assets/Supercent/Luna/TOOL_REFERENCE.md` when applicable.
- **Self-Correction Requirement**:
  - After writing files, call `read_console(types=["error"])` via Unity MCP.
  - Fix any syntax or compilation errors autonomously until 0 errors remain.
- **Output Format**: Concise summary of modified/created files and compilation status.

---

### Phase 3: Validator Worker (Checker)
Once the Implementer finishes cleanly, dispatch a separate isolated subagent (`TypeName: "self"`) with the role `Module Validator`.

**Validator Prompt Requirements**:
- **Objective**: Adversarial audit and verification of the code written by the Implementer.
- **Audit Checklist**:
  1. *Contract & Architecture Compliance*: Are all properties read-only? Are events used for upward communication?
  2. *Defensive & Edge-Case Handling*: Null checks, division by zero, empty collections, finite numbers (no `float.IsNaN` / `float.IsInfinity`).
  3. *Performance Guardrails*: No GC allocations or component lookups in hot paths.
- **Automated Testing (if requested by user)**:
  - Create test scripts (EditMode / PlayMode) testing contracts and invariant rules using tolerances (`.Within(epsilon)`).
  - Run tests via Unity MCP (`run_tests` $\to$ `get_test_job`) and ensure 100% pass.
- **Output Format**: Structured report detailing audit findings, edge-case checks, and test results.

---

### Phase 4: Reporting & User Alignment (Main Chat)
1. Review the Validator’s findings.
2. If flaws were found, re-task the Implementer to address the specific items before final sign-off.
3. Present a concise status summary to the user highlighting what was created and validated, and await the user's next direction.
