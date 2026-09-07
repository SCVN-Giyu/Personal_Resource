open question:

- Does CPU also calculate world matrix for components that do not have rendering features?

## 1. General pipeline

Matrix:

- Matrix math is strictly limited by the rule of linear, fundamental math of the GPU
- in a matrix, the position of the stored data matter completely
- matrix3x3 is only for scale, rotation calculation
- matrix4x4 is for world calculation

the flow is:

**CPU**

- CPU calculate world matrix of each object by the transform component
- CPU calculate the world matrix of the camera
- CPU then inverse the world matrix of the camera to view matrix, which is then will be used to “move the world” so the camera can actually always sit at (0,0,0), V.
    - The view matrix is only calculated in CPU, and will be used by the GPU to offset the vertices. by doing so, the camera will not move, instead, entire world move
- then CPU take camera FOV, near/far plane, aspect, they form the frustum, then calculated the into projection matrix using a special formula.
- CPU then multiply the View and Projection Matrix together once per frame
- CPU then write instruction for shader programing, material switch, texture assign…
- Finally, CPU pack all information together into a draw call, send to GPU

**GPU**

- GPU receive the draw call

## 2. GPU Instancing
- CPU prepares a **buffer of per-instance data** (transform, and optionally color/material params/anything else that varies) — one entry per instance.
- CPU issues **one draw call** referencing that buffer and a count N.
- GPU runs the **same vertex/pixel shader program** for all N instances, but each instance's shader invocation reads its _own_ row from that buffer (usually via `SV_InstanceID` indexing into the buffer). So the _code_ is identical per instance, but the _data_ differs per instance — that's what lets 10,000 rocks look different (rotated, scaled, tinted) despite one draw call.

So: same shader, same draw call, _different data per invocation_, pulled by index. That distinction matters once you get into compute-shader culling, because that's exactly the pattern being used there too.

## 3. Optimization CPU/GPU flow
### The core principle: CPU decides _what_, GPU decides _how_

The CPU's job in a big-simulation game is almost entirely **decision-making and data preparation**. The GPU's job is **bulk parallel execution on data it's already been handed**. The moment you find your CPU doing per-object _repetitive numeric work_ at scale (transform math, similar physics checks, particle updates), that's a signal it could move to the GPU.

### What stays on CPU

- **Gameplay logic** — AI decision trees, state machines, quest/scripting logic, input handling. Inherently branchy, hard to parallelize well on GPU.
- **Simulation authority** — physics _decisions_ (collision response, constraint solving for a reasonable number of dynamic bodies), navigation/pathfinding.
- **Visibility & LOD decisions** — frustum/occlusion culling, choosing which LOD each object uses. Traditionally CPU, though this is exactly the part now migrating to GPU (see below).
- **World streaming logic** — deciding which chunks/cells load or unload based on player position, memory budget.
- **Draw call organization** — sorting by material/shader, building instance batches, populating command buffers.

The trick for high object counts on CPU: **don't process objects one at a time with cache-unfriendly OOP code.** This is where ECS (Entity Component System) architecture matters — packing data of the same type contiguously (Structure-of-Arrays) so the CPU can burn through thousands of transforms/AI updates cache-efficiently, and so the work can be trivially split across threads with a job system (Unity DOTS/ECS, Unreal's Mass Entity, or a custom job scheduler). This alone is often a bigger win than anything else you'll do, because most CPU slowdown in big-object-count games is cache misses and single-threaded logic, not raw instruction count.

### What moves to GPU

- **All per-vertex/per-pixel work** — obviously, but worth naming: skinning (skeletal animation) can be done in a vertex/compute shader instead of CPU.
- **Particles / VFX at scale** — GPU particle simulation (compute shader driven) instead of CPU-updated particle systems, once you're past a few thousand particles.
- **Instanced rendering** — if you have 10,000 rocks/trees/grass blades, you do NOT issue 10,000 draw calls. You issue one instanced draw call and let the GPU place copies using a buffer of transforms. This is probably the single highest-leverage optimization for "high object count."
- **GPU-driven culling and LOD selection** — this is the modern move (Nanite, indirect draws, GPU-driven pipelines). Instead of the CPU looping through every object to test against the frustum, you upload bounding data once and run a compute shader that does the culling and _writes the draw commands itself_, which the GPU then executes via `DrawIndirect`/`multi-draw-indirect`. The CPU's job shrinks to "kick off this compute pass," not "loop over 100k objects."
- **Terrain/vegetation generation** — procedural placement, tessellation, displacement — GPU compute/tessellation shaders.
## Inside a fragment, what data is stored? how is it processed?
