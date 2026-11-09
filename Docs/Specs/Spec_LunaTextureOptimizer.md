# Spec: Luna Texture Optimizer
> worklog: [WorkLog_LunaTextureOptimizer.md](../WorkLogs/WorkLog_LunaTextureOptimizer.md)

Goal: replace the slow "Build & Estimate → tweak each texture by hand → TinyPNG" loop for Luna playables.
Reusable editor tool (UPM package `com.supercent.luna-texture-optimizer`), not shipped with the playable.
Menus: Supercent → Luna Texture Optimizer, Supercent → Luna Sound Optimizer, Supercent → Luna Size Dashboard.
Milestone 2026-11-02: live size dashboard + faster Luna builds, verified in real builds.

## Requirements Checklist
- [x] **1. Scan** — List every texture a Luna build ships, without building -> [WorkLog#scan](../WorkLogs/WorkLog_LunaTextureOptimizer.md#scan)
- [x] **2. Predict** — Show each texture's size in the playable, matching Luna's real output -> [WorkLog#predict](../WorkLogs/WorkLog_LunaTextureOptimizer.md#predict)
- [x] **3. Compress sources in place** — Local pngquant instead of TinyPNG; Unity Compression None where it helps; backup + restore -> [WorkLog#source-optimize](../WorkLogs/WorkLog_LunaTextureOptimizer.md#source-optimize)
- [x] **4. Per-texture max size** — Resolution override per asset with live preview -> [WorkLog#max-size](../WorkLogs/WorkLog_LunaTextureOptimizer.md#max-size)
- [x] **5. Per-texture format** — PNG best quality / lossy WebP q80 / JPEG q80 (opaque), pick smallest -> [WorkLog#format](../WorkLogs/WorkLog_LunaTextureOptimizer.md#format)
- [x] **6. Write Luna overrides** — Saved settings survive the Luna window and drive later builds -> [WorkLog#luna-write](../WorkLogs/WorkLog_LunaTextureOptimizer.md#luna-write)
- [x] **7. Speed** — Multithreading and caching, no limits since the tool is not in the build -> [WorkLog#speed](../WorkLogs/WorkLog_LunaTextureOptimizer.md#speed)
- [x] **11. Fast Luna compile** — Compile cache + native ARM Mono for Luna's C#->JS step -> [WorkLog#compile-cache](../WorkLogs/WorkLog_LunaTextureOptimizer.md#compile-cache)
- [x] **12. Size dashboard** — Luna's exact size per network without a build: live scripts + textures, every asset and C# file/type, click to locate, module what-ifs -> [WorkLog#size-dashboard](../WorkLogs/WorkLog_LunaTextureOptimizer.md#size-dashboard)
- [x] **13. Fast refresh** — Instant estimate, Auto background refresh, works before the first Luna build -> [WorkLog#size-dashboard-fast](../WorkLogs/WorkLog_LunaTextureOptimizer.md#size-dashboard-fast)
- [x] **14. Faster Luna builds** — Real builds reuse the dashboard's compile (and Brotli for compressing builds) -> [WorkLog#build-reuse](../WorkLogs/WorkLog_LunaTextureOptimizer.md#build-reuse)
- [ ] **15. Reuse minify in builds** — Optional: skip Luna's ~10 s bundle minify in builds -> [WorkLog#terser-reuse](../WorkLogs/WorkLog_LunaTextureOptimizer.md#terser-reuse)
- [ ] **8. Coverage gaps** — Sprite atlases, TMP font textures, HDR / normal maps -> [WorkLog#coverage](../WorkLogs/WorkLog_LunaTextureOptimizer.md#coverage)
- [ ] **9. Custom max size** — Values between the halving steps (offered, not requested yet) -> [WorkLog#max-size](../WorkLogs/WorkLog_LunaTextureOptimizer.md#max-size)
- [ ] **10. Team rollout** — Windows check, other playables, package distribution -> [WorkLog#rollout](../WorkLogs/WorkLog_LunaTextureOptimizer.md#rollout)
- [~] **16. Audio compress / extension switch** — MP3 tuning done; Opus option built and verified in a Luna build (predicted 53,000 B == built .ogg, plays in desktop Chrome); iOS/Android devices untested -> [WorkLog#audio-compress](../WorkLogs/WorkLog_LunaTextureOptimizer.md#audio-compress)
- [~] **17. Texture preview** — built: Luna input vs output side by side under the Texture Optimizer table (zoom, background); UI not clicked yet -> [WorkLog#texture-preview](../WorkLogs/WorkLog_LunaTextureOptimizer.md#texture-preview)
- [~] **18. Asset removal and alert** — force-exclude assets/folders from the build via the Size Dashboard (project untouched), with a blast-radius review of what references them; user can accept broken refs -> [WorkLog#asset-removal](../WorkLogs/WorkLog_LunaTextureOptimizer.md#asset-removal)
- [~] **20. Luna Optimizer hub** — built in UI Toolkit: header + Overview (IMGUI, kept) / Textures / Sounds / Models & Other tabs, table + right details panel with previews (texture compare, waveforms, Unity 3D preview), Used by, Add to Exclusions; awaiting user click-through; one window with tabs (Overview, Textures, Sounds, Models & Other, Scripts & Modules, Exclusions), table + right details panel (preview, settings, Used by, Exclude), global size/save header -> [WorkLog#optimizer-hub](../WorkLogs/WorkLog_LunaTextureOptimizer.md#optimizer-hub)
- [~] **19. Quality-of-life backlog** — undo last save (built: Undo… history in all three windows, UI not clicked yet), bulk edit (built: tick rows + apply bar in both optimizers; presets later), budget bar, bulk edit/presets, post-build check, biggest-wins sort, silence trim, duplicate check, source warnings, A/B listen -> [WorkLog#qol-backlog](../WorkLogs/WorkLog_LunaTextureOptimizer.md#qol-backlog)

*(Status legend: `[ ]` pending | `[~]` in-progress / iterating | `[!]` blocked | `[x]` done)*
