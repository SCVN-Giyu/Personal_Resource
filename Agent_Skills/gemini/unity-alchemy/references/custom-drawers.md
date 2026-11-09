# Alchemy: Properties & Custom Drawers

> How to inspect non-serialized properties and extend Alchemy when standard attributes aren't enough.

---

## 1. Showing C# Properties: `[ShowInInspector]`

Unity normally serializes and displays only fields (`[SerializeField] private ...` or `public ...`).
With Alchemy, you can display C# auto-properties or calculated properties in the Inspector:

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class ScoreTracker : MonoBehaviour
{
    [SerializeField] private int _baseScore = 50;
    [SerializeField] private float _multiplier = 1.5f;

    // Displayed in Inspector at runtime or edit mode
    [ShowInInspector]
    public float FinalScore => _baseScore * _multiplier;
}
```

---

## 2. When to Write a Custom Drawer

If you need completely custom rendering or bespoke UI Toolkit visual trees for a specific struct/class:

1. Rather than writing a monolithic `[CustomEditor(typeof(MyMonoBehaviour))]` which replaces the entire Inspector, create an **Alchemy Property Drawer** for that specific field or attribute.
2. In an Editor assembly (`Editor/` folder):

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using Alchemy.Editor;

[CustomAttributeDrawer(typeof(MyCustomAttribute))]
public class MyCustomAttributeDrawer : AlchemyAttributeDrawer
{
    public override VisualElement CreateElement(SerializedProperty property, VisualElement target)
    {
        var root = new VisualElement();
        // Construct UI Toolkit elements
        return root;
    }
}
```

---

## 3. Extending the Entire Object Inspector: `AlchemyEditor`

If an entire component truly requires custom UI Toolkit visual tree injection while still preserving Alchemy's attribute drawers:

Inherit from `AlchemyEditor` instead of `UnityEditor.Editor`:

```csharp
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;
using Alchemy.Editor;

[CustomEditor(typeof(MyComplexComponent))]
public class MyComplexComponentEditor : AlchemyEditor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = base.CreateInspectorGUI(); // Preserves all Alchemy attribute drawers!
        
        // Append additional custom UI Toolkit elements
        var extraBanner = new Label("Custom Banner Element");
        root.Add(extraBanner);

        return root;
    }
}
#endif
```
*(This ensures you don't lose `[Button]`, `[ShowIf]`, or `[Foldout]` on the component!)*
