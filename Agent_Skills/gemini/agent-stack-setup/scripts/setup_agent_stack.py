#!/usr/bin/env python3
"""
Portable Agent Stack Bootstrapper (Zero-Dependency Python 3)

Automatically detects the current machine's Personal_resources root, user home,
and installed tools, then links skills, global auto-trigger workflow rules, and
MCP servers across Antigravity/Gemini, Claude Code, Cursor, and Codex.

Can also initialize a project's AGENTS.md / CLAUDE.md with the right domain profile.
"""

from __future__ import annotations
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


def resolve_repo_root() -> Path:
    # Script lives at <REPO_ROOT>/Agent_Skills/gemini/agent-stack-setup/scripts/setup_agent_stack.py
    script_path = Path(__file__).resolve()
    repo_root = script_path.parents[4]
    if not (repo_root / "Agent_Workflow_Tree" / "README.md").exists():
        sys.exit(f"ERROR: Could not locate Agent_Workflow_Tree/README.md under {repo_root}")
    return repo_root


def build_workflow_block(repo_root: Path) -> str:
    tree_readme = (repo_root / "Agent_Workflow_Tree" / "README.md").as_posix()
    return (
        "<!-- workflow-tree:start -->\n"
        "## Auto-Trigger Workflow & Operational Tree\n\n"
        f"Read `{tree_readme}` at task start. Treat it as the root routing map: "
        "follow only the phase branches (`Task_Start_And_Planning`, `Code_And_Verification`, "
        "`Adversarial_And_Chaos`) and domain leaves (`Playable_Operational_Rules`, "
        "`avoid-mistakes`, `playable-doubt-driven-development`, or `doubt-driven-development`) "
        "triggered by the current task without loading unrelated branches.\n"
        "<!-- workflow-tree:end -->"
    )


def build_agentmemory_block() -> str:
    return (
        "<!-- agentmemory:start -->\n"
        "## Agent memory (agentmemory)\n\n"
        "You have persistent long-term memory via the agentmemory MCP server. "
        "Tools: `memory_recall`, `memory_smart_search`, `memory_save`, `memory_sessions`.\n\n"
        "- At the START of a task, call `memory_recall` (or `memory_smart_search`) with the task "
        "context to load relevant past decisions, fixes, and preferences before asking the user to repeat anything.\n"
        "- When you learn something durable (a decision, a fix, a gotcha, a user preference, a project convention), "
        "call `memory_save` to persist it.\n"
        "- Prefer recalling over re-deriving, and save concise reusable facts rather than transcripts.\n"
        "<!-- agentmemory:end -->"
    )


def upsert_marked_block(text: str, start_marker: str, end_marker: str, new_block: str) -> str:
    pattern = re.compile(
        re.escape(start_marker) + r".*?" + re.escape(end_marker),
        re.DOTALL,
    )
    if pattern.search(text):
        return pattern.sub(new_block, text)
    text = text.rstrip()
    if text:
        return text + "\n\n" + new_block + "\n"
    return new_block + "\n"


def update_markdown_file(path: Path, repo_root: Path, dry_run: bool) -> str:
    existing = path.read_text(encoding="utf-8") if path.exists() else ""
    updated = upsert_marked_block(
        existing,
        "<!-- agentmemory:start -->",
        "<!-- agentmemory:end -->",
        build_agentmemory_block(),
    )
    updated = upsert_marked_block(
        updated,
        "<!-- workflow-tree:start -->",
        "<!-- workflow-tree:end -->",
        build_workflow_block(repo_root),
    )
    if updated == existing and path.exists():
        return f"OK (unchanged): {path}"
    if not dry_run:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(updated, encoding="utf-8")
    return f"{'Would update' if dry_run else 'Updated'}: {path}"


def update_cursor_rule(path: Path, repo_root: Path, dry_run: bool) -> str:
    content = (
        "---\n"
        "description: Root auto-trigger workflow and domain routing tree\n"
        "alwaysApply: true\n"
        "---\n\n"
        + build_workflow_block(repo_root)
        + "\n"
    )
    existing = path.read_text(encoding="utf-8") if path.exists() else ""
    if existing == content:
        return f"OK (unchanged): {path}"
    if not dry_run:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    return f"{'Would update' if dry_run else 'Updated'}: {path}"


def ensure_symlink(target: Path, link_path: Path, dry_run: bool) -> str:
    if link_path.is_symlink():
        current = Path(os.readlink(link_path))
        if not current.is_absolute():
            current = (link_path.parent / current).resolve()
        if current == target.resolve():
            return f"OK (linked): {link_path} -> {target}"
        if not dry_run:
            link_path.unlink()
            link_path.symlink_to(target)
        return f"{'Would relink' if dry_run else 'Relinked'}: {link_path} -> {target}"
    elif link_path.exists():
        return f"SKIP (non-symlink exists at {link_path}; leaving untouched)"
    else:
        if not dry_run:
            link_path.parent.mkdir(parents=True, exist_ok=True)
            link_path.symlink_to(target)
        return f"{'Would link' if dry_run else 'Linked'}: {link_path} -> {target}"


def ensure_skill_entries_in_dir(skills_source: Path, dest_dir: Path, dry_run: bool) -> list[str]:
    results = []
    if not dest_dir.exists() and not dry_run:
        dest_dir.mkdir(parents=True, exist_ok=True)
    for item in sorted(skills_source.iterdir()):
        if item.name.startswith(".") or item.name == "synced":
            continue
        if item.is_dir() and (item / "SKILL.md").exists():
            link_path = dest_dir / item.name
            results.append(ensure_symlink(item, link_path, dry_run))
    return results


def find_binary(name: str, fallback_paths: list[Path]) -> str:
    found = shutil.which(name)
    if found:
        return found
    for p in fallback_paths:
        if p.exists():
            return p.as_posix()
    return name


def update_json_mcp_config(
    path: Path,
    serena_context: str,
    serena_bin: str,
    roslyn_bin: str,
    dry_run: bool,
) -> str:
    data = {}
    if path.exists():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception as exc:
            return f"ERROR reading {path}: {exc}"
    if "mcpServers" not in data or not isinstance(data["mcpServers"], dict):
        data["mcpServers"] = {}

    servers = data["mcpServers"]
    desired = {
        "agentmemory": {
            "command": "npx",
            "args": ["-y", "@agentmemory/mcp"],
            "env": {
                "AGENTMEMORY_URL": "${AGENTMEMORY_URL:-http://localhost:3111}",
                "AGENTMEMORY_SECRET": "${AGENTMEMORY_SECRET:-}",
                "AGENTMEMORY_TOOLS": "${AGENTMEMORY_TOOLS:-all}",
            },
        },
        "serena": {
            "command": serena_bin,
            "args": [
                "start-mcp-server",
                f"--context={serena_context}",
                "--project-from-cwd",
            ],
        },
        "roslyn-mcp": {
            "command": roslyn_bin,
            "args": [],
        },
        "context7": {
            "command": "npx",
            "args": ["-y", "@upstash/context7-mcp"],
        },
        "sequential-thinking": {
            "command": "npx",
            "args": ["-y", "@modelcontextprotocol/server-sequential-thinking"],
        },
    }

    changed = False
    for k, v in desired.items():
        if servers.get(k) != v:
            servers[k] = v
            changed = True

    if not changed and path.exists():
        return f"OK (unchanged): {path}"
    if not dry_run:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    return f"{'Would update' if dry_run else 'Updated'} MCP config: {path}"


def update_codex_toml_mcp(path: Path, roslyn_bin: str, dry_run: bool) -> str:
    if not path.exists():
        return f"SKIP (Codex config not found at {path})"
    text = path.read_text(encoding="utf-8")
    original = text

    blocks = {
        "[mcp_servers.roslyn-mcp]": (
            f'[mcp_servers.roslyn-mcp]\ncommand = "{roslyn_bin}"\nargs = []\n'
        ),
        "[mcp_servers.context7]": (
            '[mcp_servers.context7]\ncommand = "npx"\nargs = [ "-y", "@upstash/context7-mcp" ]\n'
        ),
        "[mcp_servers.sequential-thinking]": (
            '[mcp_servers.sequential-thinking]\ncommand = "npx"\nargs = [ "-y", "@modelcontextprotocol/server-sequential-thinking" ]\n'
        ),
    }

    for header, block in blocks.items():
        if header not in text:
            text = text.rstrip() + "\n\n" + block

    if text == original:
        return f"OK (unchanged): {path}"
    if not dry_run:
        path.write_text(text + "\n", encoding="utf-8")
    return f"{'Would update' if dry_run else 'Updated'} Codex MCP config: {path}"


def init_project_instructions(
    project_dir: Path,
    profile: str,
    repo_root: Path,
    dry_run: bool,
) -> list[str]:
    project_dir = project_dir.resolve()
    if not project_dir.exists():
        sys.exit(f"ERROR: Project directory does not exist: {project_dir}")

    tree_readme = (repo_root / "Agent_Workflow_Tree" / "README.md").as_posix()
    playable_rules = (repo_root / "Playable_Operational_Rules" / "README.md").as_posix()
    unity_avoid = (repo_root / "Agent_Skills" / "gemini" / "avoid-mistakes" / "references" / "unity.md").as_posix()
    luna_avoid = (repo_root / "Agent_Skills" / "gemini" / "avoid-mistakes" / "references" / "playable-luna.md").as_posix()
    playable_ddd = (repo_root / "Agent_Skills" / "gemini" / "playable-doubt-driven-development" / "SKILL.md").as_posix()
    general_ddd = (repo_root / "Agent_Skills" / "gemini" / "doubt-driven-development" / "SKILL.md").as_posix()

    if profile == "playable":
        domain_section = (
            "<!-- agent-stack:start -->\n"
            "## Shared Operational Rules & Workflow Tree\n\n"
            f"1. **Root Workflow Map:** Read `{tree_readme}` at task start and follow only the triggered phase and domain branches.\n"
            f"2. **Playable Operational Rules:** Read `{playable_rules}` and follow its reading order (`General`, `Unity`, `Luna/Learned_Limitations.md`). Keep only project-specific context and agreed exceptions locally.\n"
            "3. **Active Avoid Lists & Chaos Gate:**\n"
            f"   - [Unity Anti-Patterns](file://{unity_avoid})\n"
            f"   - [Playable Luna Constraints](file://{luna_avoid})\n"
            f"   - [Playable Doubt-Driven Development](file://{playable_ddd})\n"
            "<!-- agent-stack:end -->"
        )
    elif profile == "unity":
        domain_section = (
            "<!-- agent-stack:start -->\n"
            "## Shared Workflow Tree & Unity Constraints\n\n"
            f"1. **Root Workflow Map:** Read `{tree_readme}` at task start and follow only the triggered phase and domain branches.\n"
            "2. **Active Avoid Lists & Verification Gate:**\n"
            f"   - [Unity Anti-Patterns](file://{unity_avoid})\n"
            f"   - [Doubt-Driven Development](file://{general_ddd})\n"
            "<!-- agent-stack:end -->"
        )
    else:
        domain_section = (
            "<!-- agent-stack:start -->\n"
            "## Shared Workflow Tree & Verification Gate\n\n"
            f"1. **Root Workflow Map:** Read `{tree_readme}` at task start and follow only the triggered phase and domain branches.\n"
            f"2. **Adversarial Gate:** Apply [Doubt-Driven Development](file://{general_ddd}) for non-trivial architectural and implementation decisions.\n"
            "<!-- agent-stack:end -->"
        )

    target_file = project_dir / "AGENTS.md"
    existing = target_file.read_text(encoding="utf-8") if target_file.exists() else f"# {project_dir.name} Agent Instructions\n"
    updated = upsert_marked_block(existing, "<!-- agent-stack:start -->", "<!-- agent-stack:end -->", domain_section)

    msgs = []
    if updated == existing and target_file.exists():
        msgs.append(f"OK (unchanged): {target_file}")
    else:
        if not dry_run:
            target_file.write_text(updated, encoding="utf-8")
        msgs.append(f"{'Would update' if dry_run else 'Updated'} project instructions ({profile}): {target_file}")
    return msgs


def main() -> None:
    parser = argparse.ArgumentParser(description="Bootstrap or verify the portable Agent Stack on this machine.")
    parser.add_argument("--dry-run", action="store_true", help="Preview changes without modifying files.")
    parser.add_argument("--install-tools", action="store_true", help="Install missing CLI binaries (e.g. RoslynMcp.Server via dotnet).")
    parser.add_argument("--init-project", metavar="PATH", help="Initialize or update a project's AGENTS.md with the shared stack links.")
    parser.add_argument(
        "--profile",
        choices=["playable", "unity", "general"],
        default="playable",
        help="Domain profile when using --init-project (default: playable).",
    )
    args = parser.parse_args()

    repo_root = resolve_repo_root()
    skills_source = repo_root / "Agent_Skills" / "gemini"
    home = Path.home()

    print(f"=== Portable Agent Stack Setup ===")
    print(f"Detected Personal_resources root: {repo_root}")
    print(f"Detected Home directory:          {home}")
    print(f"Mode:                             {'DRY-RUN (no writes)' if args.dry_run else 'APPLY'}\n")

    if args.init_project:
        for msg in init_project_instructions(Path(args.init_project), args.profile, repo_root, args.dry_run):
            print("  " + msg)
        return

    # 1. Check / Install Binaries
    dotnet_bin = shutil.which("dotnet")
    roslyn_default = home / ".dotnet" / "tools" / ("roslyn-mcp.exe" if os.name == "nt" else "roslyn-mcp")
    if args.install_tools and dotnet_bin and not roslyn_default.exists() and not shutil.which("roslyn-mcp"):
        print("[Tools] Installing RoslynMcp.Server via dotnet tool...")
        if not args.dry_run:
            subprocess.run([dotnet_bin, "tool", "install", "-g", "RoslynMcp.Server"], check=False)

    serena_bin = find_binary("serena", [home / ".local" / "bin" / "serena"])
    roslyn_bin = find_binary("roslyn-mcp", [roslyn_default])

    print("[1/4] Global Agent Instruction Files (Auto-Trigger Tree + Memory):")
    for md_target in [
        home / ".gemini" / "GEMINI.md",
        home / ".claude" / "CLAUDE.md",
        home / ".codex" / "AGENTS.md",
    ]:
        print("  " + update_markdown_file(md_target, repo_root, args.dry_run))
    print("  " + update_cursor_rule(home / ".cursor" / "rules" / "workflow-tree.mdc", repo_root, args.dry_run))

    print("\n[2/4] Global Skill Symlinks:")
    for link_target in [
        home / ".gemini" / "antigravity-cli" / "skills",
        home / ".gemini" / "config" / "skills",
        home / ".claude" / "skills",
        home / ".codex" / "skills" / "gemini",
    ]:
        print("  " + ensure_symlink(skills_source, link_target, args.dry_run))

    print("\n[3/4] Individual Skill Links in ~/.agents/skills:")
    agent_links = ensure_skill_entries_in_dir(skills_source, home / ".agents" / "skills", args.dry_run)
    print(f"  Verified {len(agent_links)} skills in {home / '.agents' / 'skills'}")

    print("\n[4/4] MCP Server Configurations:")
    print("  " + update_json_mcp_config(
        home / ".gemini" / "config" / "mcp_config.json",
        serena_context="antigravity",
        serena_bin=serena_bin,
        roslyn_bin=roslyn_bin,
        dry_run=args.dry_run,
    ))
    print("  " + update_json_mcp_config(
        home / ".cursor" / "mcp.json",
        serena_context="ide",
        serena_bin=serena_bin,
        roslyn_bin=roslyn_bin,
        dry_run=args.dry_run,
    ))
    print("  " + update_codex_toml_mcp(
        home / ".codex" / "config.toml",
        roslyn_bin=roslyn_bin,
        dry_run=args.dry_run,
    ))

    # Tool readiness summary
    print("\n=== Tool Readiness Check ===")
    print(f"  npx:        {shutil.which('npx') or 'MISSING (install Node.js)'}")
    print(f"  dotnet:     {dotnet_bin or 'MISSING (install .NET 9+ SDK for roslyn-mcp)'}")
    print(f"  roslyn-mcp: {roslyn_bin if Path(roslyn_bin).exists() or shutil.which('roslyn-mcp') else 'MISSING (run with --install-tools)'}")
    print(f"  serena:     {serena_bin if Path(serena_bin).exists() or shutil.which('serena') else 'MISSING (install serena)'}")


if __name__ == "__main__":
    main()
