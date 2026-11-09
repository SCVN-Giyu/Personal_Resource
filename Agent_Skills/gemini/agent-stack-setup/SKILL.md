---
name: agent-stack-setup
description: >-
  Bootstrap, sync, or verify the portable Agent Stack (Agent_Workflow_Tree, Playable_Operational_Rules,
  shared skills, and MCP servers: agentmemory, serena, roslyn-mcp, context7, sequential-thinking)
  on a new or existing machine across Antigravity/Gemini, Claude Code, Cursor, and Codex. Also initializes
  project-level AGENTS.md files for Playable Luna, Unity, or General projects.
---

# Agent Stack Setup (Portable Machine & Project Bootstrapper)

This skill makes your entire **Personal_resources** AI workflow portable across any machine, OS user path, or coding agent (**Antigravity/Gemini**, **Claude Code**, **Cursor**, and **Codex**).

Instead of hardcoding `/Users/giyu/...`, the bundled Python script dynamically resolves the location of `Personal_resources` from its own path (`__file__`) and configures the host machine or target project idempotently.

---

## When to Use

1. **Setting up a new machine** after cloning `Personal_resources`.
2. **Syncing or auditing the current machine** when adding a new agent (Claude, Cursor, Codex, Antigravity) or after adding new shared skills.
3. **Onboarding a project** (`AGENTS.md`) with the right domain profile (`playable`, `unity`, or `general`) without copying rules into the repo.

---

## 1. Machine Bootstrap / Sync Workflow

Locate `setup_agent_stack.py` inside this skill's `scripts/` directory and run:

### Preview Changes (Dry Run)
```bash
python3 -I "<SKILL_DIR>/scripts/setup_agent_stack.py" --dry-run
```

### Apply Full Machine Setup (and Install Missing `.NET` Roslyn Tool)
```bash
python3 -I "<SKILL_DIR>/scripts/setup_agent_stack.py" --install-tools
```

### What the Script Configures Automatically:
1. **Global Agent Prompt Files** (injects `agentmemory` + `Agent_Workflow_Tree` auto-trigger blocks with the current machine's exact path):
   - `~/.gemini/GEMINI.md` (Antigravity / Gemini CLI)
   - `~/.claude/CLAUDE.md` (Claude Code)
   - `~/.codex/AGENTS.md` (Codex CLI / App)
   - `~/.cursor/rules/workflow-tree.mdc` (Cursor IDE)
2. **Global Skill Symlinks**:
   - Links `~/.gemini/antigravity-cli/skills`, `~/.gemini/config/skills`, `~/.claude/skills`, `~/.codex/skills/gemini`, and `~/.agents/skills/*` directly to `<REPO_ROOT>/Agent_Skills/gemini`.
3. **MCP Server Configs**:
   - Wires `agentmemory`, `serena`, `roslyn-mcp`, `context7`, and `sequential-thinking` into `~/.gemini/config/mcp_config.json`, `~/.cursor/mcp.json`, and `~/.codex/config.toml` (preserving existing project entries like `ai-game-developer` or `unityMCP`).

---

## 2. Project Onboarding Workflow (`--init-project`)

To wire a project repository to the shared `Agent_Workflow_Tree` and domain leaves without duplicating files:

### For a Unity + Playable Luna Project (Default):
```bash
python3 -I "<SKILL_DIR>/scripts/setup_agent_stack.py" --init-project "<PROJECT_PATH>" --profile playable
```
*Links `Agent_Workflow_Tree/README.md`, `Playable_Operational_Rules/README.md`, `unity.md`, `playable-luna.md`, and `playable-doubt-driven-development`.*

### For a Standalone Unity Project (Non-Playable):
```bash
python3 -I "<SKILL_DIR>/scripts/setup_agent_stack.py" --init-project "<PROJECT_PATH>" --profile unity
```
*Links `Agent_Workflow_Tree/README.md`, `unity.md`, and general `doubt-driven-development`.*

### For a General / Personal Project (Non-Game):
```bash
python3 -I "<SKILL_DIR>/scripts/setup_agent_stack.py" --init-project "<PROJECT_PATH>" --profile general
```
*Links `Agent_Workflow_Tree/README.md` and general `doubt-driven-development`.*

---

## Rules

- Always run `--dry-run` first if the user only asked to inspect or check their setup.
- Never overwrite unrelated sections in `GEMINI.md`, `CLAUDE.md`, `AGENTS.md`, or `mcp_config.json`; the script updates only the `<!-- workflow-tree:start -->`, `<!-- agentmemory:start -->`, and `<!-- agent-stack:start -->` blocks.
