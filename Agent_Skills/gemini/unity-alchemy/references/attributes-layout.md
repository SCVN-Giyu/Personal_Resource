# Alchemy: Layout & Grouping

> Clean up complex Inspectors into foldouts, tabs, and columns without writing manual GUI layout code.

---

## 1. Foldouts: `[Foldout]`

Collapse groups of related variables under a collapsible header.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class CharacterStats : MonoBehaviour
{
    [Foldout("Movement Parameters")]
    [SerializeField] private float _walkSpeed = 5f;
    [Foldout("Movement Parameters")]
    [SerializeField] private float _runSpeed = 10f;
    [Foldout("Movement Parameters")]
    [SerializeField] private float _jumpForce = 7f;

    [Foldout("Combat Parameters")]
    [SerializeField] private int _baseHealth = 100;
    [Foldout("Combat Parameters")]
    [SerializeField] private int _armor = 10;
}
```

---

## 2. Tabs: `[TabGroup]`

Create tabbed views to switch between sub-configurations within the same Inspector window.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class WeaponItem : MonoBehaviour
{
    [TabGroup("Weapon", "General")]
    [SerializeField] private string _weaponName;

    [TabGroup("Weapon", "General")]
    [SerializeField] private Sprite _icon;

    [TabGroup("Weapon", "Damage")]
    [SerializeField] private float _damage;

    [TabGroup("Weapon", "Damage")]
    [SerializeField] private float _critMultiplier;
}
```

---

## 3. Horizontal Columns: `[HorizontalGroup]`

Place fields side-by-side on the same row.

```csharp
public class RangeBounds : MonoBehaviour
{
    [HorizontalGroup("Range")]
    [SerializeField] private float _min;

    [HorizontalGroup("Range")]
    [SerializeField] private float _max;
}
```
