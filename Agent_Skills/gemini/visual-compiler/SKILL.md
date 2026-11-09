---
name: visual-compiler
description: >-
  Visualizes any algorithm, physics solver, rendering pass, or codebase feature as an interactive,
  step-by-step Pipeline Call-Chain and Micro-Scale Object/Memory Visualizer. Use when the user wants
  to learn, inspect, or understand how code/math transforms data and objects visually step-by-step
  ("visualize this", "visual compiler", "show me how the data/object transforms") without reading
  scattered files or writing boilerplate code.
---

# Visual Compiler Skill (Micro-Scale Pipeline & Object State Visualizer)

Turn static code, scattered files, and abstract math into a **live, interactive visual stepper** where the user can scrub through each stage of a data pipeline and watch **how the object looks, moves, or mutates in memory at micro-scale ($N \le 4$)**—plus toggle "Sabotage / Naive Modes" to visually witness why every guard and formula exists.

Grounded in the 4-Layer Mental Model (`State/Memory Layout` → `Transformation Pipeline` → `Signature Invariants` → `Fracture Points`).

---

## 1. Core Principles (Why & How This Works)

1. **Pipeline Call-Chain, Never File Hierarchy:**
   - Do not organize explanations by `.cs` files, classes, or OOP wrappers.
   - Flatten the entire execution path into a single chronological **Pipeline of Data Transformations** (`Pass 1` → `Pass 2` → `Pass 3` → `Pass 4`).
   - Collapse `for`-loop boilerplate; step by **meaningful state transitions**, not `i++` scalar noise.
2. **Micro-Scale Toy Instance ($N = 2 \text{ to } 4$):**
   - Human eyes cannot track 1,000 particles or 500 matrix transforms at once.
   - Always reduce the system to the **smallest non-trivial visual case** (e.g., 3 particles & 2 constraints; 1 ray & 2 AABBs; 4 instances in a GPU buffer; 1 projectile & 2 chain-reaction blocks).
3. **Ghost Delta ($\Delta$) Rendering (Before vs. After on Every Step):**
   - Never just draw the final state. When stepping into `Pass K`, draw the **previous step's state as a faded/dashed ghost** alongside the **new mutated state and the vector/arrow ($\Delta$) that caused it**.
4. **Synchronized 3-Pane Mental Model:**
   - **Pane A (The Spatial / Object Viewport):** 2D/3D Canvas showing where the object is, its transform, vectors, bounds, or visual state.
   - **Pane B (The Active Pipeline Stage & Code Slice):** The exact 3–8 lines of actual data-mutating code/math running in the current step (with live numbers substituted right next to the variables).
   - **Pane C (The Memory / Struct Diff Table):** Raw fields at rest (`pos`, `vel`, `invMass`, `flags`, byte stride/index), highlighting in color only the exact fields that mutated in this step.
5. **Ownership via Visual Sabotage ("Break It" Toggles):**
   - Passive watching creates an illusion of competence. To make the user *own* the algorithm without writing code, include **2–3 Sabotage / A-B Toggles** that let the user disable a guard or switch to a naive formula and **watch the object geometrically explode, tunnel, or desync**.

---

## 2. Deliverable Format (Interactive Visual Lab)

When invoked, produce a **self-contained, zero-dependency interactive visualizer** (plus a concise 4-Layer Mental Model summary):

- **If in an environment supporting live React Canvas (`.canvas.tsx`):** Render an interactive React Canvas app.
- **If in standard IDE / CLI / Browser environment:** Write a single, self-contained HTML5 + Canvas/SVG file (e.g. in `<project_or_scratch>/visual_labs/<topic>_visual_compiler.html`) and open or link it so the user can scrub through it immediately in any browser with zero build step.

### Required Interactive Controls in the Visual Lab

1. **Pipeline Stage Scrubber (`[ < Prev Step ]` / `[ Next Step > ]` + Timeline Bar):**
   - Steps through each transformation stage within a single frame/tick, plus a `[ Step Full Frame ]` or `[ Play / Pause ]` button to see multi-frame accumulation.
2. **Micro-State Preset Selector:**
   - 2–3 presets (e.g., `Normal Case`, `Boundary / Edge Case`, `Collision / High-Stress Case`).
3. **Sabotage / Naive Mode Toggles (The "Pain & Fracture" Switches):**
   - Interactive switches that mutate the active pipeline logic live:
     - *Example (Physics/XPBD):* `[x] Derive Velocity from (x - prevX)/dt` vs `[ ] Naive Euler Velocity (Watch Energy Explode)`
     - *Example (GPU/Culling):* `[x] Pad Struct to 16B Alignment` vs `[ ] Unaligned 12B Stride (Watch Buffer Drift)`
     - *Example (Gameplay/State):* `[x] Latch Win on <= 0` vs `[ ] Check == 0 with Simultaneous Hit (Watch Softlock)`
     - *Example (DeltaTime):* Slider for `dt` (`0.016s` normal vs `0.250s` lag spike).
4. **Live Variable Substitution:**
   - Inside the code box for the current step, show both the symbolic line and the live evaluated numbers for the selected object:
     ```text
     // Symbolic:
     Vector2 corr = grad * (-C / (wA + wB + alpha));
     // Live (Particle B):
     corr = (0.0, 1.0) * (-0.35 / (1.0 + 1.0 + 0.01)) = (0.0, -0.174)
     ```

---

## 3. Execution Workflow

### Step 1: Extract the 4-Layer Mental Model
Before generating the visual lab, map the target code or concept into 4 compact blocks (no filler stories):
1. **State (Memory Layout):** What structs/arrays exist and what each field physically represents.
2. **Transformation Pipeline:** The 3–6 discrete passes that mutate State A into State B.
3. **Signature Invariants (2–3 Checkpoints):** What must hold true after each pass (and how the visualizer draws it).
4. **Fracture Points (2–3 Sabotage Toggles):** Which exact guards or formulas will be wired to the "Break It" buttons.

### Step 2: Build the Interactive Visual Compiler Lab
Generate the self-contained HTML/Canvas (or `.canvas.tsx`) file implementing:
- High-contrast dark UI with clear geometric drawings (vectors with arrowheads, dashed ghost previous states, color-coded forces/deltas, coordinate grid).
- The synchronized Pipeline Stepper, Code Slice with live number substitution, Struct Diff Table (highlighting changed cells), and Sabotage Toggles.

### Step 3: Present & Guide the Eye
Provide the clickable link to the generated Visual Lab and give a **3-bullet "Eye Guide"**:
- What to watch on **Step 1 → Step N** in normal mode.
- Which **Sabotage Toggle** to flip first and what geometric/data failure to watch for in the viewport.
