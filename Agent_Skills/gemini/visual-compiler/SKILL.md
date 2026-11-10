---
name: visual-compiler
description: >-
  Visualizes any algorithm, physics solver, rendering pass, or codebase feature as an interactive
  visual HTML/Canvas app. Supports two modes: (1) Guided Lesson Mode ("teach me X") — builds
  intuition one chapter at a time with 1-dot/1-stick micro-scenes, an always-visible loop strip,
  Happy Path → Predict → Break → Explain flow, and misconception quizzes; and (2) Lab Mode
  ("inspect/debug this code") — a pipeline stepper with ghost deltas, struct-diff tables, and
  verified sabotage toggles.
---

# Visual Compiler Skill (Guided Lesson & Pipeline Lab)

Turn abstract algorithms, physics solvers, rendering passes, and codebase mechanics into **self-contained, interactive visual HTML/Canvas pages** that build deep engineering intuition—without reading walls of text or writing boilerplate code.

---

## Step 0: The Mode Gate (Ask or Route First)

Before building anything, distinguish **learning a concept** from **auditing existing code**:

1. **Guided Lesson Mode (Default for `"teach / learn / explain / I don't get X"` or when no code is provided):**
   - A learner who doesn't yet have a mental model will be overwhelmed by a 3-pane dashboard full of struct tables and 4 simultaneous sabotage toggles.
   - Build a **4–7 Chapter Guided Lesson** that reveals **one idea at a time**, starts with the smallest possible case ($N = 1\text{ or }2$), and teaches via **Happy Path → Predict → Break → Explain**.
   - **Reference Template:** Always inspect [`examples/xpbd_cloth_guided.html`](examples/xpbd_cloth_guided.html) as the gold-standard layout and interaction model.

2. **Pipeline Lab Mode (For `"inspect / debug / visualize this code"` when a file/snippet is given, or as the final chapter of a Lesson):**
   - For a user who already knows the concept and wants to trace data transformations, struct mutations, and edge-case fractures across a pipeline.

*If the user's intent is ambiguous, default to **Guided Lesson Mode** (and optionally include a sandbox/lab in the final chapter).*

### Specimen Rule: Never Teach the Code's Behavior as Truth
- When teaching from an existing codebase, the code is a **specimen to be judged**, not the reference. Its bugs must not become lesson content.
- Happy-path chapters must come from a cited reference (paper, talk, official docs). Mark code-only observations as `📄 Observed in code (file:line)` and never present them as "how it works".
- Break chapters must come from **reproduced** failures, framed as: *"This code does X. The reference says Y. Here is the reproduced failure."*
- If a `study-and-harden` Concept Dossier exists, use **it** as the lesson input instead of the raw code, and carry its provenance labels (📚 / 🔬 / 📄 / ❓) into the page.

---

## Mode A: Guided Lesson Mode (How Understanding Builds Up)

### 1. Reveal One Idea at a Time (4–7 Chapters)
- Organize by **how human understanding builds up**, not by raw file structure.
- **Strict Screen Budget per Chapter:**
  - **1 always-visible Loop Strip** at the top (e.g. `① Predict → ② Fix the sticks → ③ Velocity ↻`) highlighting the step the current chapter teaches so the learner always sees how the pieces fit.
  - **≤ 4 sentences of plain-English text** at the top. Define every new symbol (`C`, `w`, `α`, `λ`, Gauss–Seidel) the first time it appears in plain English. If a symbol isn't needed in Chapter $K$, leave it out!
  - **1 focused 2D/3D Canvas scene** with **≤ 3 controls** (buttons/sliders) and direct pointer dragging where helpful.
  - **1 live formula box** (`formula` + live substituted numbers in green/red).
- **Start Smaller Than $N = 2\text{–}4$:**
  - Chapter 1–5: Use the **smallest case that carries meaning** (e.g., 1 pinned dot + 1 free dot + 1 stick).
  - Final Chapter: Scale up to the full system (e.g., a 9×7 interactive cloth or full buffer) showing that the exact same 3-step loop runs across all items.

### 2. Order Every Chapter: Happy Path → Predict → Break → Explain
Breaking things is the strongest way to learn *why* each line of code exists—**but a failure is just noise until the learner knows what "correct" looks like.**
Walk this exact 4-step sequence inside each chapter:
1. **Happy Path First:** Let the learner click/drag the working step and see the ghost delta ($\Delta$) and target invariant (e.g., the dashed circle where $C = 0$).
2. **Predict Before Breaking:** Ask the learner what will happen if a specific line, guard, or term is removed/changed.
3. **Break It on Purpose (Sabotage Toggle):** Give a clear toggle button right on the chapter controls (e.g., `Correct v` vs `Naive v (skip step ③)`, or `/ h² ON (XPBD)` vs `/ h² OFF (old PBD)`, or `Pin w = 0` vs `Bug: Pin w = 1`, or `Guard |d| < 1e-9` off).
4. **Explain the Mechanism:** Show on the HUD and formula box *why* the break failed geometrically or numerically.

Choose breaks from these three high-value categories:
- **Plausible Misconceptions:** e.g., *"The velocity from Predict is already good enough; why recompute `v = (x - prev) / h`?"*
- **Real Production Bugs:** e.g., `NaN` from zero-length vector division (`|d| == 0`), a pinned particle with `w != 0`, or a `dt` lag spike.
- **Tuning Traps:** e.g., stiffness/compliance that changes when frame rate (`h = 1/30` vs `1/240`) or iteration count changes.

### 3. Mandatory Comprehension & Misconception Quizzes (2–3 per Chapter)
- Place a `🧠 Check yourself (guess before you click)` card at the bottom of every chapter with **2–3 multiple-choice questions**:
  - At least **1 prediction question** asked *before* clicking the action/break button.
  - At least **1 transfer question** (e.g., *"Your cloth looks too stretchy in a game—what do you change?"*).
- **Treat Wrong Answers as Learning Material:**
  - Every wrong option must represent a **tempting misconception**.
  - Clicking an option must immediately reveal an explanation of **why the wrong idea is tempting AND why it fails**.

---

## Mode B: Pipeline Lab Mode (For Code Inspection & Deep Debugging)

When the user already understands the concept or asks to inspect a specific code snippet/system:
1. **Pipeline Call-Chain Scrubber:** Step through discrete passes (`Pass 1 → Pass 2 → Pass 3`) with ghost delta ($\Delta$) rendering and live variable substitution.
2. **Tidy Layout (Collapse Secondary Panels by Default):**
   - Keep the Spatial Viewport and Active Code Slice primary.
   - Collapse the **4-Layer Mental Model reference** and **Struct-Diff Table** by default, or only display struct rows that actually mutated on the current step.
3. **Verified Sabotage Toggles & Presets:** Wire the system's real fracture points into interactive toggles.

---

## Hard Requirement: Headless Verification Before Presenting

**Never invent or guess what a bug/break looks like.** (For example, in PBD, skipping the velocity update does *not* make the pendulum bounce higher—the stick still clamps the position to the circle while `|v|` silently accumulates gravity forever (`~98 m/s` after 10s) until the solver can't keep up; and a coincident-particle `NaN` preset won't trigger `NaN` if an earlier constraint separates the particles first!).

Before presenting the HTML file to the user, you **MUST**:
1. **Keep Simulation Logic Pure & Free of DOM Calls:** Write the step/solver functions so they can be executed headlessly in Node.js (`node -e ...`) or Python.
2. **Run Every Chapter, Preset, and Sabotage Toggle Headlessly:**
   - Verify that the numbers, HUD readouts, and failure modes described in the text **actually happen** when clicked (e.g., confirm that XPBD stretch stays constant across `h = 1/30`, `1/60`, `1/240` with `/h² ON` and changes with `/h² OFF`; confirm that a `NaN` sabotage toggle actually produces `NaN`).
3. **Declare Visual Exaggerations on Screen:**
   - Whenever a time step (`h = 0.15s`), force vector, or compliance value is exaggerated for visual clarity, state that explicitly on screen alongside the true scale.

---

## Deliverable & Housekeeping Rules

1. **Persistent Output Folder (Never Lose Lessons in `/tmp`):**
   - If a project repository is open and relevant, save to `<PROJECT_ROOT>/Docs/Visual_Labs/<topic>.html`.
   - If no project is open (or for general learning), save to your persistent learning vault:
     `/Users/giyu/Documents/Personal_resources/_Obsidian/_Learning_Resource/Visual_Labs/<topic>.html`
     *(or relative to the repo root: `<PERSONAL_RESOURCES_ROOT>/_Obsidian/_Learning_Resource/Visual_Labs/<topic>.html`)*.
2. **Keep the Guidance Inside the Page (Zero Chat Walls of Text):**
   - Put all explanations, formulas, quizzes, and eye-guides **inside the interactive HTML page itself**.
   - Your chat response must be **concise**:
     1. The clickable link to the `.html` file (and open it in the browser if authorized/appropriate).
     2. How to start (1–2 sentences).
     3. A brief bullet list of what simulation claims & sabotage breaks were headlessly verified.
