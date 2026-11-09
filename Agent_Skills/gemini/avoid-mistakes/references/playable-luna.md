# Playable Luna Anti-Patterns & Constraints

> Curated "Should-Be-Avoided" patterns for Unity Playable Ads built with **Luna** (Bridge compiler).
> Cross-agent reference for Gemini, Claude, GPT/Cursor.

---

### Luna: C# Features Newer Than C# 7.0 / 7.3

- ❌ **Avoid:** Using C# 8.0+ features such as nullable reference types (`string?`), switch expressions (`var x = obj switch { ... }`), pattern matching improvements (`is not null`), default interface methods, or asynchronous streams (`IAsyncEnumerable`).
- ✅ **Use Instead:** Stick strictly to C# 7.0/7.3 syntax (standard `switch-case`, explicit null checks, classic interfaces, standard enumerators).
- 💡 **Why / Context:** Luna transcompiles C# into JavaScript / WebAssembly via its custom Bridge transpiler. Modern C# features newer than 7.3 are either unsupported or trigger compilation/transpiler errors during Luna build exports.
- 🏷️ **Scope:** `#luna #csharp #compiler`
- 📅 **Logged:** 2026-09-21

```csharp
// ❌ Bad: C# 8+ switch expression and pattern matching
string label = state switch {
    State.Running => "Run",
    _ => "Idle"
};

// ✅ Good: Compatible C# 7.0 switch statement
string label;
switch (state) {
    case State.Running:
        label = "Run";
        break;
    default:
        label = "Idle";
        break;
}
```

---

### Luna: Unlisted / Unsupported .NET and Unity APIs

- ❌ **Avoid:** Calling .NET or Unity APIs not explicitly marked as supported in the Luna documentation (e.g. `System.Reflection`, `System.Threading.Thread`, LINQ queries with heavy closure allocations, advanced Unity Physics 3D joints, custom compute shaders).
- ✅ **Use Instead:** Use Luna-supported APIs only. Replace reflection with explicit interfaces or direct registrations; replace threads with Luna-friendly coroutines or frame-based timers; replace LINQ with simple `for` loops.
- 💡 **Why / Context:** Unsupported APIs compile fine in the Unity Editor, but fail when building the playable bundle with cryptic Bridge transpilation or runtime JS errors.
- 🏷️ **Scope:** `#luna #api-compatibility`
- 📅 **Logged:** 2026-09-21

---

### Luna: `Component.SendMessage` with `SendMessageOptions` (CS1501)

- ❌ **Avoid:** Calling `SendMessage(methodName, value, SendMessageOptions.DontRequireReceiver)`. Luna's Bridge API surface does not provide the 3-argument overload taking `SendMessageOptions`.
- ✅ **Use Instead:** Use explicit C# interfaces (e.g. `ICollisionListener`, `ILitePhysicsEvents`) to dispatch events directly without reflection.
- 💡 **Why / Context:** Luna compiler reported `CS1501: No overload for method 'SendMessage' takes 3 arguments` (Build `95be844d-0e6b-4796-98f9-b2f090d54c0f`). Reflection-based method discovery is also significantly slower than direct interface dispatch in WebGL.
- 🏷️ **Scope:** `#luna #unity #api-compatibility #compiler`
- 📅 **Logged:** 2026-09-25

```csharp
// ❌ Bad: 3-argument SendMessage not supported in Luna
SendMessage("OnCollisionEnter", evt, SendMessageOptions.DontRequireReceiver);

// ✅ Good: Direct interface dispatch
ILitePhysicsEvents listener = behaviour as ILitePhysicsEvents;
if (listener != null) listener.OnLitePhysicsEvent(evt);
```

---

### Luna: Unity Collider `sharedMaterial` API

- ❌ **Avoid:** Reading `Collider.sharedMaterial` or relying on Unity `PhysicMaterial` APIs in playable runtime code.
- ✅ **Use Instead:** Store the required friction/restitution values in Luna-safe serialized fields or an explicit external-physics configuration object, then pass those numeric values to the JavaScript physics layer.
- 💡 **Why / Context:** Luna's compiler reported `CS1061` for `BoxCollider.sharedMaterial` during build `ec7682b6-5de9-4389-8297-d466b30c234c` (`LogID: 61738e33-aafe-4a2e-9158-e87612e022ef`). The Unity Editor API is not automatically available in the Luna runtime surface.
- 🏷️ **Scope:** `#luna #unity #csharp #physics #compiler`
- 📅 **Logged:** 2026-09-21

```csharp
// ❌ Bad: Editor/Unity API not exposed by Luna's BoxCollider surface
PhysicMaterial material = box.sharedMaterial;

// ✅ Good: Explicit Luna-safe configuration
float friction = defaultFriction;
float restitution = defaultRestitution;
```

---

### Luna: Large Package Size & Dynamic Asset Instantiation

- ❌ **Avoid:** Loading uncompressed high-res textures, unused 3D mesh vertices, or allocating large arrays dynamically at runtime.
- ✅ **Use Instead:** Aggressive texture compression (e.g. ASTC/ETC2 or WebP depending on pipeline), low-poly meshes, and object pooling. Keep total playable ad package strictly within ad network limits (typically 2MB - 5MB compressed).
- 💡 **Why / Context:** Playable ads must load instantaneously on cellular connections across ad networks (IronSource, AppLovin, Unity Ads). Exceeding package size budgets causes ad networks to reject the build.
- 🏷️ **Scope:** `#luna #playable-ads #size-budget`
- 📅 **Logged:** 2026-09-21

---

### Luna / SmashDown Rapier: Verify the Active Physics Adapter's API Coverage

- ❌ **Avoid:** Assuming a Rigidbody API works in the playable because it exists in Unity or Luna's default physics implementation. In particular, do not introduce `Rigidbody.AddForce` without checking the active Rapier adapter's `addLinearForce` implementation.
- ✅ **Use Instead:** Trace the generated Unity wrapper to `Assets/Supercent/RapierPhysics/External/rapier-adapter.js` and verify the exact method and force mode. Add an approved adapter implementation or use an already-supported equivalent with the same physical meaning.
- 💡 **Why / Context:** Intact gravity scaling called `AddForce(..., ForceMode.Acceleration)` successfully in Unity but failed in the exported playable with `this.lNa$.addLinearForce is not a function`. The adapter had `addLinearForceAtPosition` but no `addLinearForce`; checking Luna's default physics runtime alone missed the active backend gap.
- 🏷️ **Scope:** `#luna #unity #rapier #physics #api-compatibility`
- 📅 **Logged:** 2026-10-19

---

### Luna / SmashDown: Pass-Through Nested Coroutine Wrappers

- ❌ **Avoid:** Adding an iterator wrapper that only `yield return`s a child iterator when another coroutine yields that wrapper. Do not assume Unity's full iterator-stack unwinding works identically in the bundled Luna runtime.
- ✅ **Use Instead:** For pass-through methods, return the child `IEnumerator` directly, removing the unnecessary iterator layer. Keep real orchestration in its owning coroutine; never patch generated export files.
- 💡 **Why / Context:** Static inspection of the 2026-10-19 export's `UnityEngine.Coroutine.MoveNext` found that it pops one completed iterator, resumes its parent once, and marks the whole coroutine done if that parent also completes. `Construct -> HolderAssembly.AnimateDimensions -> TableFace.AnimateDimensions` therefore stops after holder resizing instead of returning to block construction. The user reported holder-only animation and blocked input, then confirmed that replacing only the animation coroutines with `Update()` timers and completion flags worked in Luna. The direct-return alternative below was not runtime-tested separately.
- 🏷️ **Scope:** `#luna #coroutines #nested-iterators #stage-loading`
- 📅 **Logged:** 2026-10-19

```csharp
// Avoid a pass-through iterator layer in this Luna runtime.
public IEnumerator AnimateDimensions(Vector3 size)
{
    yield return _face.AnimateDimensions(size, _resizeSeconds, _resizeOvershoot);
}

// Return the actual animation iterator directly instead.
public IEnumerator AnimateDimensions(Vector3 size)
{
    return _face.AnimateDimensions(size, _resizeSeconds, _resizeOvershoot);
}
```
