# Historical Setup Baseline

[Parent: Setup](README.md)

Inspected 2026-10-21. These are the source project's settings, not requirements or an automatic installation list for a new project.

## Environment

- Unity: **2022.3.19f1**.
- Luna Playable: **7.2.0**, installed through a local `com.unity.playworks.upp` package path.
- Rendering: Built-in render pipeline, Gamma color space.
- Enter Play Mode: Domain Reload disabled; Scene Reload enabled.

## Relevant package references

- Alchemy: Git package `https://github.com/annulusgames/Alchemy.git?path=/Alchemy/Assets/Alchemy`, resolved revision `9288cbcc663b179f060fc195064256af344d4244`.
- TextMesh Pro: **3.0.6**.
- Unity UI: **1.0.0**.
- Device Simulator devices: **1.0.1**.
- Unity development/test tooling was installed; this does not authorize running it.
- Visual Scripting was installed but its use was prohibited. Package presence is not permission to use it or a recommendation to carry it over.

## Luna export reference

- Compiler V2, minification, shader-variant export optimization, and Mecanim WASM were enabled.
- Realtime shadows were disabled.
- Module inclusion/exclusion, explicit asset includes, script exclusions, and scene selection were tailored to that project; choose these again for the new project.

## Timing caution

- Fixed timestep: **0.01**.
- Maximum allowed timestep: **0.02**.
- These are not recommended defaults. Read the historical floating-point catch-up risk in [Learned limitations](../Luna/Learned_Limitations.md) before relying on their apparent ratio.
