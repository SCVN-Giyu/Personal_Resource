# Anti-Pattern Registry Index

> A directory of curated, framework- and technology-specific anti-pattern files.
> Designed for shared consumption across AI agents (**Gemini**, **Claude**, **GPT / Cursor**, etc.).

---

## 🧭 Active Registries

| Domain / Stack | Registry File | Key Topics & Constraints | Triggers / Tags |
| :--- | :--- | :--- | :--- |
| **Unity** | [`unity.md`](unity.md) | `GetComponent` in hot loops, global scene lookups, `?.` operator on `UnityEngine.Object`, string-based lookups, physics raycast allocation | `#unity #csharp #gamedev` |
| **Playable Luna** | [`playable-luna.md`](playable-luna.md) | C# <= 7.0 language limit, unlisted/unsupported Bridge APIs, size budget, reflection | `#luna #playable-ads #playable-luna` |

---

## 🔗 How Projects Link These Files

Instead of copying rules into every repository, a project's agent instructions (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, or `GEMINI.md`) can reference only the relevant domain files:

```markdown
## Active Anti-Patterns & Constraints
Follow the curated avoid lists for this project:
- [Unity Anti-Patterns](<PERSONAL_RESOURCES_ROOT>/Agent_Skills/gemini/avoid-mistakes/references/unity.md)
- [Playable Luna Constraints](<PERSONAL_RESOURCES_ROOT>/Agent_Skills/gemini/avoid-mistakes/references/playable-luna.md)
```

This prevents context window bloat: an AI assistant in a Unity Luna project only loads `unity.md` and `playable-luna.md`, ignoring irrelevant web or backend registries.
