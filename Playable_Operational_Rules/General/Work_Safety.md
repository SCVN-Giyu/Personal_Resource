# Work Safety

[Parent: General](README.md)

## Authorization

- Do not enter Play Mode, run tests, build, or export unless the user explicitly authorizes that verification.
- Do not run verification automatically after writing scripts. State what has and has not been verified.
- Do not expand an implementation, migration, or fix into unrelated work.
- Preserve agreed behavior and current tuning during migrations; do not add an unrequested redesign.

## Existing work and assets

- Treat unfamiliar edits and files as user work. Do not revert, discard, delete, or overwrite them without clear authorization.
- Preserve live tuning, test scenes, and dirty Unity scenes. Do not reopen a scene over unsaved edits.
- A scoped scene-save authorization does not authorize saving future unrelated dirty state.
- Reuse authoritative shared assets where the project already uses them. Do not merge stale copied tuning into current originals.
- Do not silently change cameras, object scale, physics values, or gameplay tuning to hide an integration problem.
- Change original source/configuration, not generated export files.

## Reporting

- Separate intended behavior, inspected implementation, static verification, and observed runtime results.
- If creating a handoff, show its full absolute filesystem path explicitly in the final response.
