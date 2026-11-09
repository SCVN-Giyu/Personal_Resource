# Unity Anti-Patterns & Constraints

> Curated "Should-Be-Avoided" patterns for Unity development in C#.
> Cross-agent reference for Gemini, Claude, GPT/Cursor.

---

### Unity: GetComponent or Find in Hot Loops (`Update`, `LateUpdate`, `FixedUpdate`)

- ❌ **Avoid:** Calling `GetComponent<T>()`, `GetComponentInChildren<T>()`, `GameObject.Find()`, or `GameObject.FindWithTag()` inside `Update()`, `FixedUpdate()`, or frequent loop iterations.
- ✅ **Use Instead:** Cache component and object references in `Awake()` or `Start()`, or assign them directly in the Inspector via `[SerializeField] private T _component;`.
- 💡 **Why / Context:** `GetComponent` has internal native-to-managed marshaling overhead and dictionary lookup cost; `GameObject.Find` performs an exhaustive scene graph search ($O(N)$). Running these every frame creates massive CPU stalls and mobile thermal throttling.
- 🏷️ **Scope:** `#unity #csharp #performance`
- 📅 **Logged:** 2026-09-21

```csharp
// ❌ Bad: Lookup every frame
void Update() {
    var rb = GetComponent<Rigidbody>();
    rb.velocity = Vector3.forward * speed;
}

// ✅ Good: Cached reference
[SerializeField] private Rigidbody _rigidbody;

void Awake() {
    if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
}

void Update() {
    _rigidbody.velocity = Vector3.forward * speed;
}
```

---

### Unity: Null-Conditional Operator (`?.`) on `UnityEngine.Object`

- ❌ **Avoid:** Using `myUnityObject?.SomeMethod()` or `myUnityObject?.transform` on types derived from `UnityEngine.Object` (e.g. `MonoBehaviour`, `GameObject`, `Transform`, `Component`).
- ✅ **Use Instead:** Explicit null check: `if (myUnityObject != null) { myUnityObject.SomeMethod(); }`.
- 💡 **Why / Context:** `UnityEngine.Object` overrides the `==` and `!=` operators to check whether the underlying C++ native object is destroyed or pseudo-null. The C# null-conditional operator `?.` bypasses custom operator overloads and checks only for pure managed `null`, causing `NullReferenceException` or operating on dead/destroyed native objects.
- 🏷️ **Scope:** `#unity #csharp #pitfall`
- 📅 **Logged:** 2026-09-21

```csharp
// ❌ Bad: Bypasses Unity's custom lifetime check
targetComponent?.TriggerAction();

// ✅ Good: Respects Unity's overloaded == operator
if (targetComponent != null) {
    targetComponent.TriggerAction();
}
```

---

### Unity: String-Based Method Invocation and Coroutines

- ❌ **Avoid:** Using `SendMessage()`, `BroadcastMessage()`, or `StartCoroutine("MethodName")`.
- ✅ **Use Instead:** Strongly-typed direct method calls, events/interfaces, or `StartCoroutine(MethodName())`.
- 💡 **Why / Context:** String-based invocations rely on slow reflection, fail silently if method names change during refactoring, and cannot be caught at compile-time.
- 🏷️ **Scope:** `#unity #csharp #refactoring`
- 📅 **Logged:** 2026-09-21

---

### Unity: Runtime Auto-Adding Gameplay Components During Map Loading

- ❌ **Avoid:** Calling `AddComponent<T>()` from a map loader or stage-loading path to inject gameplay components onto loaded block objects.
- ✅ **Use Instead:** Author required components directly on prefabs or scene objects, expose their settings in the Inspector, and let the component register itself through its lifecycle.
- 💡 **Why / Context:** Runtime injection hides required prefab configuration, bypasses Inspector-authored settings, and makes stage behavior depend on implicit loader side effects.
- 🏷️ **Scope:** `#unity #csharp #architecture #playable-luna`
- 📅 **Logged:** 2026-09-22

```csharp
// ❌ Bad
if (block.GetComponent<BlastBodySettings>() == null)
    block.gameObject.AddComponent<BlastBodySettings>();

// ✅ Good
// Add BlastBodySettings to the prefab/scene object and let its lifecycle register it.
```

---

### Unity: Global Scene Object Discovery in Runtime Code

- ❌ **Avoid:** Using `FindObjectOfType`, `FindObjectsOfType`, `FindFirstObjectByType`, `FindAnyObjectByType`, or equivalent global scene searches in playable runtime code.
- ✅ **Use Instead:** Inject references through serialized fields, explicit bootstrap wiring, or a documented singleton such as `Playable009BlockLifecycle.Instance`.
- 💡 **Why / Context:** Global searches hide dependencies, make initialization order fragile, and can silently bind the wrong object when multiple stage systems are loaded. Playable-009 now uses explicit scene references plus a persistent lifecycle singleton.
- 🏷️ **Scope:** `#unity #csharp #architecture #playable-luna`
- 📅 **Logged:** 2026-09-22

```csharp
// ❌ Bad
_lifecycle = FindObjectOfType<Playable009BlockLifecycle>();

// ✅ Good
_lifecycle = Playable009BlockLifecycle.Instance;
```

---

### Unity / SmashDown: Visual Scripting

- ❌ **Avoid:** Unity Visual Scripting or node-graph-authored behavior, initialization, and tools in the SmashDown project.
- ✅ **Use Instead:** Explicit C# scripts and direct, strongly typed component commands.
- 💡 **Why / Context:** The user explicitly forbids visual scripting. Configuration assets and custom C# Inspectors are allowed; executable node graphs are not.
- 🏷️ **Scope:** `#unity #csharp #smashdown #architecture`
- 📅 **Logged:** 2026-10-12

```csharp
// ❌ Bad: author the break sequence in a Visual Scripting graph.

// ✅ Good: invoke the C# presentation coordinator.
block.PlayBreakPresentation();
```

---

### Unity Inspector: Boilerplate Custom Editor Scripts vs Declarative Alchemy

- ❌ **Avoid:** Writing standalone `[CustomEditor]` or IMGUI `OnInspectorGUI` scripts solely to draw buttons, group fields into foldouts, or conditionally show/hide properties when Alchemy is installed.
- ✅ **Use Instead:** Check if `com.annulusgames.alchemy` is present. If installed, use declarative Alchemy attributes directly on fields and methods (`[Button]`, `[ShowIf]`, `[Foldout]`, `[HorizontalGroup]`, `[ValidateInput]`). If custom editor code is genuinely required, inherit from `AlchemyEditor` instead of `UnityEditor.Editor` to retain attribute support.
- 💡 **Why / Context:** Handwritten `CustomEditor` scripts create immense boilerplate, break standard serialization undo/redo if not carefully implemented, duplicate layout code across files, and require dedicated `Editor/` assemblies. Alchemy gives UI Toolkit-backed inspector enhancements with zero boilerplate.
- 🏷️ **Scope:** `#unity #csharp #editor #alchemy #ui`
- 📅 **Logged:** 2026-10-13

```csharp
// ❌ Bad: 20+ lines of custom editor boilerplate just for a button and foldout
[CustomEditor(typeof(LevelSpawner))]
public class LevelSpawnerEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();
        if (GUILayout.Button("Spawn Level")) {
            ((LevelSpawner)target).Spawn();
        }
    }
}

// ✅ Good: One declarative attribute on the MonoBehaviour method
public class LevelSpawner : MonoBehaviour {
    [Button]
    private void Spawn() { ... }
}
```

---

### Unity / SmashDown: Static State with Domain Reload Disabled

- ❌ **Avoid:** Assuming static fields, singleton references, caches or event handlers reset automatically between Play sessions. Do not rely solely on static field initializers.
- ✅ **Use Instead:** Check Enter Play Mode settings whenever creating or changing static state. Explicitly reset session-owned statics/handlers before scene initialization, using `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)` where appropriate. With Scene Reload enabled, authored singleton instances can rebind in Awake. If Scene Reload is also disabled, separately handle rebinding and instance-state reset; do not assume Awake runs again.
- 💡 **Why / Context:** This project's Domain Reload is disabled (user-confirmed 2026-10-15); current EditorSettings disables domain reload only, leaving scene reload enabled. Statics otherwise survive repeated Play sessions, causing stale singleton endpoints or duplicate subscriptions. Preserve intentional same-session teardown access, but reset it before the next session. Editor-only reset hooks may be guarded by `UNITY_EDITOR` to avoid adding playable-runtime API requirements. Do not enter Play Mode or run tests/builds without user authorization.
- 🏷️ **Scope:** `#unity #csharp #smashdown #domain-reload #singleton #static-state`
- 📅 **Logged:** 2026-10-15

```csharp
// Editor-only session reset; authored instance rebinds in Awake with Scene Reload enabled.
#if UNITY_EDITOR
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetInstance()
{
    _instance = null;
}
#endif
```

---

### Unity / SmashDown: Immediate TNT Detonation Bypassing Chain Timing

- ❌ **Avoid:** Immediately detonating TNT on a direct projectile hit or a rocket-area hit, or proposing a direct-hit exception to chain timing.
- ✅ **Use Instead:** Route every TNT explosion through delayed chain timing, including the initially hit TNT. Preserve the existing floor-contact destruction-without-blast rule unless the user changes it. Clarify the initial delay rule before changing code; distance / propagation speed alone gives zero delay at zero distance.
- 💡 **Why / Context:** The user explicitly reaffirmed that no TNT should detonate immediately; nearby TNT exploding simultaneously is not the intended chain behavior. This is the intended rule, not a claim that the current implementation follows it.
- 🏷️ **Scope:** `#unity #csharp #smashdown #tnt #chain-timing`
- 📅 **Logged:** 2026-10-19

---

### Unity / SmashDown: Turning a Behavior Migration into a Redesign

- ❌ **Avoid:** Adding new rendering systems, lifetime frameworks, cleanup deadlines or gameplay changes to a behavior migration merely because source inspection suggests improvements.
- ❌ **Avoid:** Copying a global table of every block family's water materials onto each logical template. A template may serve several recipes, but each assembled visual needs only its selected material.
- ✅ **Use Instead:** Store the water material alongside the selected intact visual in the recipe and pass that single reference to the water behavior. Remove the redundant runtime lookup and authoring tables together.
- ✅ **Use Instead:** Preserve the reference flow and reuse existing content. For the water migration, use the old water material; ground/water are destruction-contact surfaces; retain the same block/body through the existing sinking duration, then dispose through `DestructionFactory`. Make only integration changes required by the agreed behavior boundaries and immediate water-destruction counting.
- 💡 **Why / Context:** The user corrected a migration spec that introduced unnecessary rendering and lifetime redesign. Destruction counting is not presentation cleanup; stage transitions must not prematurely remove sinking blocks. Do not invent a Break-completion handshake: destruction and gameplay retirement finish immediately, while existing visual routines/timers handle later presentation and disposal.
- 🏷️ **Scope:** `#unity #smashdown #migration #water #behavior-parity`
- 📅 **Logged:** 2026-10-19
