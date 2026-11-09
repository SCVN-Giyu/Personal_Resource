# Learned Luna Limitations and Useful Notes

[Parent: Luna](README.md)

These findings were carried from prior playable work, mostly with Luna 7.2.0. They are historical evidence, not fresh verification in the consuming project. Recheck version-sensitive details against the installed SDK/runtime. User-confirmed results are distinct from agent-run verification.

## 1. Three-argument SendMessage

- **Confirmed historical compiler failure:** `SendMessage(methodName, value, SendMessageOptions.DontRequireReceiver)` produced `CS1501` because that overload was unavailable.
- **Instruction:** Use direct, strongly typed dispatch instead. Do not assume all Unity overloads exist in Luna.
- **Evidence:** Export build `95be844d-0e6b-4796-98f9-b2f090d54c0f`, recorded 2026-09-25.

## 2. Collider.sharedMaterial is not Renderer.sharedMaterial

- **Confirmed historical compiler failure:** `BoxCollider.sharedMaterial` produced `CS1061`.
- **Instruction:** Keep required friction/restitution values in explicit Luna-safe configuration instead of reading a collider's physics material at runtime.
- **Evidence:** Export build `ec7682b6-5de9-4389-8297-d466b30c234c`.
- **Important distinction:** `Renderer.sharedMaterial` was found in the inspected Luna 7.2 SDK. Do not generalize the collider failure to renderer materials.

## 3. Nested pass-through coroutine wrappers

- **Observed:** A loading coroutine yielded an animation wrapper which only yielded another animation iterator. The holder animated, but construction did not resume and input stayed blocked.
- **Static finding:** The inspected exported scheduler could finish the whole coroutine when consecutive child/parent iterators completed without unwinding the remaining parent stack correctly.
- **User-confirmed solution, 2026-10-19:** Replace the affected animation coroutines with Update-driven timers and completion flags; retain the existing loading/settling coroutines.
- **Untested alternative:** Return the child `IEnumerator` directly from a pass-through method rather than adding a `yield return` wrapper. This alternative was not separately runtime-tested.
- **Instruction:** Avoid unnecessary nested iterator wrappers. Do not infer that all coroutines are unsupported or rewrite unrelated orchestration.

## 4. Orientation selection

- **User-confirmed constraint, 2026-10-15:** Unity orientation-change handling was not supported for the intended playable flow.
- **Instruction:** Determine portrait/landscape using current `Screen.width` and `Screen.height`, not `Screen.orientation`, `DeviceOrientation`, or orientation callbacks.
- Where distinct layouts are needed, author portrait and landscape values and select them from current dimensions. Do not assume Editor orientation behavior transfers to the playable.

## 5. Camera dimension API types

- **Confirmed historical compiler failure, 2026-10-20:** Assigning `camera.pixelHeight` to an integer cache produced `CS0266` in Luna-generated compilation.
- **Solution:** A float cache accepts the Unity integer dimension and the Luna floating-point dimension without narrowing.
- **Instruction:** Check target SDK types as well as member names. Fix the original source rather than decompiled/generated files.
- **Evidence:** Build `1313ff9a-4462-429e-b521-2c1ba6f8f0ef`.

## 6. Camera aspect can lag screen-size changes

- **Static finding in Luna 7.2, 2026-10-20:** Automatic camera aspect was recalculated during rendering, after script Start/Update/LateUpdate. New screen dimensions could be visible alongside the previous camera projection.
- **User-confirmed observation:** Full layout fitting every Update worked, but was extremely expensive. Watching dimensions alone had missed a needed refit.
- **Latest approved solution:** Compare `Screen.width`, `Screen.height`, and the configured camera's `aspect`; refit on change or when no successful fit is cached. Cache only after success, retry failures, and support explicit invalidation/refitting.
- **User result:** The optimization made the playable smoother. This is useful precedent, not proof of correctness for every camera setup.
- **Instruction:** Include dependent camera state in invalidation; do not assume a resize and projection update happen together. Do not force camera settings or run an expensive solver every frame without an agreed need.

## 7. Luna Editor configuration caching

- **Observed, 2026-10-16:** Disk-only changes to `luna.json` external JavaScript entries disappeared when the loaded Luna configuration/UI state wrote its cached values back.
- **Solution:** Synchronize the relevant loaded Luna configuration as well as the disk configuration, using the installed package's actual supported editor mechanism where available.
- **Instruction:** Do not assume a JSON edit is the final configuration while Unity is open. Check the relevant loaded state when entries revert or exports omit expected content. Do not copy another project's private editor implementation blindly.

## 8. Browser cache can masquerade as a rendering defect

- **User-confirmed resolution, 2026-10-18:** A long rendering investigation was resolved by browser cache; earlier apparent material, lighting, and scene failures were stale-build observations.
- **Instruction:** Establish that the browser is loading the intended fresh export before diagnosing a platform rendering defect or changing settings.
- Keep known-good comparisons tied to identified builds. Do not treat stale observations as confirmed engine limitations.

## 9. Fixed-update catch-up floating-point boundary

- **Static risk, not runtime-confirmed, 2026-10-20:** An exported maximum timestep of `0.0199999995529652` was below two JavaScript fixed steps of `0.01`.
- The inspected scheduler capped the entire accumulator before stepping; this combination could allow at most one fixed step per rendered frame instead of the expected two.
- **Instruction:** If investigating slow physics, inspect the actual exported/runtime timestep values and accumulator behavior. Do not assume visually matching decimal settings produce the intended catch-up count.
- Do not change timestep/catch-up settings without approval. This finding did not establish measured impact or a confirmed fix.

## 10. Physical Camera support: positive evidence, not a limitation

- **Static/documentation verification, 2026-10-15:** The installed Luna 7.2 runtime had a physical-camera projection path and SDK members for physical properties, focal length, sensor size, lens shift, and gate fit.
- The official supported-features page also listed Physical Camera support: <https://docs.lunalabs.io/docs/playable/getting-started/supported-features/urp/>.
- **Instruction:** Do not label Physical Camera unsupported without checking the target version. Implementation/documentation presence did not prove export parity for every project setting.

## 11. API audits need signatures and runtime context

- **Static SDK verification, 2026-10-16:** The inspected Luna 7.2 SDK contained declarations for collision contact access, nonalloc overlap queries, Rigidbody sleep/wake and constraints, particle playback/completion, trail clearing/emission, and camera projection APIs.
- Member existence does not prove a particular overload, inherited member, parameter default, field/property shape, or runtime behavior is compatible.
- **Audit lesson:** Account for generic inheritance, optional parameters, and fields when comparing SDK metadata; simplistic comparisons produced false positives.
- Editor-only buttons and test benches do not become browser runtime controls or initialization merely because the associated runtime method exists.

## Evidence provenance

Distilled on 2026-10-21 from the source project's saved observations and the shared Unity/Luna anti-pattern registries. This file contains the portable findings; the consuming agent does not need access to those historical memory records.
