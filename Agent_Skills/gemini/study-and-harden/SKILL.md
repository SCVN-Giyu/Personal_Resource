---
name: study-and-harden
description: >-
  Orchestrates learning a subject AND making its code production-ready in one consistent loop,
  without learning the code's mistakes as truth. Treats existing code as a suspect (specimen),
  never as the teacher: concepts come from cited references, failures come from reproduced
  evidence. Produces a Concept Dossier per concept and a two-axis Q&A Log (answer confidence ×
  learner understanding). Use when the user wants to learn a system/algorithm/codebase feature
  and also harden, fix, or extend it; when they say "study this", "learn and fix", "make this
  production ready and teach me", "harden", or ask to log questions/what they found hard.
---

# Study & Harden

One loop for two goals: **understand the subject** (happy side + broken side) and **work on it**
(fix, harden, extend). Every claim must show where its truth came from.

This skill is a **conductor**: it fixes the phase order, the labels, the files, and the gates.
Follow it literally. When unsure, choose the more cautious option and write `unknown`.

---

## 0. The 10 Invariants (never break these)

1. **Code is a suspect, not a teacher.** Never say "this is how it works" based only on code.
2. **Every claim gets exactly one label:** `reference`, `verified`, `code`, or `inferred` (see §1).
3. **Reference before code.** Write the Concept Card (Phase 1) before opening the source files of that concept.
4. **No bug without a repro.** A suspected problem stays `inferred` until reproduced. Say "suspected", not "bug".
5. **No fix without before/after.** A fix is done only when the same repro fails before and passes after.
6. **Never write "the author intended".** Describe what the code does and compare it to the reference.
7. **Never mark the user as understanding.** Only the user, or a passed quiz, changes understanding state.
8. **Never delete an answer.** Wrong answers are struck through in History with date and reason.
9. **One phase, one concept at a time.** Finish the phase gate (§3) before moving on.
10. **If you cannot do a step, say so and record it** as `blocked` with the exact steps the user must do. Never pretend.

If two rules seem to conflict, the lower number wins.

---

## 1. Labels — How to Tag Every Claim

Use the **word** (machine-readable); the emoji is optional decoration.

| Label | Use when… | Must include |
|---|---|---|
| `reference` 📚 | A source says it | Source title + link/section |
| `verified` 🔬 | You ran / measured / reproduced it | Command or steps + the result numbers, saved in `evidence/` |
| `code` 📄 | You read it in the code | `file:line`. Describe only, no judgment |
| `inferred` ❓ | Anything else (your reasoning, memory, guess) | Nothing — but it can't be taught as fact |

**Decision procedure (apply in order, stop at first YES):**
1. Did I reproduce or measure it this session, with saved evidence? → `verified`
2. Can I cite a specific source I actually opened? → `reference`
3. Is it a plain description of what a code line does? → `code`
4. Otherwise → `inferred`

**Promotion rule:** `code` and `inferred` can be promoted only to `reference` (find a source) or `verified` (reproduce it). Nothing is promoted by argument.

---

## 2. Setup (once per subject)

1. Fill these four values. Ask the user for any you can't infer; do not guess the target.
   - **Subject:** e.g. "InfiniteGrass GPU grass"
   - **Target:** platform + budget, e.g. "PC + Android mid-range, ≤ 2 ms GPU, ≤ 64 MB VRAM"
   - **Goal:** `learn` | `learn+fix` | `learn+extend`
   - **Speed:** `light` (1 concept, adversarial review optional) | `full` (subsystem, review mandatory)
2. Create the study folder **with the script, never by hand**:
   ```bash
   # canonical skill folder (agent configs like ~/.claude/skills symlink here):
   S="$HOME/Documents/Personal_resources/Agent_Skills/gemini/study-and-harden/scripts/study.py"
   python3 "$S" init <subject-slug> --title "Readable Title" --open
   ```
   It creates the folder under `STUDY_ROOT` (set in `config.env`, default: the user's Obsidian vault `_Learning_Resource/Study/`), fills the templates, and opens the `QA_LOG.md` dashboard in Obsidian (Dataview renders the tables). Layout:
   ```
   INDEX.md                    concept list + status (agent edits this)
   QA_LOG.md                   dashboard (Dataview queries + generated Manual Summary)
   questions/Q-NNN.md          one note per question (created by the script)
   concepts/NN-<slug>.md       one per concept: copy of references/concept-dossier-template.md
   evidence/                   repro scripts, captures, numbers (name: X<id>-<short>.<ext>)
   lessons/                    visual-compiler outputs
   ```
   If the study folder must live in the project instead, set `STUDY_ROOT` in `config.env` (or as an env var). Never use a dot-folder: Obsidian does not index hidden folders. If the folder is outside the vault, `open` prints the path instead of launching Obsidian.
3. Break the subject into **3–8 concepts**, ordered so each depends only on earlier ones. Write them in `INDEX.md` with status `not-started`.
4. **Every session starts with:** read `INDEX.md`, then run `study.py status <subject-slug>` and show the user its three lists (needs attention / blocked / quiz). Offer them before new work.

---

## 3. The Loop (per concept) with Gates

Each phase ends with a **gate**: a checklist. Do not start the next phase until every box is checked or explicitly marked `blocked` with a reason.

### Phase 1 — Reference (before reading the concept's code)
Tools: `source-driven-development` skill, official docs, web search, context7.
Fill the dossier's **Concept Card**: problem · principle · 2–3 approaches with tradeoffs · invariants · pitfalls.

**Gate 1:**
- [ ] At least 1 `reference` source for the principle (prefer 2 independent ones).
- [ ] At least 2 invariants, each phrased as a testable statement ("instance count ≤ buffer capacity").
- [ ] At least 2 approaches listed (the code's own approach does not count unless a source describes it).
- [ ] If no source found: card says `inferred` and a question note is added (`questions/Q-NNN.md`, `next_action: find source`).

### Phase 2 — Specimen (read the code against the card)
Tools: serena / roslyn MCP / grep / IDE call hierarchy.
Write the call tree for the concept's path only, then the comparison table.

**Classification table (use exactly these words):**
| If the code… | Class |
|---|---|
| does what a listed approach does | `matches` |
| can violate an invariant | `suspected-bug` |
| uses a valid approach that may not fit the target | `tradeoff` |
| uses a different approach that a source supports | `alternative` |
| never enforces an invariant (missing guard, bound, cleanup) | `missing` |
| you can't tell | `unknown` |

**Gate 2:**
- [ ] Every row has `file:line` and label `code`.
- [ ] Every row has exactly one class from the table.
- [ ] No row says "intended", "obviously", "should be fine".

### Phase 3 — Chaos (built from invariants, not from the code)
For each invariant, cross it with the conditions that apply to the target:

| Condition checklist |
|---|
| zero / negative / extreme parameter values |
| capacity overflow, large counts |
| multiple cameras / instances / scenes |
| time: lag spike, very small or large dt, pause |
| lifecycle: enable/disable, domain reload, destroy mid-use, scene change |
| platform: mobile GPU, API differences (Metal/Vulkan/DX), memory limits |
| engine/package upgrade, deprecated APIs |

Write each case as **hypothesis → repro → status** with status in `confirmed` | `not-reproduced` | `blocked`.
Pick at most **5 cases per concept**, highest risk to the target first.
**Full mode:** run `doubt-driven-development` (or `playable-doubt-driven-development` for Unity) as a fresh reviewer. Give it the Concept Card and the code, **not** your conclusions.

**Gate 3:**
- [ ] Each case names the invariant it attacks.
- [ ] Each `confirmed` case has a file in `evidence/` with exact steps and numbers.
- [ ] Each `blocked` case lists exact user steps (menu paths, settings, what to look at, what result means what).

### Phase 4 — Learn (from the dossier, never from raw code)
Call `visual-compiler` in Guided Lesson Mode with the **dossier** as input.
- Happy-path chapters: only from `reference` content.
- Break chapters: only from `confirmed` chaos cases, framed *"This code does X (file:line). The reference says Y. Reproduced failure: Z. Predict what happens."*
- `inferred` items: only as a labeled "open question" box.
- Quizzes: built from question notes with `understanding` = `lost` or `can-follow`, and from misconceptions.

**Gate 4:**
- [ ] Lesson saved in `lessons/`.
- [ ] Lesson contains no fact whose label is `code` or `inferred` presented as truth.
- [ ] User took the quiz; `understanding` in question notes updated **only from the user's answers**.

### Phase 5 — Solve (skip if goal = `learn`)
1. Choose an approach **from the Concept Card's list**. Write: chosen, rejected, why for the target.
2. Spec it (`spec-driven-development` if it touches >1 file or public API).
3. Implement in small slices (`worklog`). One slice = one confirmed case or one invariant.
4. Re-run the **same** repro. Record before → after numbers.

**Gate 5:**
- [ ] Every fixed case has before and after evidence from the same repro.
- [ ] No new `suspected-bug` introduced (re-check the Phase 2 table for touched code).
- [ ] Things the agent can't run (editor, device) are `blocked` with exact user steps — not marked done.

### Phase 6 — Record
- Update dossier status, INDEX.md, question notes, and the QA_LOG summary.
- `confirmed` + fixed pitfalls → `avoid-mistakes` registry.
- Durable decisions → memory.

**Concept is `done` only when:** Gate 1–5 (or 1–4 for `learn`) are all checked, and no question note with `concept:` = this concept has `answer_state` `open`, `explained`, `contested`, or `blocked` (they may be `parked` with a written reason).

---

## 4. The Q&A Log (two axes, never just red/green)

**When to add an entry (any of these):** the user asks a question · pushes back · says "I don't get it" / "confusing" · you revise an earlier answer · a chaos case is blocked. Write the question in the **user's own words**.

**Storage:** one note per question at `questions/Q-NNN.md` (next free number), copied from `references/qa-entry-template.md`. States live in its YAML frontmatter as `answer_state` (axis A) and `understanding` (axis B), using exactly the codes below. `QA_LOG.md` is only a dashboard (Dataview queries + a manual summary table). Never put entries inside `QA_LOG.md`.
**Use the script for all state bookkeeping** (it validates codes, numbers questions, stamps dates, writes History, and regenerates the Manual Summary):
```bash
S="$HOME/Documents/Personal_resources/Agent_Skills/gemini/study-and-harden/scripts/study.py"
python3 "$S" question <slug> <concept> "<user's own words>" --hard <hard_because code>   # new Q-NNN
python3 "$S" set <slug> Q-003 answer_state sourced --note "Unity docs: RenderGraph"     # change a state
python3 "$S" set <slug> Q-003 understanding can-explain --note "user explained it back"  # ONLY from user words/quiz
python3 "$S" set <slug> Q-003 next_action "quiz next session"
python3 "$S" status <slug>    # what needs attention
python3 "$S" open <slug>      # show the dashboard in Obsidian
```
Write the answer, evidence, and blocked steps into the note's body sections with a normal file edit. If an answer is revised, strike the old one through in the body's History.

**Axis A — answer confidence** (frontmatter `answer_state`, about the evidence):
| Code | Meaning | Set when |
|---|---|---|
| `open` ❓ | no answer yet | entry created |
| `explained` 💭 | answered, unbacked | you answered from reasoning/memory |
| `sourced` 📚 | backed by a source | you cite a source you opened |
| `verified` 🔬 | reproduced/measured | evidence file exists |
| `contested` ⚠️ | sources or evidence disagree | new info conflicts with the answer |
| `blocked` 🧱 | can't check yet | needs editor/device/access |
| `parked` ⏸ | out of scope | user agrees; reason written |

**Axis B — user understanding** (frontmatter `understanding`, about the user):
| Code | Meaning | Set when |
|---|---|---|
| `lost` 🌫 | didn't land | default for new entries, or user says so |
| `can-follow` 👀 | makes sense while reading | user says so |
| `can-explain` 🗣 | can say it in own words | user explained it back correctly |
| `can-apply` 🎯 | used it to predict/debug/fix | user passed a prediction quiz or did it in work |

**Rules:**
- The agent sets A. **Only the user's words or quiz results set B.** If unsure, ask: "Can you explain it back in one sentence?"
- States may go **down**. Record the move in History.
- Every entry tags **why it was hard** with one of: `concept-gap`, `terminology`, `code-confusion`, `source-contradiction`, `tooling-env`, `misconception`.
- Every entry not (`verified`|`sourced`) + (`can-apply`) has a **Next action**.
- The Manual Summary in `QA_LOG.md` is generated by the script; never edit it by hand (run `study.py summary <slug>` after manual frontmatter edits).

---

## 5. Response Checklist (run before every reply)

- [ ] I named the current **phase** and **concept**.
- [ ] Every new claim has a label word; no `code`/`inferred` claim is stated as fact.
- [ ] I did not use "intended", "obviously", "should work".
- [ ] New questions/pushback from the user are logged with `study.py question`, and state changes went through `study.py set`.
- [ ] I did not change axis B without the user's words or a quiz result.
- [ ] Files changed are listed; long content lives in the study folder, not chat.
- [ ] I ended with the **next action** (and who does it: agent or user).

---

## 6. Fallbacks (when a tool is missing)

| Missing | Do this |
|---|---|
| No web search / docs access | Mark Concept Card `inferred`; add QA entry "find source"; continue to Phase 2 but **do not** run Phase 4 for that concept |
| Can't run Unity / the device | Chaos cases → `blocked` with exact steps; if the logic is pure math, build a headless repro (Node/Python) and say what it does and doesn't prove |
| No code navigation tool | Use grep; list the search terms used so the user can repeat them |
| Sub-skill not installed | Do that phase inline following its gate; note "skill X unavailable" in INDEX.md |
| No Python / script fails | Edit the note frontmatter by hand using exactly the codes in §4, add a History line, then fix the Manual Summary table by hand; tell the user the script failed and why |
| Obsidian not installed / vault missing | Everything still works as plain markdown; `study.py status` replaces the dashboard. Tell the user to install Obsidian + Dataview and set `config.env` |

---

## 7. Worked Mini-Example (how a correct entry looks)

Concept `02-indirect-draw`, target "PC + Android, ≤ 64 MB VRAM".

- Card invariant: **I1** "instance count passed to the draw ≤ position buffer capacity" — `reference` (Unity docs, `Graphics.RenderMeshIndirect` / indirect args; GPU-driven rendering talk).
- Specimen row: "copies append counter into args with no clamp — `GrassDataRendererFeature.cs:198`" — label `code`, class `missing`.
- Chaos X1: I1 × capacity overflow. Hypothesis: spacing 0.1 → counter > capacity → reads past buffer end. Repro: `blocked` — user steps: set spacing 0.1, enable previewVisibleGrassCount, compare "Visible Grass Count" with maxBufferCount×1e6; capture in Frame Debugger/RenderDoc.
- QA entry `questions/Q-003.md`: question "Why does the GPU decide the count?" — frontmatter `answer_state: sourced`, `understanding: can-follow` (user said "I follow but couldn't explain"), `next_action: quiz next session`.

Wrong version (never write): "The author intended the counter to never exceed capacity, so this is fine."

---

## 8. Anti-Patterns

- Opening the concept's code before Gate 1.
- Teaching from code; "the author intended…".
- Calling something a bug, or a fix done, without a repro.
- Setting axis B yourself.
- Silently replacing a wrong answer.
- More than 5 chaos cases per concept, or cases not tied to an invariant.
- Skipping a gate because "it's obvious".
