# Playable Operational Rules

Shared, agent-independent rules for Unity playable projects. Start here and follow the relative links from general rules to the relevant specific guidance.

## Reading order

1. Always read [General](General/README.md) and its three linked rule files.
2. For Unity work, read [Unity](Unity/README.md) and the relevant linked files.
3. For Luna work, read [Luna](Luna/README.md). Before changing playable runtime behavior, also read [Learned limitations](Luna/Learned_Limitations.md).
4. For a new project or setup change, read [Setup](Setup/README.md). Its version baseline is reference material, not mandatory configuration.

## Point another agent here

Put this instruction in the project's `AGENTS.md`, `GEMINI.md`, or equivalent:

> Read `<PERSONAL_RESOURCES_ROOT>/Playable_Operational_Rules/README.md` and follow its reading order and linked rules. Use this shared folder as the source of truth for general playable operational rules; keep only project-specific context and explicitly agreed exceptions locally. Do not copy the shared rules into the project.

## Scope and maintenance

- Follow higher-priority host instructions. Surface material conflicts with local rules rather than silently combining contradictory guidance.
- Do not treat historical project settings, experiments, or version-specific findings as universal platform requirements.
- Record confirmed failures, static findings, proposals, and user-confirmed fixes distinctly.
- When the user changes a shared rule, update its owning file here rather than creating another competing copy.
- This folder is standalone: its instructions do not depend on another project's files, a skill symlink, or a memory server.
