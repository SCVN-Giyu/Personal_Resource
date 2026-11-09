# Alchemy: Conditional Logic & Visibility

> Control field visibility and interactability dynamically without writing CustomEditor code.

---

## 1. Visibility: `[ShowIf]` & `[HideIf]`

Show or hide fields depending on boolean fields, properties, or methods.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class AttackConfig : MonoBehaviour
{
    [SerializeField] private bool _isRanged;

    // Shows only if _isRanged is true
    [ShowIf(nameof(_isRanged))]
    [SerializeField] private float _projectileSpeed = 10f;

    // Hides if _isRanged is true
    [HideIf(nameof(_isRanged))]
    [SerializeField] private float _meleeSwingRadius = 1.5f;
}
```

### Checking Enums or Values
You can compare enum values or specific equality:

```csharp
public enum AttackType { Melee, Ranged, Magic }

public class SkillConfig : MonoBehaviour
{
    [SerializeField] private AttackType _type;

    [ShowIf(nameof(_type), AttackType.Magic)]
    [SerializeField] private int _manaCost;
}
```

---

## 2. Interactability: `[EnableIf]` & `[DisableIf]`

Keep the field visible in the Inspector, but toggle whether it is grayed-out (disabled) or editable.

```csharp
[SerializeField] private bool _overrideGlobalSettings;

[EnableIf(nameof(_overrideGlobalSettings))]
[SerializeField] private float _customTickRate = 0.02f;

[DisableIf(nameof(_overrideGlobalSettings))]
[SerializeField] private string _info = "Using project defaults";
```

---

## 3. Method-Based Conditions

You can use a parameterless method returning a `bool` as the condition:

```csharp
[SerializeField] private Transform _target;

[ShowIf(nameof(HasValidTarget))]
[SerializeField] private float _stoppingDistance = 2f;

private bool HasValidTarget()
{
    return _target != null;
}
```
