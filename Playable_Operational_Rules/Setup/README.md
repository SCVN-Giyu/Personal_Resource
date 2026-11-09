# Playable Setup

[Back to root](../README.md)

Use this checklist when starting a project or changing setup. Inspect first; do not install packages or change project settings without authorization.

## Establish the actual environment

- Read `ProjectSettings/ProjectVersion.txt` and identify the installed Luna version.
- Inspect `Packages/manifest.json` and `Packages/packages-lock.json`; distinguish declared versions from resolved revisions.
- Resolve machine-local SDK paths for the new project instead of copying another project's relative path.
- Check the active rendering pipeline, color space, and Enter Play Mode settings. They are project choices, not inherited universal requirements.
- Follow [MCP servers](../General/MCP_Servers.md) and confirm project-bound connections target this project.

## Configure the playable deliberately

- Set the correct project/application identity, creative name, startup scene, and export scenes.
- Review script/asset exclusions, explicit asset includes, shader handling, modules, compression/minification, and animation options for this project's needs.
- Do not copy another project's module flags, scene selection, external libraries, export identity, or gameplay tuning wholesale.
- Keep editor-only dependencies separate from exported runtime requirements. An installed package is not automatically needed by the playable.
- Confirm the actual destination network's current size/delivery requirements.
- Check configuration caching concerns in [Learned limitations](../Luna/Learned_Limitations.md) when editing Luna configuration with Unity open.

## Keep the shared rules portable

- Reference the [root entry point](../README.md) from the project's own agent instruction file; do not duplicate the shared rule text.
- Keep project-specific decisions and approved exceptions local and explicit. Surface conflicting old instructions before relying on them.
- Preserve legitimate `.meta` files with their assets. Do not carry `Library`, `Temp`, `LunaTemp`, logs, generated project files, or caches as setup.
- A linked external folder or submodule is not made self-contained merely by copying a parent directory. Check required dependencies without importing unrelated content.

See [Historical baseline](Reference_Baseline.md) only when an existing-version reference is useful.
