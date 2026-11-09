# Alchemy: Visual Decorations & Formatting

> Polish the visual hierarchy of Inspector fields without boilerplate GUI code.

---

## 1. Headers & Titles: `[Title]`

Adds a styled section header with an optional sub-title.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class GameSettings : MonoBehaviour
{
    [Title("Gameplay Parameters", "Controls global pacing and difficulty")]
    [SerializeField] private float _gameSpeed = 1f;
}
```

---

## 2. Custom Labels: `[LabelText]`

Override the field's displayed label without renaming the serialized C# field.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class PlayerConfig : MonoBehaviour
{
    [LabelText("HP (Health Points)")]
    [SerializeField] private int _hitPoints = 100;
}
```

---

## 3. Formatting Helpers

- `[Indent]`: Indents the field line.
- `[Blockquote]`: Renders a side-bordered quote / note block.
- `[HorizontalLine]`: Inserts a subtle dividing line between sections.

```csharp
[SerializeField] private string _category;

[HorizontalLine]

[Blockquote("Values below are calculated dynamically at runtime.")]
[SerializeField, ReadOnly] private float _currentDps;
```
