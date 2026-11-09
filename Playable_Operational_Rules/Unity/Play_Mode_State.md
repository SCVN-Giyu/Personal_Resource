# Play Mode State

[Parent: Unity](README.md)

- Check Enter Play Mode settings before creating or changing static fields, static events, caches, or singleton references.
- With Domain Reload disabled, do not assume static state resets between Play sessions. Static field initializers alone are insufficient.
- Explicitly reset session-owned static state and event handlers before the next session. Preserve intentional same-session teardown behavior.
- Where appropriate, an editor-only `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)` hook can reset session state without adding a playable-runtime API requirement.
- With Scene Reload enabled, authored objects can rebind in `Awake`.
- With Scene Reload disabled too, separately handle instance-state reset and rebinding; do not assume `Awake` runs again.
- Checking settings does not authorize entering Play Mode or running tests.

## Historical context, not a required setting

The source project used Domain Reload disabled and Scene Reload enabled. Verify the new project's actual settings instead of copying that assumption.
