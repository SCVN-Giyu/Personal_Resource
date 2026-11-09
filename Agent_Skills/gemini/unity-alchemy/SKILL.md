---
name: unity-alchemy
description: >-
  Enforce Alchemy-first declarative Unity inspector tooling.
  Use when designing, enhancing, or refactoring Unity Inspector UI, custom editors, or editor tools.
  Checks for com.annulusgames.alchemy and leverages attributes before creating boilerplate CustomEditor scripts.
---

# Unity Alchemy Skill (Declarative Inspector First)

A modular, token-optimized guide and gatekeeper for Unity Inspector enhancements using the **Alchemy** package (`com.annulusgames.alchemy`).

Designed for cross-agent workflows (**Gemini**, **Claude**, **GPT / Cursor**, **Codex**).

---

## ⚡ The "Alchemy First" Gatekeeper Rule

Whenever you need custom inspector behavior, buttons, grouped parameters, conditional fields, or editor validation in Unity:

```
[Inspector Requirement]
       │
       ▼
1. Is Alchemy installed? (Check Packages/manifest.json for com.annulusgames.alchemy)
       ├── NO  ─► Standard Unity (IMGUI / UI Toolkit CustomEditor)
       └── YES ─► Proceed to step 2
                   │
                   ▼
2. Can Alchemy declarative attributes solve this?
       ├── YES ─► Use Alchemy Attributes (NO CustomEditor script needed!)
       └── NO  ─► Can an Alchemy Custom Drawer / ObjectAdapter solve this?
                   ├── YES ─► Write an Alchemy Drawer (references/custom-drawers.md)
                   └── NO  ─► Write custom Editor / UI Toolkit script
```

> [!IMPORTANT]
> **NEVER write a boilerplate `[CustomEditor]` or `Editor` class** with `GUILayout.Button` or foldout state solely to show a button or hide a field when Alchemy is present. Use attributes directly on the `MonoBehaviour` or `ScriptableObject`.

---

## 🧭 Fast Lookup Table (Progressive Disclosure)

Open only the specific reference file needed for your task:

| Desired Inspector Feature | Primary Alchemy Attribute(s) | Reference Document |
| :--- | :--- | :--- |
| **Buttons & Action Triggers** | `[Button]`, `[ButtonGroup]` | [`references/attributes-buttons.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-buttons.md) |
| **Conditional Visibility / State** | `[ShowIf]`, `[HideIf]`, `[EnableIf]`, `[DisableIf]` | [`references/attributes-logic.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-logic.md) |
| **Layout, Folding & Tabs** | `[Foldout]`, `[TabGroup]`, `[HorizontalGroup]` | [`references/attributes-layout.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-layout.md) |
| **Input Validation & Warnings** | `[ValidateInput]`, `[Required]`, `[HelpBox]` | [`references/attributes-validation.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-validation.md) |
| **Decorations & Formatting** | `[Title]`, `[LabelText]`, `[Indent]`, `[Blockquote]` | [`references/attributes-decorations.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/attributes-decorations.md) |
| **Deep Serialization & Drawers** | `[ShowInInspector]`, Custom Drawer, `ObjectAdapter` | [`references/custom-drawers.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/custom-drawers.md) |
| **Setup, asmdef & Package Check** | Verification steps & assembly definitions | [`references/setup-and-asmdef.md`](file:///Users/giyu/Documents/Personal_resources/Agent_Skills/gemini/unity-alchemy/references/setup-and-asmdef.md) |

---

## 🚀 Quick Example

```csharp
using UnityEngine;
using Alchemy.Inspector; // Main namespace for attributes

public class LevelManager : MonoBehaviour
{
    [Title("Configuration")]
    [SerializeField] private bool _enableDebugMode;

    [ShowIf(nameof(_enableDebugMode))]
    [SerializeField] private int _debugLogLevel;

    [Foldout("Audio Settings")]
    [SerializeField, Range(0f, 1f)] private float _masterVolume = 1f;

    [Button]
    private void ResetLevel()
    {
        Debug.Log("Level Reset triggered from Inspector!");
    }
}
```
