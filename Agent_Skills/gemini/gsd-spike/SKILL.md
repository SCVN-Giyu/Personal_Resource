---
name: gsd-spike
description: >-
  Spike technical ideas through isolated, experiential exploration.
  Builds focused, throwaway experiments in .planning/spikes/ to validate feasibility,
  prevent agent drift, and produce verified verdicts before committing to a full build.
---

# GSD: Spike (Experiential Feasibility Exploration)

A disciplined workflow to validate technical feasibility, prototype uncertain logic, and test platform constraints before committing to a full spec or writing production code.

---

## 1. Core Principles (Zero-Drift Sandboxing)

1. **Strict Directory Isolation (Sandbox):**
   - ALL spike code MUST live inside `.planning/spikes/<spike-name>/` (or a dedicated throwaway test folder like `Assets/_Spikes/` in Unity).
   - **HARD GUARDRAIL:** Never edit existing production scripts, components, or asset files during a spike.
2. **Anti-Productionizing Mandate:**
   - Spikes are **throwaway code designed solely to answer a question**.
   - Strictly forbidden: clean architectural layers, dependency injection, ScriptableObjects, unit tests, and premature refactoring.
   - Strictly encouraged: single file, public fields, hardcoded values, dirty minimal lines.
3. **Action Budget (Circuit Breaker):**
   - Timebox to max **3–5 tool calls/attempts**. If the concept cannot be proven or compiled within 3 attempts, halt immediately and report `REJECTED / INFEASIBLE` rather than chasing symptoms down a rabbit hole.
4. **Binary Verdict Deliverable:**
   - The primary deliverable is **Knowledge and a Verdict**, not a pull request.

---

## 2. Modes of Operation

- **Idea / Quick Mode (Default or `--quick`):**
  - Quickly validate a specific technical question (e.g. *"Can Luna transpile `Mesh.vertices`?"*, *"Can Rigidbody scale without jitter?"*).
  - Skips heavy decomposition and jumps straight into the isolated test script.
- **Decomposition Mode:**
  - Used when an idea has multiple technical unknowns.
  - Decomposes the idea into 1–3 independent feasibility questions, risk-ordered (highest-risk first).
- **Wrap-Up Mode (`--wrap-up`):**
  - Extracts the verified facts, constraints, and learnings from completed spikes into a project finding note.
  - Prompts to delete or archive the disposable spike scripts so they never dirty version control.

---

## 3. Step-by-Step Procedure

### Step 1: Define the Hypothesis
Formulate the exact question before touching any file:
```markdown
- **Hypothesis:** <e.g., Unity Luna can transpile runtime vertex animation without Bridge error>
- **Target Seam:** <e.g., Single Cube with MeshFilter in an isolated test scene>
- **Success Criteria:** <e.g., Compiles cleanly in Luna with 0 errors and vertex moves visibly>
```

### Step 2: Create the Isolated Sandbox
Ensure the spike folder exists:
```bash
mkdir -p .planning/spikes/<spike-name>/
```
*(In Unity, create an isolated script at `Assets/_Spikes/Spike_<Name>.cs` or `.planning/spikes/<spike-name>/`).*

### Step 3: Write the Minimal Dirty Experiment
- Keep it under 30–50 lines.
- Test only the specific API, equation, or mechanic in question.

### Step 4: Execute & Verify
- Run, build, or compile the experiment.
- Call console / compiler diagnostics once.
- If it fails on the first try, make at most **two** adjustments. If still failing, trigger the circuit breaker.

### Step 5: Deliver the Feasibility Verdict
Return a concise, structured verdict report:

```markdown
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 GSD ► SPIKE VERDICT: <SPIKE-NAME>
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

- **Status:** ✅ VALIDATED | ❌ REJECTED | ⚠️ BLOCKED
- **Question Answered:** <Exact hypothesis tested>
- **Key Finding:** <1-2 sentences on what was proven or what failed>
- **Discovered Constraints:**
  - ⚠️ <e.g., Cannot use NativeArray; must use classic array copying>
  - ⚠️ <e.g., C# 8+ pattern matching fails Luna compiler>

### Core Working Snippet (Reference only)
```<language>
<the 5-10 lines of verified working code to carry into the real build>
```

### Next Action
- [ ] Proceed to write Spec using verified findings.
- [ ] Delete or clean up `.planning/spikes/<spike-name>/`.
```

---

## 4. Spike Wrap-Up (`--wrap-up`)

When spiking is complete and you are ready to build the real feature:
1. Summarize all validated constraints into `.planning/spikes/SUMMARY.md` (or your project's `AGENTS.md` / `feature-context`).
2. Offer to remove the spike folder or commit it to a temporary throwaway branch.
3. Transition cleanly into `spec-driven-development` or `modular-execution` with 100% confidence.
