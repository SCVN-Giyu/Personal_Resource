# Indirect Draw (Minimal Pipeline - Step 1)

## Core Concepts & Mental Model

### Why Indirect Draw?
- **Traditional (`MeshRenderer`)**: CPU loops through every GameObject. For 1,000 rocks, CPU issues 1,000 draw calls: *"Set matrix for Rock 0 -> Draw. Set matrix for Rock 1 -> Draw..."* (Massive CPU bottleneck).
- **Indirect Instancing**: CPU uploads all matrices to the GPU once in a single buffer, then sends **1 single draw call**: *"Draw 1,000 rocks. Here is the buffer. Figure it out yourself."* (Stats show: only 1 draw call for all instances).

### Stride & Buffers
- **`stride`**: The exact size in bytes of a single element in the buffer.
  - A `Matrix4x4` has 16 floats $\times$ 4 bytes = **64 bytes**.
  - Hardware formula: $\text{Address} = \text{Buffer Start} + (\text{instanceID} \times \text{stride})$.
- **`GraphicsBuffer.Target.Structured` (`instanceBuffer`)**:
  - The **Payload / Passenger List**: Stores custom data per object (e.g. 1,000 matrices).
  - Read by: **GPU Shader Cores** during vertex shading.
- **`GraphicsBuffer.Target.IndirectArguments` (`argsBuffer`)**:
  - The **Instruction / Train Ticket**: Exactly 5 numbers (`indexCount`, `instanceCount`, `startIndex`, `baseVertexIndex`, `startInstance`).
  - Read by: **GPU Command Processor** (how many times to repeat the draw).

---

## C# Implementation Steps (with Compute Animation)

1. **Collect Transforms**: Store objects' rest transforms into a CPU `Matrix4x4[]` array (`transform.localToWorldMatrix`).
2. **Allocate Buffers (`Structured`)**:
   - `baseBuffer`: Stores initial/rest transforms uploaded once.
   - `instanceBuffer`: Target buffer written by Compute Shader every frame and bound to the material.
   ```csharp
   int stride = sizeof(float) * 16; // 64 bytes
   baseBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, targets.Length, stride);
   baseBuffer.SetData(matrix);

   instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, targets.Length, stride);
   material.SetBuffer("_InstanceData", instanceBuffer);
   ```
3. **Allocate & Upload Args Buffer (`IndirectArguments`)**:
   ```csharp
   argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
   var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
   args[0] = new GraphicsBuffer.IndirectDrawIndexedArgs
   {
       indexCountPerInstance = mesh.GetIndexCount(0),
       instanceCount = (uint)targets.Length,
       startIndex = (uint)mesh.GetIndexStart(0),
       baseVertexIndex = (uint)mesh.GetBaseVertex(0),
       startInstance = 0
   };
   argsBuffer.SetData(args);
   ```
   > [!WARNING]
   > Do not redeclare `GraphicsBuffer argsBuffer = ...` inside `Start()`, or it shadows the class field, leaving the field `null` in `Update()` and leaking GPU memory on exit.

4. **Compute Dispatch & Render in `Update()`**:
   ```csharp
   // 1. Dispatch Compute Shader to animate instances on GPU
   int threadGroupsX = Mathf.CeilToInt((float)targets.Length / 64f);
   computeShader.SetFloat("_Time", Time.time);
   computeShader.Dispatch(_computeKernel, threadGroupsX, 1, 1);

   // 2. Render Indirect
   RenderParams renderParams = new RenderParams(material);
   renderParams.worldBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
   // CRITICAL for URP: default constructor sets mask to 0, which silently drops the draw call!
   renderParams.renderingLayerMask = GraphicsSettings.defaultRenderingLayerMask;

   Graphics.RenderMeshIndirect(renderParams, mesh, argsBuffer);
   ```
5. **Cleanup in `OnDestroy()` / `OnDisable()`**:
   Always call `baseBuffer?.Release()`, `instanceBuffer?.Release()`, and `argsBuffer?.Release()` to prevent native VRAM leaks.

---

## HLSL Shader Implementation

### Why Standard Shaders Don't Work
Standard URP Lit shaders read one transform from a constant buffer managed by the CPU. They do not know how to sample custom structured buffers per instance.

### The Direct Hardware Instancing Pattern
Rather than relying on `#pragma instancing_options procedural:setup` (which can conflict with modern URP's internal `GetObjectToWorldMatrix()` macro handling in constant buffers), we use explicit hardware `SV_InstanceID` fetching:

1. **Hardware Instance ID in Vertex Input**:
   ```hlsl
   struct Attributes
   {
       float4 positionOS : POSITION;
       float2 uv : TEXCOORD0;
       uint instanceID : SV_InstanceID;
   };
   ```
2. **Structured Buffer**:
   ```hlsl
   StructuredBuffer<float4x4> _InstanceData;
   ```
3. **Explicit World & Clip Space Transformation in Vertex Shader**:
   ```hlsl
   Varyings vert(Attributes IN)
   {
       Varyings OUT;
       float4x4 instanceMatrix = _InstanceData[IN.instanceID];
       float3 positionWS = mul(instanceMatrix, float4(IN.positionOS.xyz, 1.0)).xyz;
       OUT.positionHCS = TransformWorldToHClip(positionWS);
       OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
       return OUT;
   }
   ```
4. **Fragment Semantic Gotcha**:
   Always use singular `half4 frag(Varyings IN) : SV_Target` (`SV_Targets` plural triggers a compilation error on PS 5.0).

---

## Unity Shader Conventions & Quirks
- **`TEXTURE2D(name)` & `SAMPLER(name)`**: Cross-platform macros that expand into native API code (DirectX, Metal, Vulkan).
- **`CBUFFER_START(UnityPerMaterial)`**: Groups material properties in persistent GPU memory for the SRP Batcher.
- **`.hlsl` Include Separation**: Putting structs and logic into an `.hlsl` file and using `#include` inside the `.shader` gives full IDE code completion and lets you create modular, reusable instancing headers.

---

## Architecture Insight: Resident Drawer vs. Compute Indirect Draw

- **GPU Resident Drawer (Unity 6)**:
  - **Best for**: Pre-authored scene `GameObject`s with `MeshRenderer` components (props, rocks, buildings, enemies).
  - **Advantage**: Saves time by handling GPU data persistence, batching, and GPU culling automatically with zero custom shader code.
  - **Limitation**: Cannot handle dense procedural elements because generating hundreds of thousands of scene GameObjects crushes CPU memory and the scene hierarchy.
- **Compute Shader + Indirect Draw**:
  - **Best for**: "Visual on demand" / procedural elements (dense grass fields, foliage splatmaps, crowd systems, debris particles).
  - **How it works**: Instances exist purely as mathematical data in GPU buffers (zero GameObjects). The compute shader simulates, animates, and culls them, updating the dynamic instance count directly on the GPU.

---

## Compute Shader Fundamentals (Practice Bridge)

### 1. Compute Shader Syntax (`.compute`)
```hlsl
#pragma kernel CSMain

StructuredBuffer<float4x4> _BaseMatrices;     // Read-only input
RWStructuredBuffer<float4x4> _ResultMatrices; // Read-write output (RW prefix required)

float _Time;
uint _InstanceCount;

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _InstanceCount)
        return; // Guard against out-of-bounds threads

    float4x4 mat = _BaseMatrices[id.x];
    // Translation column in row-column notation: _m03 (X), _m13 (Y), _m23 (Z)
    mat._m13 += sin(_Time + mat._m03);

    _ResultMatrices[id.x] = mat;
}
```

### 2. C# Dispatch Pattern
```csharp
int kernel = computeShader.FindKernel("CSMain");
computeShader.SetBuffer(kernel, "_BaseMatrices", baseBuffer);
computeShader.SetBuffer(kernel, "_ResultMatrices", instanceBuffer);
computeShader.SetInt("_InstanceCount", targets.Length);
computeShader.SetFloat("_Time", Time.time);

int threadGroupsX = Mathf.CeilToInt((float)targets.Length / 64f);
computeShader.Dispatch(kernel, threadGroupsX, 1, 1);
```

---

## Next Things to Learn (The AAA GPU-Driven Roadmap)

## Milestone 2: GPU Frustum Culling (Completed)

### 1. The Core Mental Model
- **Frustum**: A truncated 3D pyramid defined by 6 planes (Left, Right, Bottom, Top, Near, Far).
- **Bounding Sphere**: Wrap each instance in a sphere (world center + radius).
- **Signed Distance Test**:
  $$\text{Distance} = \text{dot}(\vec{N}, P) + d$$
  - $\vec{N}$ = plane normal (pointing inward), $d$ = plane distance offset.
  - If $\text{Distance} < -R$ for *any* of the 6 planes, the sphere is completely outside the pyramid $\rightarrow$ cull!

### 2. The Indirect Arguments Buffer Structure
An indirect draw call command is laid out as **5 contiguous 32-bit unsigned integers (`uint`)** in GPU memory:
| C# Struct (`IndirectDrawIndexedArgs`) | Raw Offset | HLSL Flat Buffer (`RWStructuredBuffer<uint>`) |
| :--- | :--- | :--- |
| `indexCountPerInstance` | 0 bytes | `_ArgsBuffer[0]` |
| `instanceCount` | 4 bytes | **`_ArgsBuffer[1]`** |
| `startIndex` | 8 bytes | `_ArgsBuffer[2]` |
| `baseVertexIndex` | 12 bytes | `_ArgsBuffer[3]` |
| `startInstance` | 16 bytes | `_ArgsBuffer[4]` |

> [!NOTE]
> - `args[0]` in C# represents the 1st draw call.
> - If rendering multiple submeshes or different mesh types, each requires its own 5-int command slot (`args[0]`, `args[1]`, etc. $\rightarrow$ `_ArgsBuffer[0..4]`, `_ArgsBuffer[5..9]`, etc.).
> - **Golden Rule**: 1 Draw Call = 1 Pipeline State (Material) + 1 Geometry Range (Mesh/Submesh).

### 3. GPU Atomics (`InterlockedAdd`) as a Ticket Dispenser
Rather than separate copy passes, we use hardware atomic operations:
- `_ArgsBuffer[1]` is reset to `0` each frame on the CPU (`args[0].instanceCount = 0; argsBuffer.SetData(args);`).
- Inside the compute shader, thousands of parallel threads execute. When an instance passes the frustum test:
  ```hlsl
  uint writeIndex;
  InterlockedAdd(_ArgsBuffer[1], 1, writeIndex);
  _CulledMatrices[writeIndex] = mat;
  ```
- **The "Queue Choke-Point"**: The GPU hardware forces threads to queue up for that single counter address. Each thread gets a unique ticket (`writeIndex` = 0, 1, 2, ...), densely packing only visible matrices into `_CulledMatrices`.
- The final visible count is left sitting in `_ArgsBuffer[1]`, which `Graphics.RenderMeshIndirect` consumes directly with **zero CPU readback**.

### 4. Scale-Aware Dynamic Bounding Radius
In level design, instances are reused with non-uniform scale and rotation.
- **Base Mesh Radius (CPU)**: `float baseRadius = mesh.bounds.extents.magnitude;` uploaded once as `_BaseRadius`.
- **Dynamic Scale Extraction (GPU)**: The basis vector lengths of the 4x4 matrix correspond to world scale:
  ```hlsl
  float scaleX = length(float3(mat._m00, mat._m10, mat._m20));
  float scaleY = length(float3(mat._m01, mat._m11, mat._m21));
  float scaleZ = length(float3(mat._m02, mat._m12, mat._m22));
  float maxScale = max(scaleX, max(scaleY, scaleZ));
  float dynamicRadius = _BaseRadius * maxScale;
  ```
- Ensures spheres never cull prematurely even when objects are heavily scaled or stretched.

---

### Milestone 3: Dynamic GPU LOD Selection
- **Concept**: Far away rocks should use LOD1/LOD2 meshes instead of full-poly LOD0.
- **Implementation**: In the Compute Shader, compute distance from camera to each instance and append the matrix to the matching LOD argument buffer (`argsBufferLOD0`, `argsBufferLOD1`, etc.).

### Milestone 4: Hi-Z (Hierarchical Z-Buffer) Occlusion Culling
- **Concept**: Don't draw rocks hidden behind mountains, large cliffs, or hills even if they are inside the camera frustum.
- **Implementation**: Downsample the camera depth buffer into a mip chain (Hi-Z pyramid) and test each rock's screen-space bounding box against depth.

---

## Advanced Production Considerations (The Real-World Scaling Problem)
*Saved as advanced study topics for future architectural expansion:*

1. **OBB / AABB vs. Bounding Spheres**:
   - Spheres are fast (1 dot product per plane) but suffer from false-positive visibility on elongated meshes (poles, bridges, walls).
   - Production systems test 8 corners of an Oriented Bounding Box (OBB) transformed to clip space.
2. **Shadow Cascades (CSM) Culling**:
   - Culling only against the Camera Frustum causes disappearing shadows when off-screen objects cast shadows onto the screen.
   - Requires culling instances against separate Directional Light Shadow Cascade frustums.
3. **Spatial Partitioning (Hierarchical / BVH / Octree)**:
   - Linear brute-force compute pass ($O(N)$) scales up to ~50k instances.
   - For open-world foliage (>1,000,000 instances), the GPU tests high-level cluster bounding boxes (chunks) first, culling thousands of instances in a single thread before individual testing.
4. **Wave / Subgroup Atomics (Atomic Contention Mitigation)**:
   - When 500,000 instances hammer `InterlockedAdd` at the same cycle, the memory bus serializes.
   - Solution: Use Wave Intrinsics (`WavePrefixCountBits` / `WaveActiveCountBits`) or `groupshared` memory to aggregate counts per warp/wave, issuing only 1 global atomic add per 32/64 threads.
5. **Multi-Draw Indirect & Bindless**:
   - Combining different meshes and textures into unified geometry buffers and bindless descriptor heaps so entire scenes are dispatched in a single indirect pass.

---

## Ecosystem & Workflow Integration (Living Notes - Subject to Revision)
*Recorded insights on engine architecture boundaries and artistic workflow:*

### 1. GPU Resident Drawer (GRD) in Unity 6 vs. Custom Indirect Pipeline
- **Shared Foundation**: Both systems build on top of `BatchRendererGroup` (BRG) low-level memory persistence.
- **Role Boundary**:
  - **GRD**: Drop-in automation for standard `GameObject`s with `MeshRenderer`s (static props, modular buildings, dynamically spawned enemies/items via `Instantiate`). Handles transform caching, frustum, and occlusion culling automatically.
  - **Custom Indirect Pipeline**: High-density procedural elements & physics (SPH fluids, 500k grass blades, Verlet cloth). Avoids creating millions of scene `GameObject`s that would destroy CPU memory and hierarchy traversal.
- **Runtime Shader Modifications in GRD**:
  - Supported: Global variables (`_Time`, wind), vertex displacement (waves, foliage sway), and shared material changes (`material.SetFloat`).
  - Prohibited: Per-instance `MaterialPropertyBlock` (breaks batching).

### 2. Bridging Shader Graph & Third-Party Shaders to Indirect Draw
- Does indirect draw lock us into writing pure HLSL from scratch? **No!**
- **Shader Graph**: Connect a **Custom Function Node** at the Vertex Stage to sample `_InstanceData[instanceID]` and output `positionWS`. Artists keep full visual styling (PBR, cel-shading, toon outlines).
- **Third-Party / Asset Store Shaders**: Only replace the object-to-world vertex transform hook (`mul(_InstanceData[instanceID], ...)`); all visual lighting, passes, and fragment styling remain intact.

### 3. Per-Instance Material Variation (Dissolve, Fade, Hit-Flash, Tint)
- **Traditional Unity Mistake**: Calling `renderer.material.SetFloat("_Dissolve", val)` clones the material in memory. 100 dying enemies = 100 new material instances = **100 state switches and 100 separate draw calls**.
- **The GPU-Driven / Indirect Solution**: All 100 enemies share the **exact same 1 Material** (1 single draw call!).
  - Instead of putting variables on the Material, pack them into the **`StructuredBuffer`**:
    ```hlsl
    struct InstanceData {
        float4x4 worldMatrix;
        float dissolveProgress; // 0.0 (alive) to 1.0 (dead)
        float4 tintColor;
    };
    StructuredBuffer<InstanceData> _InstanceData;
    ```
  - In the Fragment Shader:
    ```hlsl
    InstanceData data = _InstanceData[IN.instanceID];
    float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, IN.uv).r;
    clip(noise - data.dissolveProgress); // Discards dissolved pixels per instance!
    ```
  - **Result**: 1,000 enemies can each be at a completely different stage of dissolving, burning, or flashing white upon being hit, all drawn in **1 single draw call**!