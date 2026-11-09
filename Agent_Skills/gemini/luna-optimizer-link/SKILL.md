---
name: luna-optimizer-link
description: Link the shared Luna Optimizer tool (Supercent > Luna Optimizer — playable size dashboard, texture/sound optimizer, scene estimate, asset exclusions) into a Unity project as a live package, or unlink it. Use when the user asks to set up, install, add, link, enable, remove or unlink the Luna Optimizer / Luna tooling / Luna size tool in a Unity project.
---

# Luna Optimizer link

Adds one line to the Unity project's `Packages/manifest.json` that points at the shared package folder
`<PERSONAL_RESOURCES_ROOT>/Unity_Packages/com.supercent.luna-texture-optimizer`.
Every linked project uses that same folder, so updates to the tool reach all projects at once.
Nothing else in the project is touched.

## Steps

1. Find the Unity project folder: the folder that contains `ProjectSettings/ProjectVersion.txt` and
   `Packages/manifest.json`. Usually the current working directory or one of its parents. If unsure, ask the user.
2. Run (link):
   ```bash
   python3 -I "<PERSONAL_RESOURCES_ROOT>/Agent_Skills/gemini/luna-optimizer-link/scripts/link_luna_optimizer.py" "<UNITY_PROJECT_FOLDER>"
   ```
   To preview without writing, add `--dry-run`. To remove the tool, add `--unlink`.
3. Report the script's output to the user, including any `WARNING:` lines, and the next step it prints.

## Reading the result

| Output | Meaning | What to do |
|---|---|---|
| `Linked: …` / `Relinked …` | Manifest updated | Tell the user to switch to Unity; it re-resolves packages, then menu **Supercent > Luna Optimizer** |
| `Already linked: …` | Nothing to change | Same next step |
| `WARNING: no Luna install found` | Tool installs, but sizes/predictions/saving need Luna | Tell the user; do not try to install Luna |
| `WARNING: tool verified with Luna 7.2.0 …` | Different Luna version | Tell the user saving to Luna and Measure should be checked on that version |
| `ERROR: … needs Unity 2022.3 or newer` (exit 1) | Project too old | Stop; nothing was written |
| `ERROR: …` (exit 1/2) | Not a Unity project, tool folder missing, broken manifest | Stop and report; do not edit the manifest by hand |

## Rules

- Do not edit `Packages/manifest.json` by hand or change any other file; the script keeps the rest of the manifest
  byte-identical and puts the entry where Unity would.
- Do not copy the package into the project (that breaks the live link); always link the shared folder.
- Do not commit, and do not touch `Packages/packages-lock.json` (Unity updates it).
- Optional extras (compile cache, "Speed up builds") are turned on from inside the tool, not by this skill.
