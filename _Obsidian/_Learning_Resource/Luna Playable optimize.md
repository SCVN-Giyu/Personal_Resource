Note: each part can be improved further, this is not the final and hard rule to applied
## 1. Particle as proxy visual
- if there are many objects with the same mesh, same material, we use particle as the proxy visual
- the game object will just contain logical component like transform, collider, v...v -> "Logical game object"
- The particle will be synced to the logical game in component like transform, color, scale,...
-> Result: potentially reduce draw calls per particle group and optimize the rendering part
## 2. Physics
- Set physics steps to 0.02
- May increase the bounce threshold if needed
## 3. Collider Scale Decoupling
- Keep Collider Transform scale at `(1, 1, 1)` to avoid PhysX transform dirtying and shape recalculation overhead.
- **Hierarchy Structure**:
  - Root GameObject: Scale `(1, 1, 1)` holding the Collider (with scale baked into `size` / `radius`) and logic / `Rigidbody`.
  - Child GameObject: Holds visual components (`MeshRenderer`) with the authored local scale.
- **Bake Timing**:
  - Convert at startup initialization (during loading screen) or as an Editor pre-build step to preserve designer workflow.
- **Bake Formulas (World scale caching before reparenting)**:
  - `BoxCollider`:
    - `size = Vector3.Scale(originalSize, lossyScale)`
    - `center = Vector3.Scale(originalCenter, lossyScale)`
  - `SphereCollider`:
    - `radius = originalRadius * Mathf.Max(lossyScale.x, lossyScale.y, lossyScale.z)`
    - `center = Vector3.Scale(originalCenter, lossyScale)`
  - `CapsuleCollider`:
    - `height = originalHeight * lossyScale[directionAxis]`
    - `radius = originalRadius * Mathf.Max(lossyScale[otherAxis1], lossyScale[otherAxis2])`
    - `center = Vector3.Scale(originalCenter, lossyScale)`
- **Constraints**:
  - Sphere and Capsule colliders do not support non-uniform scaling via properties alone.
  - Ensure gameplay scripts reference the root Transform.

##3 Set the compression texture to none