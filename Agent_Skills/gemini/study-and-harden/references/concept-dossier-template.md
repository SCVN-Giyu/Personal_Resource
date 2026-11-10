# <NN> · <Concept name>

Subject: <subject> · Target: <platform / budget / scale> · Status: not-started | reference | specimen | chaos | learning | solving | done
Linked Q&A: Q-xxx, Q-yyy

Labels (one per claim): reference 📚 · verified 🔬 · code 📄 · inferred ❓
Gates: see SKILL.md §3 — copy each gate checklist under its section and tick it.

---

## 1. Concept Card (written BEFORE reading the code)

**Problem it solves:**
<one or two sentences> 📚 <source>

**Industry principle:**
<the core idea> 📚 <source>

**Standard approaches & tradeoffs:**
| Approach | How | Pros | Cons | Source |
|---|---|---|---|---|
| A | | | | 📚 |
| B | | | | 📚 |

**Invariants (must always hold):**
- I1: <statement> 📚
- I2: <statement> 📚

**Known pitfalls:**
- <pitfall> 📚

---

## 2. Specimen vs Card (this code)

Call tree (relevant path only):
```
<entry point>
└─ <stage> (file:line)
```

| Stage | Code does (file:line) | Invariant/approach compared | Class | Label |
|---|---|---|---|---|
| | | | matches · suspected-bug · tradeoff · alternative · missing · unknown | code |

---

## 3. Broken Side — Chaos Cases

| ID | Invariant × condition | Hypothesis | Repro (evidence/…) | Status |
|---|---|---|---|---|
| X1 | I1 × <condition> | <what breaks> | <script / steps> | confirmed · not-reproduced · blocked |

Adversarial review (Full mode): <reviewer summary, disagreements>

---

## 4. Solution

**Chosen approach:** <from the Concept Card options> — **why for this target:** <tradeoff reasoning>
**Rejected:** <approach> — <reason>
**Slices:** <link to worklog / list>

**Verification (same repro, before → after):**
| Case | Before | After | Evidence |
|---|---|---|---|
| X1 | | | |

---

## 5. Open Questions
- <question> (inferred) → Q-xxx
