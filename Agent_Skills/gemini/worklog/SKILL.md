---
name: worklog
description: >-
  Gradual, iterative development tracker that links PM requirement documents to a
  token-efficient Tree + Key-Value WorkLog. Use when converting a PM doc/checklist
  into gradual implementation slices, catching up on current feature state, or
  recording iterative progress (state, problem, solution, constraints, Unity refs,
  and next steps) without one-shot implementation overload.
---

# WorkLog Skill (`worklog`)

Manage feature development **gradually and iteratively** using a two-tier linked markdown system:
1. **Source Spec (`Docs/Specs/Spec_<Feature>.md`)** — The PM requirement checklist with status markers and links to WorkLog nodes.
2. **WorkLog (`Docs/WorkLogs/WorkLog_<Feature>.md`)** — A **Tree + Key-Value** state ledger tracking the active slice, iterative problems, solutions, constraints, scripts, and next steps.

---

## 1. Core Rules

1. **Never Implement in One Go:** When given a PM document or checklist, initialize the Spec + WorkLog tree first, then **stop and ask which single node to start with**.
2. **Tree + Key-Value Only (Zero Prose Essays):** Organize everything as hierarchical nodes with concise `key: value` pairs for instant, low-token agent catch-up and recording.
3. **Iterative State Recording (More Than Just Checkboxes):** Updating a WorkLog means capturing the engineering reality—current `state`, `problem`, `solution`, `constraints`, `decisions`, and `next` step—not just marking `[x]`.
4. **Bi-Directional Linking:** Every Spec checklist item links to its WorkLog node (`[WorkLog#node-id]`), and every WorkLog node references its `spec_ref`.
5. **Preserve Constraints & Solutions on Compaction:** When a node finishes (`state: done`), strip temporary debug scratchpad noise, but **keep** `contracts`, `solution`, and `constraints` so future iterations never violate established boundaries or repeat dead ends.

---

## 2. Document Templates

### A. Source Spec Checklist (`Docs/Specs/Spec_<Feature>.md`)
Preserves the PM's requirements while linking each trackable capability to its WorkLog node.

```markdown
# Spec: <Feature Name>
> worklog: [WorkLog_<Feature>.md](../WorkLogs/WorkLog_<Feature>.md)

## Requirements Checklist
- [x] **1. Core Slot Data** — Item stacking & capacity rules -> [WorkLog#slot-data](../WorkLogs/WorkLog_<Feature>.md#slot-data)
- [~] **2. Drag & Drop UI** — Ghost icon, snapping, swap logic -> [WorkLog#drag-drop](../WorkLogs/WorkLog_<Feature>.md#drag-drop)
- [!] **3. Equipment Validation** — Class/level restrictions -> [WorkLog#equip-validation](../WorkLogs/WorkLog_<Feature>.md#equip-validation)
- [ ] **4. Hotbar Quick-Use** — Cooldowns & keybinds -> [WorkLog#hotbar](../WorkLogs/WorkLog_<Feature>.md#hotbar)
```
*(Status legend: `[ ]` pending | `[~]` in-progress / iterating | `[!]` blocked | `[x]` done)*

---

### B. Tree + Key-Value WorkLog (`Docs/WorkLogs/WorkLog_<Feature>.md`)

```markdown
# WorkLog: <Feature Name>

```yaml
meta:
  spec_doc: ../Specs/Spec_<Feature>.md
  active_focus: drag-drop.ghost-icon
  updated: YYYY-MM-DD
```

## Tree Overview
```text
<Feature>
├── slot-data [done]
├── drag-drop [iterating]
│   ├── pointer-input [done]
│   ├── ghost-icon [in-progress]  <-- ACTIVE FOCUS
│   └── drop-swap [pending]
├── equip-validation [blocked]
└── hotbar [pending]
```

---

## Nodes

### `slot-data` {#slot-data}
```yaml
spec_ref: "Spec#1 Core Slot Data"
state: done
scripts: [InventorySlot.cs, ItemDefinitionSO.cs]
unity_refs: [Assets/Data/Items/]
contracts: "IReadOnlySlot; fires OnSlotChanged(int slotIndex, ItemStack stack)"
solution: "Struct-based ItemStack to avoid GC alloc on stack split"
constraints: "MaxStack must be validated in SO OnValidate; never mutate stack outside InventoryService"
```

### `drag-drop` {#drag-drop}
```yaml
spec_ref: "Spec#2 Drag & Drop UI"
state: iterating
├── pointer-input:
│     state: done
│     scripts: [SlotPointerHandler.cs]
│     contracts: "Implements IBeginDragHandler, IDragHandler, IEndDragHandler"
│
├── ghost-icon:  # <-- ACTIVE FOCUS
│     state: in-progress
│     scripts: [DragGhostView.cs, UIRootCanvas.cs]
│     unity_refs: ["Canvas/OverlayLayer/GhostIconPrefab"]
│     problem: "Ghost icon drifts from cursor when Canvas Scaler is in 'Scale With Screen Size' mode"
│     solution: "Testing RectTransformUtility.ScreenPointToLocalPointInRectangle using OverlayLayer RectTransform"
│     rejected: "Direct Input.mousePosition assignment (breaks on non-1080p aspect ratios)"
│     constraints: "Ghost must render above all windows; disable raycastTarget on ghost Image so OnDrop fires on target slot"
│     next: "Verify local point conversion with camera=null for Screen Space Overlay"
│
└── drop-swap:
      state: pending
      next: "Wire IDropHandler to InventoryService.TrySwapOrMerge(fromIndex, toIndex)"
```

### `equip-validation` {#equip-validation}
```yaml
spec_ref: "Spec#3 Equipment Validation"
state: blocked
problem: "Waiting on CharacterStats system to expose level/class query interface"
constraints: "Must not reference concrete PlayerController; use IStatProvider"
next: "Stub IStatProvider interface once drag-drop finishes"
```

### `hotbar` {#hotbar}
```yaml
spec_ref: "Spec#4 Hotbar Quick-Use"
state: pending
```
```

---

## 3. Standard Key Vocabulary

Use consistent keys inside nodes so any agent can parse them immediately:

| Key | Purpose |
| :--- | :--- |
| `spec_ref` | Backlink to the section/item in `Spec_<Feature>.md` |
| `state` | `pending` \| `in-progress` \| `iterating` \| `blocked` \| `done` |
| `scripts` | C# scripts / classes created or modified |
| `unity_refs` | Scene paths, Prefabs, ScriptableObjects, Animator/Canvas refs |
| `contracts` | Public APIs, events, interfaces exposed to other nodes |
| `problem` | Current bug, edge case, or design friction being solved |
| `solution` | Active approach or verified fix that solved the problem |
| `rejected` | Tried-and-failed approaches (prevents future sessions from repeating dead ends) |
| `constraints` | Hard rules (GC alloc limits, execution order, Unity lifecycle gotchas, architectural boundaries) |
| `next` | Immediate next concrete step to execute |

---

## 4. Workflow Procedures

### Mode 1: Initialize from PM Document
When the user provides a new PM doc, feature brief, or raw requirement list:
1. Create or update `Docs/Specs/Spec_<Feature>.md` with a clean checklist and anchor links to the WorkLog.
2. Create `Docs/WorkLogs/WorkLog_<Feature>.md` with the `Tree Overview` and stubbed `pending` nodes.
3. **Do NOT start coding the whole feature.** Ask the user which node to set as `active_focus` first.

### Mode 2: Catch Up (Session Start / Resume)
When starting or resuming work on a feature:
1. Read `Docs/WorkLogs/WorkLog_<Feature>.md`.
2. Check `meta.active_focus` and inspect that node's `state`, `problem`, `solution`, `constraints`, and `next` fields (plus sibling `contracts`).
3. Summarize the current focus and proposed action in 3–4 bullet lines, then proceed with only that slice.

### Mode 3: Record & Iterate (During / End of Session)
When progress is made, a bug is hit, a design pivots, or the user says "update worklog":
1. **In-Place Tree Update:** Update the active node's `state`, `problem`, `solution`, `rejected`, `constraints`, `scripts`, `unity_refs`, and `next`.
2. **Update Tree Overview:** Keep the ASCII tree header status tags (`[done]`, `[in-progress]`, `[iterating]`, `[blocked]`, `[pending]`) in sync.
3. **Sync Source Spec:** If a top-level item's status changed (`[ ]` -> `[~]` or `[x]`), update `Docs/Specs/Spec_<Feature>.md`.
4. **Compact on Completion:** When a node moves to `done`, remove transient WIP scratch notes while keeping `scripts`, `unity_refs`, `contracts`, `solution`, and `constraints` intact. Advance `meta.active_focus` to the next target node.
