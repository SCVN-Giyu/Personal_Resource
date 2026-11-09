# Agent Workflow Tree (Auto-Trigger Map)

Shared, zero-waste root routing map for AI coding agents (Antigravity/Gemini, Claude, Cursor, Codex, and local LLMs).
Start here at the beginning of a task and load **only** the branch and leaf files triggered by the current phase and domain.

---

## 1. Phase Triggers (Always Active Across All Projects)

Follow these branches based on what phase of work is happening right now:

1. **Task Start, Planning & Tool Discovery** → Read [Workflow/Task_Start_And_Planning.md](Workflow/Task_Start_And_Planning.md)
   - *Triggers:* Starting a new session/task, ambiguous requirements, multi-step planning, or using a non-thinking local model.
2. **Code Navigation, Editing & Refactoring** → Read [Workflow/Code_And_Verification.md](Workflow/Code_And_Verification.md)
   - *Triggers:* Modifying existing files, refactoring C#/TS/JS symbols, looking up unfamiliar library APIs, or verifying compiler diagnostics.
3. **Adversarial Stress-Testing & Error Recovery** → Read [Workflow/Adversarial_And_Chaos.md](Workflow/Adversarial_And_Chaos.md)
   - *Triggers:* Finishing a non-trivial feature/decision, fixing a bug, or when the user says *"doubt this"* / *"chaos test"*.

---

## 2. Domain Triggers (Load Only When Matching Project Stack)

Do **not** load Unity or Luna leaves in non-game projects. Match the active workspace:

- **Unity + Playable Luna Projects** (Daily Production):
  1. Operational Rules Tree: [Playable_Operational_Rules/README.md](../Playable_Operational_Rules/README.md)
     - [General](../Playable_Operational_Rules/General/README.md) | [Unity](../Playable_Operational_Rules/Unity/README.md) | [Luna](../Playable_Operational_Rules/Luna/README.md) (`Learned_Limitations.md`)
  2. Curated Avoid Lists:
     - [unity.md](../Agent_Skills/gemini/avoid-mistakes/references/unity.md)
     - [playable-luna.md](../Agent_Skills/gemini/avoid-mistakes/references/playable-luna.md)
  3. Playable Adversarial Skill:
     - [playable-doubt-driven-development](../Agent_Skills/gemini/playable-doubt-driven-development/SKILL.md) & [playable-chaos-contract.md](../Agent_Skills/gemini/playable-doubt-driven-development/references/playable-chaos-contract.md)

- **Unity (Non-Playable / Standalone Game Projects)**:
  1. [Playable_Operational_Rules/Unity/README.md](../Playable_Operational_Rules/Unity/README.md) & [unity.md](../Agent_Skills/gemini/avoid-mistakes/references/unity.md)
  2. [unity-alchemy](../Agent_Skills/gemini/unity-alchemy/SKILL.md) (for Inspector UI)
  3. [doubt-driven-development](../Agent_Skills/gemini/doubt-driven-development/SKILL.md)

- **General Software / Personal Non-Game Projects**:
  1. [architecture-boundary](../Agent_Skills/gemini/architecture-boundary/SKILL.md)
  2. [doubt-driven-development](../Agent_Skills/gemini/doubt-driven-development/SKILL.md)
  3. Domain-specific registry in [avoid-mistakes/references/INDEX.md](../Agent_Skills/gemini/avoid-mistakes/references/INDEX.md)

---

## 3. How to Point Any Agent or Project Here

Place this minimal snippet in `~/.gemini/GEMINI.md`, `~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md`, or a project's `AGENTS.md` (run the `agent-stack-setup` skill to inject it with the current machine's exact repo path automatically):

> Read `<PERSONAL_RESOURCES_ROOT>/Agent_Workflow_Tree/README.md` at task start. Treat it as the root routing tree: follow only the phase and domain links relevant to the current task without loading unrelated branches.
