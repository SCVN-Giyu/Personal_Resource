# Alchemy: Buttons & Action Triggers

> Use `[Button]` and `[ButtonGroup]` instead of writing custom `OnInspectorGUI` button handling.

---

## 1. Single Button: `[Button]`

Attach `[Button]` to any method (public, private, or internal) to render an Inspector button.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class Spawner : MonoBehaviour
{
    // Default button (uses method name as label)
    [Button]
    private void SpawnWave()
    {
        Debug.Log("Spawning wave...");
    }

    // Button with custom text label
    [Button("Clear All Spawned Entities")]
    private void ClearAll()
    {
        Debug.Log("Clearing all...");
    }
}
```

---

## 2. Horizontal Button Row: `[ButtonGroup]`

Combine multiple buttons into a single horizontal row.

```csharp
using UnityEngine;
using Alchemy.Inspector;

public class PlaybackController : MonoBehaviour
{
    [Button, ButtonGroup("Playback")]
    private void Play() => Debug.Log("Play");

    [Button, ButtonGroup("Playback")]
    private void Pause() => Debug.Log("Pause");

    [Button, ButtonGroup("Playback")]
    private void Stop() => Debug.Log("Stop");
}
```

---

## 3. Passing Method Parameters

Alchemy automatically creates parameter input fields in the Inspector for methods that take arguments!

```csharp
[Button]
private void DamageTarget(int amount, string reason)
{
    Debug.Log($"Damaged {amount} due to {reason}");
}
```
*When the user clicks the button, the values entered into the generated fields are passed directly into the method invocation.*
