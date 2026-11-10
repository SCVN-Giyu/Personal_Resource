# WorkLog: Luna Texture Optimizer

```yaml
meta:
  spec_doc: ../Specs/Spec_LunaTextureOptimizer.md
  package: "/Users/giyu/Documents/Personal_resources/Unity_Packages/com.supercent.luna-texture-optimizer (moved 2026-11-06; old /Volumes/Supercent/Unity Packages/… path is a symlink to it). Personal_resources is a git repo (not committed by agent)"
  first_project: "luna_themeparkmanager (Unity 2022.3.19f1), manifest: file:/Users/giyu/Documents/Personal_resources/Unity_Packages/com.supercent.luna-texture-optimizer"
  link_skill: "Personal_resources/Agent_Skills/gemini/luna-optimizer-link (SKILL.md + scripts/link_luna_optimizer.py; ~/.claude/skills -> Agent_Skills/gemini). Links any Unity 2022.3+ project live; --unlink, --dry-run"
  luna_version: "7.2.0 (/Volumes/Supercent/Unity Packages/Luna Playable V7.2.0)"
  active_focus: optimizer-hub (all 4 steps built 2026-11-06)   # user click-through of Textures/Sounds/Models tabs + Exclusions; then backlog (presets, budget bar per tab, Overview UIT optional)   # spike done 2026-11-06 (excluded texture -> white, silent). Waiting on user: undo + bulk click tests, Opus on Safari/devices
  updated: 2026-11-10 (simple-mode-ux built: Simple/Manual modes, editable sound trim, leaner columns)
```

## Tree Overview
```text
LunaTextureOptimizer
├── luna-internals [done]        (reference: how Luna exports, compresses, stores settings)
├── scan [done]
├── predict [done]
├── source-optimize [done]
│   └── apply-restore-ui [done, UI not clicked by agent]
├── max-size [done]
│   └── custom [pending]
├── format [done]
├── luna-write [done]
├── speed [done]
├── window [done]
├── script-injection-spike [done: low gain]
├── quality-search [pending]
├── compile-cache [done, verified in real builds]
├── size-dashboard [done]          (Overview tab of Luna Optimizer)
│   ├── scene-estimate [built 2026-11-06]  (sizes for a scene that was never built: memory + estimator)
│   ├── live [done]                (scripts compiled in sandbox, textures predicted, per network)
│   ├── fast [done]                (instant estimate, Auto, incremental sandbox, no-build mode)
│   ├── scene-roots [done]         (current scenes decide compiled scripts; single-run lock)
│   └── ui-lists [done]            (all assets/scripts, filter, click-to-locate; UI not clicked by agent)
├── build-reuse [done, verified]   (real builds HIT the dashboard's compile)
├── terser-reuse [pending]         (~10 s per build; needs NODE_OPTIONS injection)
├── coverage [pending]
├── rollout [pending]
├── audio-compress [iterating]        (Opus BGM built + plays in desktop Chrome; devices untested)
│   ├── mp3-tuning [done, UI not clicked by agent]  (Supercent/Luna Sound Optimizer)
│   └── ogg-switch [built: needs real Luna build + device check]
├── texture-preview [built, user: very good]
├── asset-removal [in-progress]    (force-exclude assets/folders from the build + blast-radius review)
│   ├── luna-exclude-spike [done]
│   ├── ref-graph [done, engine only]   (chain from asset up to the build scene, field-level)
│   ├── review-ui [replaced by exclusion-window]
│   ├── exclusion-window [built: UI not clicked]   <-- ACTIVE FOCUS
│   ├── exclude-store [built]
│   ├── entry-points [built: UI not clicked]
│   ├── excluded-list [built: UI not clicked]
│   └── build-verify [pending]       (real build: asset gone, game still runs)
├── optimizer-hub [in-progress]        (one "Luna Optimizer" window: tabs + table/details layout; replaces the 3 windows + review pop-up)
│   ├── shell [built: UI not clicked]   (hub window, tabs hosting existing views, global header)
│   ├── uitk-frame [built: UI not clicked]  (hub frame in UI Toolkit hosting IMGUI tabs)
│   ├── master-detail [done: Textures, Sounds, Models & Other in UIT; Overview kept IMGUI by user request]  (per tab: MultiColumnListView + TwoPaneSplitView details; Textures first)
│   ├── previews [done]          (texture compare, audio waveform+play, Unity interactive preview for models/prefabs/materials, thumbnail fallback)
│   ├── used-by-inline [done]    (Used-by chain + Add to Exclusions in every details panel; queued rows badged; Exclusions window)
│   └── simple-mode-ux [built 2026-11-10, not opened in Unity]  (Simple/Manual modes, leaner columns, editable sound trim, restyle)  <-- ACTIVE
├── shared-package [done 2026-11-06]  (moved to Personal_resources + link skill)
└── qol-backlog [in-progress]      (QoL ideas for both tools)
    ├── undo-last-save [built: UI not clicked]
    ├── bulk-edit [built: UI not clicked]
    └── presets [pending]                        (after user has used bulk edit)
```

---

## Nodes

### `luna-internals` {#luna-internals}
```yaml
state: done
source: "Obfuscated. Editor: scripts/Editor/EditorScripts.dll. Pipeline: pipeline/Jakefile.js"
decompile: "ICSharpCode.Decompiler + Unity Managed refs; strings decrypted under mono via class 008DE65C-...; Jakefile decoded by running its string-array prelude in node vm"
editor_export: "Copies the source file only for .png + TextureType Default + not NPOT-scaled. Otherwise reads Unity's IMPORTED texture: raw copy for uncompressed formats, else Blit to RGBA32 -> EncodeArrayToPNG (carries ETC/ASTC/DXT artifacts)"
output_ext: "webp if rule webp; jpeg if rule jpeg OR imported format DXT1/RGB24 OR alphaSource None; else png -> depends on active build target"
magick: "png: png32: + -filter Mitchell -interpolate average16 -interpolative-resize WxH>; jpeg: -resize + -quality Q%; webp: -define webp:lossless=true. maxWidth/maxHeight <= 0 -> 100%"
second_step: "quantQuality: pngquant --force --strip --quality 1-75; quantSize: --force --quality=1-65 --speed=1 --strip; cwebp -q Q -alpha_q 100 -m 6; pngcrush; jpeg skipped; linear + non-sRGB quant -> pngcrush; rule.script bypasses compression"
size_breakdown: "(file + ~120 B json sidecar) * 4/3 (base64) or * 8/log2(122) (base122). Rows labeled by source path, so JPEG output still shows .png"
settings_store: "Only luna.json. Static holder class (static field of type Luna.Unity.EditorScripts.Interface.LunaData) keeps live LunaData + cached file text (first static string) + change Action. Save = JsonUtility.ToJson(data, true), written only if != cache"
format_tables: "textureFormats: png8 0, png24 1, png32 2, jpeg 3, webp 4, default 5. textureCompressions: quantQuality 0, quantSize 1, crush 2, none 3"
constraints: "Editing luna.json directly is overwritten by Luna's in-memory copy on its next save"
```

### `scan` {#scan}
```yaml
spec_ref: "Spec#1 Scan"
state: done
scripts: [BuildTextureScanner.cs, LunaSettings.cs, LunaBuildStats.cs, TextureEntry.cs, LunaInstall.cs]
solution: "Roots = Luna scenes + includes + Assets/**/Resources/ (non-Editor) when export has resources. Walk GetDependencies(path, false) without entering MonoScripts. Apply Luna exclude globs. Rule lookup ported from Luna (exact < *substring < ^regex; longer prefix first)"
contracts: "BuildTextureScanner.Scan(root, settings, stats) -> List<TextureEntry>; CreateEntry(root, path, settings, stats) -> null if not TextureImporter; LunaSettings.RuleFor(path, out isOverride); LunaInstall.Find() reads tool paths from Luna's config.json"
rejected: "AssetDatabase.GetDependencies(recursive) -> pulls script editor icons (TMP gizmo .psd)"
verified: "8/8 textures and output types match Luna's last build stats"
```

### `predict` {#predict}
```yaml
spec_ref: "Spec#2 Predict"
state: done
scripts: [LunaPipeline.cs, LunaInputExporter.cs, ProcessRunner.cs, PredictionCache.cs]
solution: "Run Luna's own tools (from the Luna install) with Luna's exact arguments on an input exported the way Luna's editor does"
contracts: "LunaPipeline.Predict(entry, install, root, cache?); PredictMany(entries, install, root, cache, progressTitle) -> LunaPrediction[] (null = canceled); LunaPrediction.PlayableBytes = (outputBytes + 120) * 4/3"
constraints: "JPEG decision must use the IMPORTED TextureFormat (DXT1/RGB24), not source alpha. Prediction depends on active build target"
verified: "PNG/JPEG byte-identical to Luna stage1/stage2; in-playable size within ±11 B of Size Breakdown; WebP confirmed by user rebuild (exact match)"
```

### `source-optimize` {#source-optimize}
```yaml
spec_ref: "Spec#3 Compress sources in place"
state: done
scripts: [SourceOptimizer.cs]
solution: "Candidates per PNG: current, pngquant (dither), pngquant --nofs, each +/- Unity Compression None when Unity compression affects Luna. Judged by Luna's predicted output, not file size. Winner needs >= 256 B and >= 2% saving"
contracts: "EvaluateMany(entries, install, root, settings, cache) -> List<OptimizationResult> (null = canceled); Apply(result, root); RestoreAll(root); HasBackups(root)"
constraints: "Backup file + .meta to Library/LunaTextureOptimizer/Backup/<assetPath> before first change. Temp imports live in Assets/__LunaTextureOptimizerTemp and are deleted in finally. Ask before Apply"
rejected:
  - "Luna custom-script passthrough (ship source bytes) -> only helps pre-optimized sources; Luna window erased the script field"
  - "Opaque PNG -> JPEG source pre-conversion -> Luna's JPEG output got 6-7% bigger (img01 31.2 vs 33.3 KB)"
  - "TinyPNG API -> chose local open-source CLIs"
verified: "Evaluate non-destructive (checksums), temp folder removed. Apply/Restore code path not run on real assets by agent"
```

### `max-size` {#max-size}
```yaml
spec_ref: "Spec#4 Per-texture max size"
state: done
scripts: [LunaTextureOptimizerWindow.cs, TextureEntry.cs]
solution: "Max size popup: Full, then halving steps down to 32; labels show real output px. Box is square: ImageMagick WxH> keeps aspect ratio, never upscales"
contracts: "TextureEntry.LunaInputSize (source px if Luna reads the file, else imported px); OutputSizeFor(maxW, maxH)"
decisions: "Separate W/H values change nothing about shape; they only give finer steps"
├── custom:
│     spec_ref: "Spec#9 Custom max size"
│     state: pending
│     next: "Add 'Custom…' option: type output width OR height, other side from aspect ratio; preview px + KB before save"
```

### `format` {#format}
```yaml
spec_ref: "Spec#5 Per-texture format"
state: done
scripts: [LunaTextureOptimizerWindow.cs]
solution: "Format popup: PNG best quality (png32 + quantQuality), WebP q80, JPEG q80 (no-alpha only). Label shows '(ships as X)' when Luna forces another type. 'Pick smallest format' predicts all options in one parallel pass and selects the smallest (preview only)"
decisions: "Best-quality PNG preset only, never quant small (user). Lossy WebP accepted incl. iOS 14+ requirement"
findings: "WebP wins on large soft images (cloud, T_Player); small flat UI icons stay smaller as PNG"
```

### `luna-write` {#luna-write}
```yaml
spec_ref: "Spec#6 Write Luna overrides"
state: done
scripts: [LunaSettingsWriter.cs]
solution: "Mutate Textures.overridesDictionary[path] in Luna's in-memory LunaData (maxWidth, maxHeight, quality, currentFormat, currentCompression), serialize, write file, set cached text, invoke change Action. Missing override -> new TextureOverride copied from the rule that matches today"
contracts: "LunaSettingsWriter.SaveRules(entries, root, out error) writes each entry.LunaRule"
constraints: "Abort when Luna's in-memory JSON != luna.json on disk (edited outside Luna). Static tables need BindingFlags.FlattenHierarchy on TextureOverride"
verified: "Save, new-override creation and revert round trip: only intended lines change, file restored byte-identical, Luna memory == file"
```

### `speed` {#speed}
```yaml
spec_ref: "Spec#7 Speed"
state: done
scripts: [ParallelWork.cs, LunaPipeline.cs, SourceOptimizer.cs]
solution:
  - "Export inputs on main thread (Unity API), run tool chains on all cores via ParallelWork.Run (cancelable progress bar)"
  - "Content cache key = SHA1(input pixels) + all commands (work path blanked) + Luna version; stored in predictions.json next to mtime keys"
  - "pngquant output cached by SHA1(source + options) in Library/LunaTextureOptimizer/QuantizeCache (atomic copy+move)"
  - "Default PNG candidates skip Unity import (TextureEntry.SourceFileOverride); Sprite candidates imported in one StartAssetEditing batch"
results: "8 textures: predict 2.9 s -> 1.3 s cold / 0.02 s cached; evaluate 18.6 s -> 5.5 s cold / 0.8 s cached; outputs identical to serial version"
constraints: "No Unity API on worker threads (PlayerSettings.colorSpace read while building commands on main thread). Each batch uses its own work folder (Predict/<guid>/<i>)"
rejected: "Faster cwebp/pngquant settings -> predictions would stop matching Luna"
limits: "Cold run bounded by slowest single texture (cwebp -m 6, 2048px pngquant). Cache files never pruned"
```

### `window` {#window}
```yaml
state: done
scripts: [LunaTextureOptimizerWindow.cs]
solution: "MultiColumnHeader table; cells drawn from header.GetCellRect so they align with titles; virtualized rows. Toolbar: Refresh, Predict, Pick smallest format, Save to Luna (N), Discard, Optimize sources…, Restore originals, search. Unsaved rows show Predicted in blue with 'was X' tooltip"
constraints: "Popup changes run via EditorApplication.delayCall (predicting runs external tools). Optimize sources disabled while unsaved changes exist"
dev_tip: "Unity does not auto-recompile edits in this external file package: AssetDatabase.ImportAsset(ForceUpdate) + CompilationPipeline.RequestScriptCompilation"
not_verified: "Agent cannot click editor UI: dropdowns, progress-bar Cancel, Save/Discard, Apply/Restore buttons"
```

### `coverage` {#coverage}
```yaml
spec_ref: "Spec#8 Coverage gaps"
state: pending
problem: "Sprite atlases and TMP font atlas textures are not listed; HDR and normal maps not modeled; prediction cache never pruned"
next: "Check how Luna exports SpriteAtlas and TMP font textures (EditorScripts.dll) before adding them"
```

### `rollout` {#rollout}
```yaml
spec_ref: "Spec#10 Team rollout"
state: pending
problem: "Only tested on macOS, one project, 8 textures"
next: "Windows check (tool paths resolved from Luna config.json per platform); try on a second playable; large-project timing"
```

### `script-injection-spike` {#script-injection-spike}
```yaml
state: done
question: "Can a custom encoder injected into Luna's build ship smaller files at >= Luna's quality?"
hook: "Rule `script` replaces Luna's whole compression step (incl. resize). Placeholders %{input} (stage1 PNG, NOT resized), %{output} (script/<name + rule.ext>), %{temp}. Runs via child_process.exec. Luna bundles node (tools/node/mac64, win64) -> cross-platform script possible"
method: "spike.js / lunaq.js in session scratchpad: export Luna input + exact Luna output per texture, encode candidates from a lossless Mitchell-resized reference, SSIM (magick compare, flattened on black+white for alpha) must be >= Luna's"
results:
  - "Custom PNG (pngquant --nofs + pngcrush -brute -rem alla): 0-11% smaller on small icons (~0.35 KB total of ~66 KB)"
  - "Custom WebP (cwebp 1.6, sharp_yuv, -af, lower alpha_q, near_lossless): no gain over Luna at equal SSIM, except near_lossless on one flat icon"
  - "Custom JPEG (libjpeg-turbo progressive, magick -strip): loses to WebP"
  - "Luna's OWN WebP chain at lower quality: t_machine JPEG q65 22.8 KB -> WebP q40 6.3 KB at equal SSIM (q60 8.0 KB, visibly closer to source)"
  - "Lossy WebP on tiny palette icons is much worse (EmojiOne SSIM 0.84 vs 0.999) -> keep PNG there"
decisions: "Injection not worth it now (small gain, disables Luna resize, Windows/script upkeep). Bigger win = per-texture quality search with Luna's own settings"
constraints: "Equal SSIM != visually identical: WebP q40 smooths fine grain. Show visual compare before accepting"
rejected: "AVIF (not in Luna's format list)"
```

### `quality-search` {#quality-search}
```yaml
state: pending
next: "Per texture: search Luna's own format + quality (WebP/JPEG quality, PNG quant) for the smallest output whose quality metric >= a floor; add quality metric to Pick smallest (size-only can pick a visibly worse format); visual before/after compare in window"
```

### `compile-cache` {#compile-cache}
```yaml
state: done (verified in real Luna builds: HIT 0.2-0.3 s instead of ~11-23 s)
scripts: [Tools~/LunaCompileCache/msbuild-cache.sh, msbuild-cache.js, msbuild-arm.sh]
hook: "luna.json unity.scripts.msbuildMac64 -> msbuild-cache.sh (set via Luna in-memory field `msbuild`; OnBeforeSerialize copies it to msbuildMac64). Backup: session scratchpad luna.json.before-probe"
facts: "Luna compiles C#->JS with MSBuild /t:Rebuild on every build (Developer and Build&Estimate): ~23 s of ~38 s. Bridge parse ~8 s + emit ~8 s; Csc 0.7 s. Mono 6.12, Unity MonoBleedingEdge, Luna node + ImageMagick are x86_64 -> Rosetta on Apple Silicon"
solution: "Content-hash cache of all compile inputs (csproj, sources, HintPaths, bridge.json, Bridge tools, typemaps) -> restore 58 output files + replay console. Misses run Mono's MSBuild.dll on Homebrew ARM Mono 6.14 (9-12 s vs 20-23 s, identical JS)"
constraints: "Detect ARM with sysctl hw.optional.arm64 (uname says x86_64 under Luna's Rosetta node). Flush stdout before exit (Luna parses 'Build FAILED'). Cache in ~/Library/Caches/LunaCompileCache, last 5. LUNA_COMPILE_CACHE=off bypasses"
rejected: "dotnet msbuild (Bridge still execs Intel mono), Bridge CLI on .NET 10 (missing .NET Framework assemblies), Mono AOT (6%), GC nursery tuning, node reuse; custom transpiler (months of work, max ~8 s gain)"
```

### `size-dashboard` {#size-dashboard}
```yaml
state: done v1
scripts: [LunaSizeDashboardWindow.cs, LunaStatsRunner.cs, Tools~/LunaSizeDashboard/stats-hook.js, LunaSettingsWriter.SaveModules]
menu: "Supercent/Luna Size Dashboard"
solution: "Runs Luna's own `package:stats` (PROJECT_PATH, STATS=3, TARGET_PLATFORM=develop) on the last build output via native ARM node (~21-33 s, identical to Luna's node). Private copy Library/LunaSizeDashboard/luna-<ver> (symlinks + patched Jakefile minify call) preloads stats-hook.js for per-file / per-C#-type Brotli sizes, scaled to Luna's exact scripts total"
facts: "Luna scripts size = base122(brotli q11(terser(all script files)))); network limits + template sizes are literal tables in Jakefile.js (5 MB most, 3 MB tiktok/tencent). Mecanim WASM = 138 KB of scripts; disabling saves 141.8 KB total (size only, needs playtest)"
contracts: "LunaStatsRunner.Start/Finish/ReadNetworks/BuildOutputTime; what-if = Library/LunaSizeDashboard/whatif (edited luna.json copy + LunaTemp symlink)"
verified: "dashboard run == Luna's unmodified stats (total 1,357,522, scripts 772,255); luna.json untouched"
not_verified: "window UI clicks, what-if from the window, Apply modules (writes luna.json), Windows (no symlinks -> no script breakdown)"
next: "User tries the window; then quality-search for textures, live (no-build) texture deltas in the total"
```

### `size-dashboard.live` {#size-dashboard-live}
```yaml
state: done (engine verified; window flow not clicked)
scripts: [LiveScriptsSandbox.cs, LunaStatsRunner.cs (live/network), LunaSizeDashboardWindow.cs (Compose), Tools~/LunaSizeDashboard/live-hook.js, brotli-cache.sh]
solution:
  - "Measure = live: textures from current settings (Texture Optimizer predictions), scripts compiled from current code with Luna's own code:compose -> code:build -> code:stage, then package:stats; other categories = last build, assets changed since flagged"
  - "Sandbox Library/LunaSizeDashboard/live/project: .cs COPIES, LunaTemp APFS clone (cp -c, 0.2 s), luna.json copy; Packages/ProjectSettings links only. Private compiler at luna-<ver>/pipeline/templates/LunaCompiler"
  - "live-hook.js wraps jake.createTask: drops account:authorize, skips diagnostics, refuses to compile files outside sandbox/private compiler"
  - "Skip: ScriptsFingerprint (all .cs + luna.json unity block + Luna version) unchanged and same network -> reuse; other network -> stats only"
  - "Caches: terser full + per-file by content hash (cache-<ver>), Luna brotli via BROTLI_PATH_OVERRIDE wrapper keyed on content"
facts:
  - "Stats depend on TARGET_PLATFORM: develop/tiktok count assets raw, applovin etc. base64 (x4/3); template ('other') per network. Develop totals understate assets ~25%"
  - "Live texture size = (predicted output + sidecar json) x (Luna size / (build file + sidecar)) -> 0-byte difference vs Luna (applovin)"
  - "code:stage runs pipeline/lib/utils/tracer.js; needed for exact scripts (without it +101 KB minified)"
  - "Luna install's LunaCompiler folder is shared by all projects (holds the last-built project)"
  - "Luna Compiler V2 (luna.json unity.enableLunaCompilerV2): Luna's build composes with hidden task code-v2:compose (runProjectDiagnostics, compileIntermediateAssembly -> LunaTemp/Assemblies/stage_1, decompileIntermediateAssembly -> stage_2/decompiled, Scripts.csproj lists the decompiled files). Bridge can fail on raw sources that compile fine decompiled (smashdown RapierPhysicsConfig.cs: Script.Write with '+'-split '{0}' template -> BridgeCompilerTask crash, exit 6)"
  - "2026-11-06 fix: LunaStatsRunner.LiveComposeTask picks code-v2:compose when LunaSettings.UsesCompilerV2; live-hook treats code-v2:compose like code:compose. Verified manually on smashdown sandbox: exit 0, 0 Bridge errors, 437 compiled files all inside sandbox, real LunaTemp untouched, diagnostics json written locally (not .sent); ~85 s first run"
verified: "scripts 772,255 = Luna; textures 0 B diff; deterministic snapshot: real Assets .cs, Luna compiler/engine/pipeline, luna.json, real LunaTemp unchanged"
timings: "first live ~50 s; repeat with code change ~35 s; network switch ~11 s; unchanged -> instant"
constraints: "Never link anything a Luna task may write (deletes/writes follow links). Use sorted (deterministic) checksums for safety checks. SafeDelete removes links with rm (no -r)"
not_verified: "window clicks (Measure/network switch/what-if/Apply), Windows"
```

### `size-dashboard.fast` {#size-dashboard-fast}
```yaml
state: done (engine verified; window flow not clicked)
scripts: [ScriptSizeEstimator.cs, LiveScriptsSandbox.cs (incremental + no-build skeleton), LunaSizeDashboardWindow.cs (Auto, estimate)]
solution:
  - "Instant estimate: exact measure stores per-.cs-file share (Bridge type -> declaring file); after edits, share x (deflate size new / old), new files at project average, metadata scaled. 20 added methods: estimate +345 B vs exact +209 B (raw-bytes scaling gave +983)"
  - "Auto (toolbar): watches .cs, luna.json, shipped textures every 2 s; after 1.5 s settle updates estimate + textures, then starts the exact live run; changes during a run stay pending"
  - "Incremental sandbox: copy only changed .cs (size+mtime), re-clone LunaTemp only on a new build: prepare 5.5 s -> 0.33 s"
  - "No build yet: skeleton LunaTemp (stage1/js package.json + unity.json, rootPaths.json from MonoBehaviour scripts reachable from Luna scenes/includes/Resources = Luna 17/17, empty deserializers.js in stage2+stage3, empty stats.json, stage2/3 assets dirs) -> scripts 756,355 vs 772,255 with build; deserializers estimated 16 KB"
facts:
  - "Refresh after code change ~40 s: Bridge 10.6 s, SyntaxTransformer ~4 s, Terser full bundle ~10 s, Luna brotli 1.0.9 (Intel) ~4 s; Burst/CPU-cache tuning does not apply (external tools)"
  - "deserializers.js comes from Luna's export type registry (filled while serializing scenes); cannot be generated standalone"
  - "Luna task errors can be hidden (log.info filtered): LUNA_LIVE_TIMING mirrors them as LUNA_LOG_INFO / LUNA_LIVE_ERROR"
incident: "14:22/14:32 shell prototypes linked stage3 files -> package:stats rewrote real LunaTemp/stage3/stats.json with scratch paths; restored 15:04 by Luna's unmodified package:stats on the real project; snapshot script no longer excludes any file"
```

### `size-dashboard.scene-estimate` {#size-dashboard-scene-estimate}
```yaml
state: built (2026-11-06), validated on Playable003
problem: "User: after switching scene, Measure kept the last scene's sizes. Cause: Measure re-runs Luna stats on the LAST BUILD output; only scripts (sandbox compile) and textures (predictions) were live; meshes/anims/prefabs/materials/fonts/scene came from the last build's export"
user_goal: "2026-11-06 user: measuring is about a faster overview than Luna's build; tool must do its own work, not just re-display a build"
split_playable003: "4,059 KB = textures 1,324 + scripts 882 + sounds 216 (tool-computed, ~60%) + meshes 1,014 + fonts/anims/materials/scene/prefabs/sprites/… ~480 + template 141"
decision: "2026-11-06 user agreed: per-asset size memory + own estimator (Luna export hook / real build stay optional for exact)"
scripts: [AssetSizeMemory.cs (new), SceneSizeEstimator.cs (new), LunaSettings.cs (ForScenes), SizeDashboardView.cs (Compose: record memory per measurement, SceneEstimate cached by scene|network|luna.json time|measuredAt; rows tagged; ReloadSettings rescans textures + Compose), LunaOptimizerWindow.cs (SizeOf uses estimate; badge '≈ <scene> not built: sizes partly estimated'; Overview note)]
luna_facts:
  - "Mesh blob (stage2 .blob) = vertexFloats*2 (halfPrecision) + indices*2 + blendShapeDeltaFloats*6 — byte-exact on 73/73 meshes. Floats per vertex by attribute: pos3 normal3 tangent4 color4 blendIdx4 blendWeight4 uv0-3 2 each (stage1 flags order). Unity Mesh data reproduces Luna's counts exactly (69/69 project meshes; 4 built-in via Resources.GetBuiltinResource)"
  - "Playable size per network = Luna stats size; compressing networks (ironsource) make it data-dependent: mesh factor 0.10-0.70 of raw, prefabs ~0.18, scenes ~0.065, sounds/fonts ~1.07 — so raw size is exact but playable size is not without a build"
  - "Luna export: EditorScripts polls an ExportInstruction json (Application.dataPath/<obfuscated name>) every 0.5 s and exports scenes to its targetPath (possible sandbox export, not used)"
  - "Placed prefabs: Luna lists only some placed prefab instances (by name); rule not determined (not 'referenced by a scene field'). Prefab instance m_SourcePrefab fileID 100100000 = the prefab asset itself"
solution: "AssetSizeMemory: Library/LunaSizeDashboard/asset-memory.json, key network|category|path|title -> size + file write ticks; Record on every new measurement (not noBuild). SceneSizeEstimator: shipped objects = scene + Resources/includes roots + every {fileID,guid} reference in shipped YAML (BlastRadius graph files) resolved via LoadAllAssetsAtPath + TryGetGUIDAndLocalFileIdentifier, classified by type into Luna categories; built-in meshes (guid e000…, ids 10202 Cube…) and builtin_extra (guid f000…); placed models add their renderers' meshes; TMP font assets add their material sub-assets; prefabs counted when referenced as asset, placed ones only if remembered. Size: memory (exact path+title, path-only, name-only; 'remembered, file changed' when ticks differ) else mesh raw*median(remembered size/raw of this scene) else scenes file bytes*median ratio else category median ('estimated'); resources/project-settings copied from memory; estimated mesh with same title as a shipped remembered mesh dropped (overridden mesh)"
verified: "Playable003 estimate vs Luna stats: non-texture 1,703.1 KB vs 1,711.3 KB (-0.5%); meshes 1,017.7 vs 1,014.5; sounds/fonts/scenes/SO/controllers/text exact; prefabs 37 vs 51 (known gap). Stage1LogicTest (never built): 0.2 s, total ~1.37 MB with 7 live textures (57 KB). Compose 47 ms, cached 21 ms; no tool errors"
not_verified: "A real build of a second scene to compare the estimate (next time the user builds Stage1LogicTest, compare). UI look of the tagged rows"
constraints: "Scripts row: deserializers part comes from the last build; texture row in estimate mode keeps only textures the current scene ships"
```

### `build-reuse` {#build-reuse}
```yaml
state: done (verified in real builds)
results:
  - "Small scene: build after code change 38 s (morning) -> 27.3 s (ARM, sharing broken) -> 14.4 s (dashboard compile HIT 16b91d9f98fa)"
  - "Playable003 (big scene): 36.5 s -> 28.0 s (HIT e858a605f2dd); the rest is Luna's Unity export of the bigger scene"
fixes:
  - "v3 keys: csproj <Link> elements repeat each path WITHOUT the leading slash; normalize/relocate every root form (slash, backslash, no leading slash). The earlier 'same key' test shared the bug; the valid test builds the real csproj independently and asserts no root left in the hashed text"
  - "Brotli reuse only matters for compressing builds (Build & Estimate / packaged networks); Developer (develop) builds do not call brotli"
scripts: [Tools~/LunaCompileCache/msbuild-cache.js (v2 keys), LunaBuildAcceleration.cs, Tools~/LunaSizeDashboard/brotli-cache.sh]
solution:
  - "Compile cache v2: keys replace the compiler root (dirname dirname csproj) and project root (prefix before /LunaTemp/stage3/processed-scripts in Compile Includes) with placeholders -> dashboard sandbox compile and real Luna build share entries. Verified: same key for both; transformed sources byte-identical"
  - "Restore relocates obj/Debug/*FileListAbsolute.txt (MSBuild clean deletes the files it lists) and the replayed console output to the current roots; two-pass token replacement, longest root first (sandbox compiler lives inside the project)"
  - "Brotli: LunaBuildAcceleration sets BROTLI_PATH_OVERRIDE/LUNA_REAL_BROTLI/LUNA_BROTLI_CACHE in the editor process; Luna's editor starts the pipeline with UseShellExecute=false + inherited env. Toolbar toggle 'Speed up builds' (EditorPrefs, default on); never overrides a foreign BROTLI_PATH_OVERRIDE"
constraints:
  - "EditorApplication.delayCall does not run while Unity is in the background; use EditorApplication.update one-shots"
  - "brotli wrapper: failed cache copy falls back to Luna's real brotli"
not_reused: "Terser minify of the bundle (~10 s) and texture processing: would need NODE_OPTIONS injection / per-texture script rules"
```

### `size-dashboard.scene-roots` {#size-dashboard-scene-roots}
```yaml
state: done (verified: sandbox compile HIT the Playable003 build's own entry 730a61bc870e)
scripts: [LiveScriptsSandbox.ScriptRoots/WriteRoots, LunaStatsRunner (TryLock/Unlock), LunaSizeDashboardWindow.WatchStamp]
problem: "After switching scenes, the sandbox used rootPaths.json from the LAST build -> compiled a different script set -> real build missed the cache"
solution:
  - "Every live run writes rootPaths.json from the CURRENT scenes (MonoBehaviour scripts reachable from Luna scenes/includes/Resources, outside Editor): 17/17 and 63/63 vs Luna, 0.25 s"
  - "Roots are part of ScriptsFingerprint; Auto also watches the Luna scene files"
  - "Library/LunaSizeDashboard/run.lock (pid|id): a second run is refused while one runs (two runs in one sandbox corrupt each other's compile)"
```

### `size-dashboard.ui-lists` {#size-dashboard-ui-lists}
```yaml
state: done (logic verified by code; window not clicked by agent)
scripts: [LunaSizeDashboardWindow (DrawCategories/DrawScripts/DrawList/LinkRow/Locatable/Resolve), ScriptSizeEstimator.TypeFileMap]
solution:
  - "All assets per category (first 20 + 'Show all N'); scripts by engine file, namespace, C# files (133) and C# types (176); toolbar filter narrows every list and auto-expands matches"
  - "Single click on a link-coloured name: FocusProjectWindow + Selection + Ping; FBX sub-assets resolved by name (50 meshes); shaders via Shader.Find -> GetAssetPath; name-only entries via FindAssets by type (prefabs, materials, textures, meshes, clips, sounds, sprites)"
  - "Playable003: 494/529 assets locatable; the rest are Unity built-ins, Luna-generated files, or names with no project asset"
constraints: "GUIStyle fields must be [NonSerialized]: Unity's script reload restores private fields of open windows and GUIStyles come back with black text (fixed in both windows)"
```

### `terser-reuse` {#terser-reuse}
```yaml
state: pending (optional)
idea: "Real builds still minify the 14 MB script bundle (~10 s). Reuse the dashboard's cached full minify by preloading the stats hook into Luna's build node (NODE_OPTIONS from the editor env) and intercepting its bundled Terser"
risk: "NODE_OPTIONS reaches every node process Unity starts; Luna's Jakefile is obfuscated (patch point found by regex in the private copy only)"
```

### `audio-compress` {#audio-compress}
```yaml
spec_ref: "Spec#16 Audio compress / extension switch"
state: iterating (MP3 tuning done; Opus format option built, not yet in a real Luna build)
scope_agreed: "2026-11-02 user: MP3 tuning + OGG switch; order: MP3 first, then OGG test build on devices"
├── mp3-tuning:
│     state: done (UI not clicked by agent)
│     menu: "Supercent/Luna Sound Optimizer"
│     scripts: [LunaSoundOptimizerWindow.cs, LunaSoundPipeline.cs, BuildSoundScanner.cs, SoundEntry.cs, LunaSettings.cs (LunaSoundRule, SoundRuleFor, IsStaleOverride), LunaSettingsWriter.cs (SaveSoundRules, RepairSoundOverrides), LunaInstall.cs (FfmpegPath), PredictionCache.cs (KeyFor(SoundEntry)), LunaBuildStats.cs (sounds too), BuildTextureScanner.cs (CollectShippedAssets)]
│     features: "List shipped sounds; Exported column (wav as-is vs Unity import, orange); Bitrate popup 32-160 kbps; Channels Mono/Stereo/As exported; Trim read-only; Predicted (base64) with unsaved change in blue; Last build; Listen Source/Luna (predicted mp3 via AudioUtil.PlayPreviewClip); Save to Luna / Discard; stale override alert + Fix (move to unique same-name shipped file, else remove; asks first)"
│     contracts: "LunaSoundPipeline.PredictMany(entries, install, root, cache, progressTitle); ExportInput(entry, root, wavPath); PreviewPathFor(entry, root, install) -> Library/LunaTextureOptimizer/SoundPreview/<sha1(cache key)>.mp3. SoundEntry.PlayableBytes = (mp3 + sidecar) * 4/3; SidecarBytes = exact stage2 json length (5-digit id)"
│     luna_pipeline: "Jakefile sound class: rule.script -> custom script (%{input} stage1 wav, %{output}, %{temp}; rule.ext sets json.ext). Else `ffmpeg -y -i in.wav -acodec mp3 <-b:a Nk | -q:a 8 if bitrate<=0> [-ac C if >0] [-ss start -t lengthInSeconds if endTime != 00:00.000] out.mp3`. No -ar (sample rate kept). Rule lookup getRules('sound') = texture lookup"
│     editor_export: "VERIFIED: .wav sources copied unchanged (Unity import ignored). Other sources: Unity AudioClip.GetData -> 16-bit PCM (short)(s*32767f), 44-byte header (import settings like Force To Mono reach the build). GetData needs Load Type Decompress On Load -> otherwise prediction fails with that message"
│     luna_settings: "EditorScripts: Sounds {Sound default; List overrides; Dictionary overridesDictionary}, SoundOverride : Sound (name; ctor(Sound)). Holder has a Sounds getter. Serialization follows overridesDictionary and is order-independent"
│     verified: "14/14 Playable003 sounds byte-identical to LunaTemp/stage2 mp3; sidecar formula = 124 B like Luna's file. Writer path tested in memory only (move + new override serialize as expected; revert -> Luna memory == luna.json). Window opened, no console errors from tool code"
│     not_verified: "Clicks (popups, Listen, Save, Fix) not done by agent; Save/Fix never written to luna.json by agent; Windows; non-wav clips with Load Type != Decompress On Load (Luna's own behavior unknown)"
│     size_note: "Playable size is per network: ironSource BGM 172 KB from 154 KB mp3 (factor ~1.12, <1 for tiny files). Window shows base64 x4/3 like the texture tool; dashboard has exact per-network numbers"
│     findings_playable003: "14 sounds, 342 KB mp3 (~456 KB base64). BGM 154 KB (45%); 64k -> 103 KB, 48k -> 77 KB. 5 of 11 sound overrides stale (Sounds/X moved to Sounds/Using/): BGM 64k override ignored. Only LevelUp.mp3 override applies"
│     constraints: "Never write luna.json directly (Luna overwrites); refuse save when Luna memory != file. Bitrates >= 32 (MPEG-1 layer III at 44.1/48 kHz)"
│
└── ogg-switch:
      state: built (2026-11-03), not verified in a Luna build or on devices
      decision: "2026-11-03 user: no iPhone available -> ship as user-chosen option with risk warnings (MP3 / Ogg Opus / WebM Opus per sound); user decides per case. Target audience likely >= iPhone 12, but iOS version is what matters"
      scripts: [LunaSoundFormat.cs (new), LunaSoundPipeline.cs, SoundEntry.cs (Format, sidecar ext), LunaSettingsWriter.cs (writes script + ext), LunaSoundOptimizerWindow.cs (Format column, risk dialog + banner, Encoder… picker), ProcessRunner.cs (output overload, public Quote)]
      rule_shape: "script = \"<ffmpeg>\" -y -i %{input} -vn -map_metadata -1 -c:a libopus -b:a Nk [-ac C] [-ss start -t length] -f ogg|webm %{output}; ext = .ogg|.webm; bitrate field mirrors the Opus bitrate. Detect: script contains libopus + ext -> Opus; other scripts -> 'Custom script' (read-only)"
      luna_exec: "VERIFIED in Jakefile: applyCustomScript replaces first %{temp}/%{input}/%{output} with commandJoin-quoted paths, runs via util.promisify(child_process.exec) (shell); output name = basename with ext; processJson sets json.ext. Custom script skips Luna's -ss/-t -> trim baked into script"
      encoder: "Luna ffmpeg has no libopus -> script calls ffmpeg found at /opt/homebrew/bin, /usr/local/bin, /usr/bin (must list libopus) or picked via Encoder… (EditorPrefs Supercent.LunaTextureOptimizer.OpusFfmpeg). Path is stored in luna.json -> every build machine needs it at the same path"
      preview: "Unity cannot play Opus -> output decoded to .wav in SoundPreview for Listen"
      ios_support: "Web sources (unverified on device): Ogg Opus Safari/iOS 18.4+ (MDN PR only); WebM Opus via decodeAudioData ~iOS 17.4+ (WebKit bug 238546, maybe 15.4). Android Chrome both"
      verified: "BGM in memory: mp3 64k 102,091 B; Ogg Opus 32k 53,095 B; WebM Opus 32k 56,451 B (same as CLI). Stored script run through /bin/sh with spaced paths -> valid opus 48k mono. Compiles; window opens without errors"
      fixed: "2026-11-03 ButtonPress_2.wav has embedded 1024px cover art -> ffmpeg tried to encode video (Ogg failed, WebM 67 KB). Added -vn -map_metadata -1"
      sizes_all_14: "mp3 64k 221 KB | ogg opus 64k 220 KB | ogg 32k 109 KB | webm 32k 121 KB | ogg 24k 84 KB (raw, before base64). Same bitrate = same size or larger (VBR overshoot, container headers); saving comes from lower bitrate. WebM header ~+450 B/file"
      user_test_2026-11-03: "User listened (Source vs Luna) and clicked Save to Luna -> luna.json 15:54 has override BGM_1Stage.wav: script libopus 32k -ac 1 -ss 00:00.000 -t 12.677 -f ogg, ext .ogg, bitrate 32, channels 1 (shape as designed). No Luna build yet (LunaTemp still 2026-11-02 16:57)"
      listen_verdict: "2026-11-03 user: BGM Ogg Opus 32k mono sounds fine vs source -> keep 32k"
      build_check: "2026-11-06 Luna build 09:20 (develop): BGM shipped as .ogg (1 ogg + 13 mp3); user: music plays in desktop Chrome. Predicted Opus output 53,000 B == real LunaTemp/stage2/assets/sounds/28446.ogg 53,000 B (byte-identical size). Still untested: Safari/iOS, Android device"
      not_verified: "Real Luna build with an Opus rule (rule now saved, build pending); game playback in browser/devices; Windows (path quoting via cmd.exe)"
      runtime_load: "engine/luna/script1.js: fetch assets/bundles/<bundle>/<id><json.ext || .mp3> -> AudioContext.decodeAudioData; no fallback (silent if undecodable)"
      encoders: "Luna ffmpeg 4.1: libmp3lame + aac; vorbis/opus only experimental native. Homebrew ffmpeg 9.0.1 here: libopus yes, libvorbis no. rule.script runs in every build -> every builder needs the encoder"
      next: "User: set one sound to Opus in the window, Save to Luna, build, check it plays in desktop browser + any Android; compare Last build size with prediction"
```

### `texture-preview` {#texture-preview}
```yaml
spec_ref: "Spec#17 Texture preview"
state: built (2026-11-05), UI not clicked by agent
decision: "2026-11-05 user: Luna input vs Luna output side by side; panel at bottom of Texture window (resizable split, toolbar 'Preview' toggle)"
scripts: [TexturePreviewPanel.cs (new), LunaPipeline.cs (RenderPreview, PREVIEW_FOLDER), LunaTextureOptimizerWindow.cs (previewPath, splitter, row highlight, Preview toggle EditorPrefs Supercent.LunaTextureOptimizer.ShowPreview)]
ux: "Click texture name -> row highlighted + preview (still pings asset). Left: 'Source (Luna reads this file)' or 'Luna input (Unity's import)' W×H · file KB. Right: 'Luna output · ext · W×H · KB (KB in playable)' + 'unsaved'. Both drawn at input's on-screen size (output stretched like in game). Zoom Fit/1:1/2:1/4:1 (Point filter >= 2:1), shared scroll; Background Checker/Black/White. Follows unsaved rule changes (key = prediction key)"
contracts: "LunaPipeline.RenderPreview(entry, install, root, key, out inputPng, out outputPng, out error): Prepare+Run one job, keep Library/LunaTextureOptimizer/TexturePreview/<sha1(key)>.input.png/.output.png; WebP decoded via Luna's magick png32:. Reused if both files exist"
verified: "Compiles. png/webp/jpeg outputs render + load in Unity (EmojiOne 512->64 png 1.4 s, T_Hammer 2048->256 webp 0.7 s, T_Trash 256 jpeg 0.1 s); 2nd call 0 ms; preview output bytes == predicted outputBytes (1642, 18485). Panel rendered via its draw path (T_Hammer 2048 vs 256), no tool errors in console"
not_verified: "UI clicks (zoom/background toolbars, splitter drag, scrolling, toggle); look of checker/stretching; very large textures (4096+) speed"
constraints: "Rendering runs synchronously on the main thread in a delayCall (one texture). TexturePreview folder is never pruned (Library only)"
user_verdict: "2026-11-06 user: texture preview is very good"
next: "User tries it on a few textures; possible follow-ups: diff/amplify view, prune old preview files, preview in Pick smallest format"
```

### `asset-removal` {#asset-removal}
```yaml
spec_ref: "Spec#18 Asset removal and alert"
state: in-progress (scoped 2026-11-06)
added: 2026-11-02 (user checklist)
goal: "User force-excludes assets they don't want shipped, from the Size Dashboard. Project files/scenes untouched; Luna leaves them out. Before excluding: blast-radius review (scene objects, prefabs, materials, assets that reference it; assets that drop with it; bytes saved). User may accept broken refs"
decisions: "2026-11-06 user: targets = assets + folders (not scripts, not scene objects). Entry = dashboard asset-list right-click 'Exclude…' + 'Exclude selected' from Project window selection; both open the same review"
luna_hooks_seen: "luna.json unity.assets.excludes / unity.scripts.excludes (glob list, e.g. **/Editor/), unity.assets.includes, unity.unusedClasses/unusedMethods excluded flags"
├── luna-exclude-spike:
│     state: done (2026-11-06)
│     verified_build: "Build 09:20 with T_Trash_Albedo.png excluded: build OK, no Luna warning/error; texture absent from stage1 (243 textures); exported Trash.mat keeps _BaseMap -> id 59586 (dangling). In game (user): trash decor on the river renders COMPLETELY WHITE (base color 1,1,1); no crash reported. Opus BGM shipped as 1 .ogg + 13 .mp3"
│     reverted: "2026-11-06 via LunaSettingsWriter.RestoreSection (history record) -> excludes back to 36 entries without the test path"
│     conclusion: "Exclude = silent drop; referencing assets keep a dangling id. Consequence per type must come from the review (texture -> white/untextured); other types (mesh, sound, prefab, font, material) not tested"
│     questions: "Does unity.assets.excludes drop an asset that is still referenced (scene/material/prefab)? Glob semantics (file path, folder, ** )? Build fails / ships null ref / ignores? Is the exclude applied in the editor export (EditorScripts) or the Jakefile? Effect on dependents (material excluded -> its textures?)"
│     method: "Decompiled EditorScripts.dll (Luna's ICSharpCode.Decompiler from ~/Downloads/7.2.0/tools/ILSpyRunner + small dotnet runner in scratchpad); then one real build (user authorized 2026-11-06)"
│     static_findings: "unity.assets.excludes = path prefixes: excluded if path StartsWith an entry (exact file or folder 'Assets/X/'), '**/' entries match anywhere; includes override. Luna's Assets tab writes exact asset paths there (tree 'excludable' flag). Main scene excluded -> Luna refuses to build. Export runs from an ExportInstruction file (excludes/includes); exporter on the export copy DestroyImmediate()s excluded GameObjects and collects excluded paths in a list (likely a log warning). Reading: refs to excluded assets become null, build does not fail — UNVERIFIED"
│     spike_write: "2026-11-06 09:2x: added Assets/Supercent/ThemeparkManager/Textures/T_Trash_Albedo.png (only user: Materials/Trash.mat) via LunaSettingsWriter.SaveAssetExcludes -> recorded in Undo history (section AssetExcludes). MUST undo after the test build"
│     build_settings_note: "EditorBuildSettings has 5 enabled scenes (Playable002, Playable003, 3x _Playable004 tests); Luna builds unity.scenes/startupScene"
├── ref-graph:
│     state: done (2026-11-06), engine only (no UI yet)
│     decision: "2026-11-06 user: full chain to the scene + one-line consequence per type (not flat list, no also-dropped list)"
│     scripts: [BlastRadius.cs (new), YamlReferenceLocator.cs (new), BuildTextureScanner.cs (CollectRoots + IsScript now public)]
│     contracts: "BlastRadius.Analyze(selection files/folders, LunaSettings) -> Targets[{AssetPath, Consequence, Users: Link tree}], NotShipped, Blockers (Luna build scene). Link {AssetPath, Where[] (how it uses the child), Users, RootKind (build scene / Resources / always included), ShownElsewhere}"
│     solution: "Graph from Luna roots (CollectRoots) via AssetDatabase.GetDependencies(path,false), not entering already-excluded assets or scripts; reverse map = users. Where = YamlReferenceLocator over text YAML (ForceText project): doc headers, GameObject paths via Transform m_Father, stripped transforms -> prefab instance; PrefabInstance overrides via propertyPath + target fileID resolved in the source prefab, nested prefabs via id = (instanceId XOR sourceId) & long.MaxValue; instance name = m_Name override on source root only. Skips bookkeeping lines (- target:, m_CorrespondingSourceObject, m_PrefabAsset). FBX users -> 'model import settings (material remap)'"
│     verified: "T_Trash_Albedo: Trash.mat › _BaseMap <- Trash_A/B/C (Pivot/Model › MeshRenderer.m_Materials[0]) <- Playable003 'Game/Map/LazyRiver › TrapStackSpawner._decorFlowPrefabs[3]' + _subDecorFlowPrefabs. T_Hammer_Albedo02: fbx remap + Hammer.mat -> AutoHammer Model / ButtonModel -> Hammer / RiverButton -> Playable003. _Playable004 folder: 0 shipped targets. ~0.2 s per analysis"
│     known_limits: "Child names inside prefab instances show the prefab's own names (scene m_Name overrides on children not applied: DirtyRiver shows as LazyRiver). Consequences other than texture are untested guesses (labelled). Code that loads assets by path/Addressables not seen"
├── exclusion-window:
│     state: built (2026-11-06), UI not clicked by agent
│     decision: "2026-11-06 user: exclude from ANY tab; one standalone exclusion window; click = stage (Pending), window shows affected files/references, then 'Apply to Luna' (chosen over immediate / ask-per-click). Overview kept as is + search"
│     scripts: [LunaExclusionWindow.cs (new, menu Supercent/Luna Exclusions), ExclusionQueue.cs (new: Library/LunaTextureOptimizer/exclusion-queue.json, Changed event, Project menu Assets/Luna Optimizer/Add to Luna Exclusions), BlastRadius.cs (asIfShipped param), LunaOptimizerWindow.cs (header 'Exclusions (n)', static SizeOf, NotifyLunaSettingsChanged), SizeDashboardView.cs (row menu 'Add to Luna Exclusions', Exclude selection… -> queue, 'Open Exclusions' button, full-width search bar DrawSearch), TextureOptimizerView.cs / SoundOptimizerView.cs (row right-click: add row / add N ticked)]
│     removed: "ExcludeReviewWindow.cs (pop-up review) deleted"
│     ux: "Toolbar Refresh | Add Project selection | Undo… (AssetExcludes). PENDING (n) · saves ~X · N in use | Apply to Luna | Clear; per item ▼ name (folder: N assets) size, 'N in use'/'unused'/'not in the build', Remove; expanded: consequence + users chain with @ locations. EXCLUDED (n): plain Assets/ entries, same chain = references broken now, Re-include. Reloads when luna.json mtime changes or queue changes"
│     contracts: "LunaExclusionWindow.Open(), AddAndShow(paths) (adds to queue, shows window without focusing). One BlastRadius.Analyze for all pending, one for all excluded (asIfShipped = excluded entries). Apply writes folder as 'X/' and file as path in one SaveAssetExcludes record, removes applied items from queue"
│     verified: "Compiles; window + hub tabs drawn without tool errors. Pending EmojiOne.png -> 1 target, 1 user, luna.json unchanged, queue cleaned. Excluded T_Trash_Albedo -> chain to Trash.mat"
│     incident: "luna.json has T_Trash_Albedo.png excluded again: history record 2026-11-06 09:40:25 'Exclude: T_Trash_Albedo.png' from the old review window's Exclude button (sample window agent opened at 09:40 for the user to look at). Agent did not intend to apply it; asked user whether to keep or Re-include"
│     incident_resolved: "2026-11-06 user chose Re-include -> SaveAssetExcludes(remove) recorded 'Re-include: T_Trash_Albedo.png'; excludes back to 36, trash not excluded"
├── review-ui:
│     state: replaced 2026-11-06 by exclusion-window (built earlier same day)
│     scripts: [ExcludeReviewWindow.cs (new, utility window)]
│     ux: "Header: N assets · saves ~X KB (dashboard last-measure sizes). Note: project unchanged, Luna drops silently. Blockers (build scene) disable Exclude; not-shipped selection listed + skipped. Per target: ▼/▶, file link (ping), size, consequence (or 'Nothing in the build uses it'); tree of users ← file link [root kind] (users listed above) + '@ GameObject › Component.field' lines. >5 targets start collapsed; Expand/Collapse all. Buttons Cancel | Exclude / 'Exclude anyway (N in use)'"
│     contracts: "ExcludeReviewWindow.Open(selection, Func<string,double> sizeOf, Action excluded). Writes folders as 'Folder/' (prefix), files as exact path, via LunaSettingsWriter.SaveAssetExcludes (history section AssetExcludes)"
│     verified: "Compiles; window opened for T_Trash_Albedo and drew without tool errors; dashboard repaints without errors. Nothing excluded by agent through the UI"
├── exclude-store: {state: built, scripts: [LunaSettingsWriter.cs (SaveAssetExcludes, LunaState.UnityAssets), LunaSaveHistory.cs (section AssetExcludes)], constraints: "Write via LunaSettingsWriter (Luna memory); Undo history section AssetExcludes restores the whole unity.assets block (excludes/includes/export)"}
├── entry-points: {state: built, ux: "Dashboard: right-click any asset row with an Assets/ path -> Exclude from Luna build…; toolbar Exclude selection… takes Project window selection (files/folders under Assets/), message if empty"}
├── excluded-list: {state: built, ux: "Dashboard section 'Excluded from the build (N)' after Engine modules: plain Assets/ entries of unity.assets.excludes (hides **/ patterns and Luna Stubs/ defaults), link + (missing in project), Re-include button, Undo… (section AssetExcludes)", scripts: [LunaSizeDashboardWindow.cs, LunaSettings.cs (Excludes)]}
└── build-verify: {state: pending}
```

### `optimizer-hub` {#optimizer-hub}
```yaml
spec_ref: "Spec#20 Luna Optimizer hub"
state: in-progress (shell built 2026-11-06)
trigger: "User: current UI/UX hard to navigate (3 windows + review pop-up). Proposed tabs, previews for any asset, integrate Sound + Texture windows"
decisions: "2026-11-06 user agreed: one hub window; tabs Overview · Textures · Sounds · Models & Other · Scripts & Modules · Exclusions; same master-detail layout on every tab with the details panel on the RIGHT (resizable); details = preview + Luna settings + size + Used by + Exclude; global header always visible (size vs network cap, unsaved delta, one Save/Discard/Undo; tab badges for pending counts); excluded = row state (greyed + badge) not a separate world; old menu items open the hub on their tab"
principles: "One mental model; consistent master-detail; information before decision (Used-by visible while browsing, no modal surprise); no hidden unsaved state"
previews: "Texture: existing Luna input/output compare. Audio: waveform + Source/Luna playback. Model/prefab/material/mesh: Unity interactive preview (Editor.CreateEditor -> OnInteractivePreviewGUI). Other: Unity default preview"
ui_tech: "2026-11-06 user: UI Toolkit, incremental. Verified in 2022.3.19: MultiColumnListView, MultiColumnTreeView, TwoPaneSplitView, TreeView, DropdownField, IMGUIContainer, ToolbarSearchField/ToolbarToggle, ContextualMenuManipulator, ProgressBar available; TabView/Tab NOT (2023.2+) -> tabs via ToolbarToggle group. Plan: (1) hub frame in UIT hosting current IMGUI views via IMGUIContainer, (2) convert tabs one by one (Textures first) to MultiColumnListView + TwoPaneSplitView details, (3) previews stay IMGUI inside IMGUIContainer"
risk: "Large refactor (~3k lines of IMGUI across LunaTextureOptimizerWindow, LunaSoundOptimizerWindow, LunaSizeDashboardWindow); do in slices, each usable; old windows keep working until replaced"
├── shell:
│     state: built (2026-11-06), UI not clicked by agent
│     menu: "Supercent/Luna Optimizer (new). Old menus Supercent/Luna Texture Optimizer | Sound Optimizer | Size Dashboard open the hub on Textures | Sounds | Overview"
│     scripts: [LunaOptimizerWindow.cs (new hub), OptimizerView.cs (new base), TextureOptimizerView.cs / SoundOptimizerView.cs / SizeDashboardView.cs (were the window classes: class renamed, base OptimizerView, MenuItem removed, OnEnable/OnDisable/OnGUI overrides, Title/PendingCount/SavePending/DiscardPending), LunaTextureOptimizerWindow.cs / LunaSoundOptimizerWindow.cs / LunaSizeDashboardWindow.cs (now redirect stubs: open hub tab + Close, keep old GUIDs for saved layouts), LunaSaveHistory.cs (ShowRevertMenuAll), SizeDashboardView (BudgetSummary, ReloadSettings; Texture Optimizer button -> OpenTab)]
│     contracts: "OptimizerView: Title, PendingCount, SavePending, DiscardPending, OnEnable/OnDisable/OnGUI, Attach(host)/Detach, protected Repaint/ShowNotification via host. Hub serializes views ([SerializeField]) so column layouts survive reloads; overview always attached (header size line), other tabs attached when first shown"
│     ux: "Header (toolbar): size / cap (network) + budget bar | 'N unsaved change(s)' | Save all | Discard all | Undo… (all sections in submenus). Tab bar: Overview | Textures (n) | Sounds (n)"
│     verified: "Compiles; each tab drawn via RepaintImmediately without tool errors; old windows redirected and closed"
│     backup: "Pre-refactor Editor folder copy: scratchpad backup-editor-20261106-095005 (package is not a git repo)"
│     not_verified: "Clicks; Save all/Discard all/Undo from header; reload survival of tab state; per-view toolbars still duplicate Save/Discard/Undo (to be unified in master-detail)"
├── uitk-frame:
│     state: built (2026-11-06), UI not clicked by agent
│     scripts: [LunaOptimizerWindow.cs (rewritten: CreateGUI, Toolbar header, ToolbarToggle tabs, IMGUIContainer content), UI/LunaOptimizer.uss (new), OptimizerView.cs (Attach(window, repaintView) callback)]
│     contracts: "Header refreshed by root.schedule every 300 ms (RefreshHeader: budget label + bar fill classes --warn/--over, pending count, Save all/Discard all enabled, Exclusions (n), tab badges). Views repaint via RepaintContent (content.MarkDirtyRepaint + Repaint). Stylesheet path Packages/com.supercent.luna-texture-optimizer/Editor/UI/LunaOptimizer.uss"
│     verified: "Compiles; CreateGUI built 3 root children + stylesheet; budget '4.00 MB / 5.00 MB (ironsource)'; all 3 tabs drawn inside IMGUIContainer via OpenTab + RepaintImmediately; no tool errors/warnings"
│     not_verified: "Clicks; look of USS in light skin; repaint of IMGUI content after async work (predictions, measure) — uses MarkDirtyRepaint"
├── master-detail:
│     state: iterating (Textures + Sounds converted 2026-11-06; Overview pending)
│     sounds_ui: "SoundOptimizerView.cs rebuilt as UIT view (1186 lines; logic blocks verbatim). Toolbar Refresh, Predict, Stop | search. Banners (HelpBox + button): stale overrides (Fix…), Opus risks (Encoder…, tooltip RISKS). Bulk bar Format/Bitrate (choices follow ticked formats)/Channels. Table: Pick, Sound, Source, Length, Exported (orange if Unity import), Format/Bitrate/Channels dropdowns (Custom script disabled; Opus pick resets field until the risk dialog is accepted), Trim, Predicted, Last build, Listen (Source/Luna buttons). Details: Listen section with Source + Luna waveforms (AudioWaveform.cs new: min/max peaks 512x64 texture as backgroundImage; Luna waveform from LunaSoundPipeline.PreviewPathFor when rendered, PlayLuna re-renders details), Stop, Luna settings dropdowns, Opus RISKS HelpBox, Size, Used by, Add to Luna Exclusions. LoadClip now also decodes .ogg/.aif sources"
│     overview_decision: "2026-11-06: Overview stays IMGUI inside the hub (user asked to keep it as is; ~1.2k lines of dashboard, low gain). Has full-width search + row right-click Add to Luna Exclusions"
│     assets_tab: "AssetsView.cs (new, tab 'Models & Other'): shipped assets minus textures/sounds/scenes/scripts/folders/DefaultAsset (BlastRadius.DirectUsers one graph pass -> users count). Kinds: Prefab, Model, Mesh, Material, Animation, Animator, Font, Shader, Data (<SO type>), other type names. Toolbar Refresh | type filter | 'Add N ticked to Exclusions' | search. Table: Pick, Asset, Type, Folder, File, In playable (LunaOptimizerWindow.SizeOf from Overview last measure; default sort desc), Used by count. Details: Unity Editor.CreateEditor preview via OnInteractivePreviewGUI in IMGUIContainer (else AssetPreview thumbnail), Size (type, file, in playable, Uses list), Used by chain, Add to Luna Exclusions…"
│     assets_verified: "350 rows (107 material, 79 prefab, 63 animation, 45 model, 18 animator, 15 shader, 5 font…); Trash_A.prefab + Hammer 1.fbx -> GameObjectInspector preview HasPreviewGUI true, sizes/uses/used-by filled; ~0.7 s for tab build + 2 selections; no tool errors"
│     shared_ui: "OptimizerUI.cs (new): SectionTitle, InfoRow, SmallLabel, LinkLabel, Ping, UsedBy(BlastRadius), ExcludedNote(settings) — Textures/Sounds views now use it (duplicates removed)"
│     polish: "Rows whose asset/folder is in ExclusionQueue show '⦸ excluding' greyed italic (class lo-excluding), live via ExclusionQueue.Changed -> table.RefreshItems (subscribed in each view's OnEnable/OnDisable); summaries append 'N excluded asset/folder entries not listed (see Exclusions)'; hub root gets class lo-light on the light skin (darker link/pending/warning/wave colours in USS)"
│     polish_verified: "T_Male_Dirty.png queued -> its row '⦸ excluding', removed -> 0 badges; summary note shows 1 entry; dark skin here (lo-light off) — light skin look not checked"
│     scene_picker: "2026-11-06 user asked for scene selection in the hub. Header: Scene DropdownField (Build Settings scenes + Luna scenes, file names, folder only on duplicate names) + Open (save prompt, opens build scene) + stale badge. Change -> asks Save all/Cancel/Discard if unsaved changes -> LunaSettingsWriter.SaveBuildScene(path): unity.scenes=[path], disabledScenes = every other known scene, startupScene=0, adds the scene to Build Settings if missing; history section Scenes (LunaSettingsSection.Scenes; snapshot = unity block, restore/compare only scenes/disabledScenes/startupScene via UNITY_SECTION_FIELDS shared with Modules) -> hub Reload. Settings cached by luna.json mtime"
│     scene_picker_bug: "2026-11-06 user: cannot switch scene. Cause: ChangeEvent callback deferred ChangeBuildScene(change.newValue) via EditorApplication.delayCall; UI Toolkit pools event objects, so newValue was reset by the time it ran -> label not found -> silent return. Also observed: EditorApplication.delayCall queue not processed while Unity is unfocused (20 entries incl. Unity's own startup ones). Fix: copy the value first and run via rootVisualElement.schedule.Execute. Verified: dropdown value Playable003 -> luna.json switched within the same second; restored from history snapshot (byte-identical); 2 stale queued test calls removed from delayCall. constraint: never read a UIT event object inside a deferred lambda"
│     stale_scene: "LunaBuildScenes.cs (new): last build scenes from LunaTemp/stage1/assets/scenes/*.json 'path' (cached by folder mtime); IsStale = build exists and its scenes != luna scenes. Hub badge '⚠ sizes from last build of X', Overview HelpBox (SizeDashboardView.StaleSceneNote), LunaOptimizerWindow.SizeOf returns 0 when stale -> Models 'In playable' shows '–'"
│     scene_verified: "Header showed Stage1LogicTest + stale badge (last build Playable003); ChangeBuildScene('Playable003') -> luna scenes [Playable003], badge cleared; RestoreSection(history before) -> luna.json byte-identical to original, badge back. No tool errors"
│     fbx_preview_bug: "User: cannot preview FBX. Screen reads showed Unity's GameObjectInspector interactive preview drew Shaker.fbx once then blank (player.fbx/prefabs ok). Offscreen renders (PreviewRenderUtility, RenderStaticPreview) are blank for ALL assets in agent context (editor in background), so pixels cannot be verified by agent; AssetPreview thumbnail does render Shaker (46 colours). Fix: ModelPreview.cs (new, own PreviewRenderUtility orbit view: drag rotate, scroll zoom, double-click reset; frames renderer bounds) for GameObject assets + 'Thumbnail instead of 3D' toggle (AssetPreview) in details. 89/94 models use Default-Material (Standard), built-in pipeline. VERIFY with user: Shaker.fbx in Models & Other"
│     fbx_preview_result: "2026-11-06 user: thumbnail works, 3D view (ModelPreview) does not; user: thumbnail is enough (nothing to optimize on FBX). ModelPreview.cs + toggle removed; models/prefabs always show AssetPreview thumbnail (no CreateEditor); materials/meshes/animations keep Unity's interactive preview (MaterialEditor etc). rejected: GameObjectInspector interactive preview and own PreviewRenderUtility orbit view for GameObject assets in the hub (blank)"
│     user_action_2026-11-06: "User excluded Assets/TextMesh Pro/Sprites/EmojiOne.png via Exclusions window Apply at 10:14:38 and built at 10:15 (kept as user set it)"
│     sounds_verified: "14 rows, 12 columns; BGM_1Stage.wav details (wav 2.34 MB 12.7 s, mono 48 kHz, trim, ogg 51.8 KB, 69.2 KB in playable, last build 59.3 KB), 2 waveforms rendered 512x64, Opus banner shown, stale hidden; GameEnd bitrate 64->48 via details: '10.0 KB (was 13.3 KB, unsaved)', tab 'Sounds (1)'; discard -> 0; luna.json unchanged. Playback not pressed by agent (no audio)"
│     scripts: [TextureOptimizerView.cs (rebuilt as UIT view, 1040 lines; IMGUI table/header/splitter removed, logic blocks kept verbatim), OptimizerView.cs (virtual CreateUI, OnGUI no longer abstract), LunaOptimizerWindow.cs (contentHost + per-view element cache: CreateUI() or IMGUIContainer fallback; ShowCurrentTab), UI/LunaOptimizer.uss (table/details/bulk classes)]
│     textures_ui: "Toolbar: Refresh, Predict, Pick smallest format, Optimize sources…, Restore originals | ToolbarSearchField. Summary + orange compression warning. Bulk bar: All shown, N ticked (+hidden), Format/Max size DropdownFields (keep…), Apply to N, Untick all. TwoPaneSplitView (details fixed 420 px right, viewDataKey lo-textures-split). MultiColumnListView (viewDataKey lo-textures-table, 22 px rows, sortable via sortedColumns -> ComparisonFor, default Last build desc): Pick toggle, Texture, File, Source px, Alpha, Type, Unity compression (orange if affects Luna), Luna format dropdown, Max size dropdown, Predicted (blue bold if unsaved), Last build. Row right-click: Add to Luna Exclusions / Add N ticked / Show in Project; double-click = ping. Details: title + path link, TexturePreviewPanel in IMGUIContainer (300 px), Luna settings (Format, Max size dropdowns), Size (source, Unity import, Luna output, In playable (was X, unsaved), Last build), Used by (BlastRadius chain, cached per path, cleared on Refresh), Add to Luna Exclusions…"
│     removed: "Preview toggle + EditorPrefs ShowPreview, per-tab Save/Discard/Undo (header has them), MultiColumnHeaderState"
│     contracts: "Cells live in a container with userData = entry (EntryOf walks parents); callbacks registered once in makeCell; bindCell sets value WithoutNotify. Every data change ends in ApplySortAndFilter -> RefreshUI (table, summary, bulk bar, toolbar enabled states, details)"
│     verified: "Compiles first try; 128 rows, 11 columns; selecting T_Hammer_Albedo02 fills details (2048×2048 1.70 MB, webp 256×256 8.1 KB, 10.9 KB in playable, last build 12.2 KB); details Format -> PNG made 1 pending: 'In playable 17.9 KB (was 10.9 KB, unsaved)', header '1 unsaved change(s)', tab 'Textures (1)'; DiscardPending -> 0; luna.json unchanged; no tool errors"
│     not_verified: "Clicks/visual look (column widths, dropdown cells, split drag, light skin); sorting by clicking headers; viewDataKey persistence across reloads; Pick smallest / Optimize sources from new toolbar"
├── previews: {state: pending}
├── used-by-inline: {state: pending, note: "Reuses BlastRadius engine; ExcludeReviewWindow becomes the confirm step or is replaced"}
└── simple-mode-ux:  # 2026-11-10
│     state: built (compiles offline against Unity 2022.3.19f1 dlls; NOT opened in Unity: MCP disconnected)
│     trigger: "User: remove Source px / Alpha / Type from textures; remove Exported + Length from sounds and make Trim editable; add Simple/Manual modes; improve UI/UX freely"
│     decisions: "Mode switch = segmented Simple | Manual at the header's left, EditorPrefs LunaOptimizer.SimpleMode (default Simple). Simple hides the tab bar and the header budget (its own size card shows it). Simple enables Textures + Sounds views (scan on open). Every Simple action is a pending change like Manual (Save all writes)"
│     simple_ui: "Pending banner (count, predicted saving, Review / Discard / Save all to Luna); Playable size card (26 px total, of cap, meter green/orange/red, % + left/over, freshness, Quick predict = Overview Measure / Cancel, last error); Textures card (count + predicted, Compression preset Balanced 80/80 · Smaller 70/65 · Smallest 55/50 (EditorPrefs LunaOptimizer.SimplePreset), Shrink texture formats = Pick smallest on ALL textures at the preset, Predict (only while some lack a prediction), Unity-compressed note, Open Textures tab); Sounds card (Trim silence, Make all mono, Predict, Open Sounds tab); What ships (category rows: name, count, bar vs largest, size, share; ≈ when estimated; click -> Manual tab: texture/sprite -> Textures, sound/audio -> Sounds, else Overview)"
│     textures: "Columns now: Pick, Texture, File, Unity compression, Luna format, Max size, Predicted (shows −/+ delta vs saved when unsaved), Last build. Optimize sources / Restore originals moved into a 'Source files' ToolbarMenu. Details: Source = px · bytes, Unity import = compression (orange + tooltip when it affects Luna)"
│     sounds: "Columns now: Pick, Sound, Source, Format, Bitrate, Channels, Trim (s) [start FloatField – end FloatField, isDelayed, 0.00], Predicted, Last build, Listen. Details 'Listen & trim': source waveform with shaded cut parts, MinMaxSlider (debounced commit after drag ends, like the quality slider), Start/End fields, 'keeps X of Y s', Trim silence, Keep whole clip. Bulk bar: Trim silence for ticked"
│     trim_contract: "SoundTrim: Luna format startTime/endTime 'MM:SS.mmm', lengthInSeconds = (float)round(end-start,3); whole clip -> both '00:00.000', length 0. Silence = all channels below 0.003 (~-50 dB), 30 ms padding, never extends an existing trim. SaveSoundRules now also writes startTime/endTime/lengthInSeconds (Convert.ChangeType to Luna's field types). ChangeRules equality includes trim (SameTrim). Opus scripts pick the trim up via RefreshScript"
│     scripts: [SimpleModeView.cs (new), SoundTrim.cs (new), LunaOptimizerWindow.cs, TextureOptimizerView.cs, SoundOptimizerView.cs, SizeDashboardView.cs (CategorySummary + MeasureNow/CancelMeasure/IsMeasuring/TotalBytes/Limit/LastMessage), LunaSettingsWriter.cs, UI/LunaOptimizer.uss (segmented switch, lo-primary, underline tabs, cards, meter, category bars, trim cell/shade)]
│     compile_check: "scratchpad cc.sh: dotnet + Unity DotNetSdkRoslyn csc, refs = Managed/UnityEngine/*.dll (NOT Managed/UnityEditor.dll: duplicate types) + unity-4.8-api; Editor/**/*.cs compiles with 0 errors"
│     not_verified: "Anything in Unity: layout/USS look (dark + light), mode switch, Simple actions, trim fields/slider/shading, Trim silence results, saving trim to Luna (field names/types on Luna's SoundOverride assumed = luna.json keys)"
│     next: "User opens Luna Optimizer: check Simple mode, then Sounds trim (edit, Save all, verify luna.json via Luna window)"
│     manual_polish_2026_11_10: "User: 'improve UI on the manual window, design like a pro'. Tab bar = custom elements (built-in icon via EditorGUIUtility.FindTexture, no console error if missing; title; accent pending badge), header pending = badge. Shared OptimizerUI blocks: IconButton, StatStrip/AddStat chips (replace summary sentences; key = green, unsaved delta = blue), Badge (accent/warn), DetailsHeader (title, path link, badges: Override/Default, output format, Unsaved, Compressed import / Decoded import, Trimmed, Excluding), Metric (big 'in playable' + −/+ unsaved delta pill), SizeCell + SetHeat (Predicted column: text over a bar vs the largest listed), Delta/Bytes. Bulk bar is contextual: hint until rows ticked, then tinted bar with 'N selected', fields, Apply (primary), Clear. Details: tinted side panel, padded, uppercase small section titles, ruled info rows, actions row (Show in Project, Exclude from build…). Rows 24 px. Applied to Textures, Sounds, Models & Other; Overview stays IMGUI"
│     polish_round2_2026_11_10: "User: redesign + reposition search, improve Used by readability, Overview content kept but redesigned. SearchBar.cs (rounded box, icon, placeholder, × clear, Esc, 'N of M', Ctrl/Cmd+F) moved from the toolbar to the row above each table (left), bulk controls/hint on its right; 'all shown' checkbox moved into the tick column header (Column.makeHeader). Size column = 34 px track + fill (sqrt scale) left of the number. Tab icons via EditorGUIUtility.ObjectContent(null, type) (FindTexture names for Texture/AudioClip do not exist). Details padding via contentContainer class (descendant selector into ScrollView did not match). Used by = callout 'If excluded', 'Ships because of' root chips, tree (cached icon, bold name, short folder, 'uses it in' code chip, guide line per depth). Overview: CreateUI frame (IMGUI toolbar container + shared SearchBar + IMGUI body), OverviewStyles.cs (cards, hero 24 px total + meter, section titles with muted notes, rows with hover, slim accent bars, size bold + share %), category notes muted and 'X not built:' shortened to '≈'; Measure tinted accent. Hub RepaintContent marks nested IMGUIContainers. Guards: AssetsView.HasInteractivePreview (AnimationClipEditor.HasPreviewGUI throws after reload → window was blank), hub CreateTabUI shows a HelpBox when a tab throws"
│     verified_round2: "Seen in Unity via window capture (InternalEditorUtility.ReadScreenPixel, snippet .ai-game-dev/snippets/lo/LoSnap.cs): Simple mode, Textures (stats chips, search row, details header/metric/Used by tree), Sounds (trim cells, waveform shading + slider, badges), Models & Other list, Overview cards. No console errors after reload"
│     browser_2026_11_10: "User: run the tool in a browser while Unity is open (AX Hero step 02). LunaOptimizerWebServer.cs: HttpListener on 127.0.0.1:8790 (+9 fallbacks; 8787 taken on this Mac), menu Supercent/Luna Optimizer (Browser) + Stop; requests queued from a background thread, handled on EditorApplication.update; per-start key injected into the page (__LO_TOKEN__), required as X-LO-Token or ?key= (thumbs); Host must be 127.0.0.1/localhost; /ping (1×1 PNG, no key) for the launcher; auto-restart after reload, per-project EditorPrefs 'LunaOptimizer.WebServer.'+project path. API: /api/state (size, categories, groups, texture + sound rows), /api/action (measure, cancel-measure, set-preset, shrink, predict-*, trim-silence, mono, save, discard, show, ping), /api/thumb (GPU blit 64 px PNG, cached). Page Editor/Web/LunaOptimizer.html (inline CSS/JS, polls 2 s, dark/light). Launcher Web~/Open Luna Optimizer.html (open before Unity; probes ports via Image; redirects or lists several projects). Hub accessors: LunaOptimizerWindow.Hub/OverviewView/TexturesView/SoundsView/ShowTab/SaveAllPending; views' Entries; SimpleModeView presets static"
│     verified_browser: "Real Unity: /api/state 127 textures, 14 sounds, 17 categories, 2.65/5.00 MB ironsource; no key -> 403; thumbnail 200; after a recompile the server came back on 8790 by itself and /ping 200. Headless Chrome capture of the real page OK. Launcher tested against a mock server (wait card, then redirect). Actions (shrink/trim/save) from the browser NOT clicked yet"
│     port_conflict_fix: "User saw a black screen: a Luna build preview (python -m http.server 8790, from a SmashDown session) listened on [::]:8790 while our HttpListener took 127.0.0.1:8790, so the browser split requests between the two. Fix: FIRST_PORT 47790 (launcher probes 47790-47799) and InUse() skips any port that answers on 127.0.0.1 or ::1. Auto-restart moved from delayCall (did not fire while Unity was in the background) to a direct Start() in the static constructor; verified: after a recompile the server is back on 47790 with no focus"
│     browser_full_2026_11_10: "User: bring all features into the browser page. LunaOptimizerWebApi.cs (new): GET /api/state (header, groups, texture + sound rows with their dropdown choices), /api/overview (SizeDashboardView.BrowserData: categories+assets, scripts files/namespaces/C# files/types, modules, what-if, excluded, networks, auto, speed-up), /api/texture + /api/texture-image (LunaPipeline.RenderPreview input/output PNG), /api/sound + /api/audio (source file or Luna output), /api/sound-bitrates, /api/assets + /api/asset + /api/asset-preview (AssetPreview, 202 while rendering), /api/exclusions (BlastRadius pending/excluded, cached by queue+luna.json time), /api/history; POST /api/action ~40 actions. Dialog paths split so the page confirms first ({confirm:text} then confirmed:true): Opus risks, module Apply, Undo, stale sound fix; Optimize/Restore sources still confirm in Unity. MiniJson.cs (recursive JSON). View APIs: TextureOptimizerView (FormatChoices/PickFormat/SizeLabels/PickSize/SetQuality/ApplyBulk→ApplyBulkCore returns note/PickSmallest/RenderPreview), SoundOptimizerView (PickFormat NEEDS_CONFIRM, BitrateLabels/ChannelLabels/Pick*, SetTrimSeconds, TrimSilence(paths), ApplyBulk core, LunaAudioPath, StaleRepairPlan/RepairStale), SizeDashboardView (BrowserData, Locate, SetNetwork/SetAuto/SetSpeedUp/PlanModule/ResetPlan/MeasureWhatIf/ApplyPlanConfirmed/ReInclude), AssetsView.BrowserRows, LunaExclusionWindow.ApplyPending (static) + ReloadOpen, LunaSaveHistory.RevertMessage/Revert, LunaOptimizerWindow ScenePaths/ChangeBuildSceneTo/AssetsViewAttached/ReloadAll. Page: Home, Overview, Textures, Sounds, Models & Other, Exclusions (hash routes; #view/assetPath deep-links open the drawer), header scene picker + Undo popover, sortable/searchable tables with inline selects, contextual bulk bars, drawer (texture compare checker/black/white + smooth/pixels, quality slider; sound players, Web Audio waveform with dual trim slider; asset preview; Used by tree), floating save bar, confirm modal"
│     verified_browser_full: "Real Unity, headless Chrome: all 6 views + 3 drawers render, no console errors. Via API: tex-format (WebP 39.3 KB vs PNG 23.5 KB), snd-trim (−4.2 KB), Opus asks to confirm, excl add/remove, discard -> all back; luna.json hash unchanged. NOT exercised (they write luna.json): Save, Undo revert, scene switch, Apply exclusions, module Apply, Optimize/Restore sources"
│     ax_hero: "Docs/AXHero: capture-browser.png, capture-unity.png, build_introduction.py -> introduction.html (0.88 MB, 5 sections, images Base64, no external refs; facts from the Notion report)"
│     gotcha: "Editing files while Unity is open triggers an auto-compile that can run before a NEW .cs is imported (CS0246/CS0103): Refresh + RequestScriptCompilation again"
```

### `shared-package` {#shared-package}
```yaml
state: done (2026-11-06)
decision: "User: put the tool in the shared resources folder for all agents/models; a skill a cheap agent runs to live-link a Unity project; keep working in the shared source"
done: "cp -Rp to Personal_resources/Unity_Packages (diff -r identical), package.json -> displayName 'Luna Optimizer' 0.2.0 + new description (name unchanged); this project's manifest -> absolute file: path, Unity re-resolved (resolvedPath new); luna.json unity.scripts.msbuild/msbuildMac64 (compile cache) -> new Tools~ path via Luna memory + Save (1 line changed); old folder replaced by symlink to the new one"
skill: "luna-optimizer-link: checks Unity >= 2022.3, finds Luna (file: dependency whose parent has config.json + pipeline/) and warns if version != 7.2.0, inserts the dependency alphabetically, keeps the rest of the manifest byte-identical (tested: unlink+link == original; real manifest == json.dumps(indent=2)); refuses Unity 2021"
constraints: "Compile cache stores an absolute script path in each project's luna.json; moving the package again requires updating unity.scripts.msbuildMac64 (via Luna memory) in every project that enabled it"
```

### `qol-backlog` {#qol-backlog}
```yaml
spec_ref: "Spec#19 Quality-of-life backlog (proposed)"
state: in-progress (2026-11-03 user picked undo-last-save first)
agent_top3: [undo-last-save, budget-bar, bulk-presets]
├── undo-last-save:
│     state: built (2026-11-03), UI not clicked by agent
│     decision: "2026-11-03 user: history list (not single level, not whole luna.json). Per section: textures / sounds / modules; last 10 each"
│     scripts: [LunaSaveHistory.cs (new: LunaSettingsSection, Record, For, Add, ShowRevertMenu, ConfirmRevert), LunaSettingsWriter.cs (all writes via Edit(); RestoreSection, TryReadSection, SameSection; LunaState.SectionJson/RestoreSection/ModulesKey/Unity), LunaSoundOptimizerWindow.cs, LunaTextureOptimizerWindow.cs, LunaSizeDashboardWindow.cs (Undo… buttons)]
│     storage: "Library/LunaTextureOptimizer/save-history.json (per project, not shared). Record: section, savedAt (ISO), label, detail (names), before, after (section JSON)"
│     contracts: "Every write (SaveRules, SaveSoundRules, RepairSoundOverrides, SaveModules, RestoreSection) goes through LunaSettingsWriter.Edit: refuse if Luna memory != file -> before = section JSON -> edit -> after -> luna.Save -> record if changed"
│     solution: "Textures/Sounds: JsonUtility.ToJson(section) + FromJsonOverwrite (ISerializationCallbackReceiver rebuilds overridesDictionary). Modules: snapshot = whole 'unity' block JSON; restore copies only enableMecanimWASM + unusedModules from an uninitialized Unity instance; compare via ModulesKey (ignores other unity fields)"
│     ux: "Undo… dropdown lists saves newest first ('Undo MM-dd HH:mm:ss <label>'). Confirm dialog: shows detail; picking an older one says it also undoes N later saves; warns if section changed after the newest save (Luna window / other tool). Undo is recorded too (redo = undo the undo)"
│     verified: "Compiles. In memory (no file write): each section mutate -> RestoreSection -> Luna memory == luna.json again; SameSection false on change, modules key ignores extra unity keys; history keeps newest 10 per section, sections separate (scratch dir)"
│     not_verified: "UI clicks (dropdown, dialogs); a real Save/Undo written to luna.json; texture-tool Optimize sources is separate (own Backup/Restore originals), not in this history"
│     constraints: "Only the touched section is restored; other Luna settings untouched. History is in Library -> lost on Library delete; not shared between machines"
│     next: "User: in the Sound Optimizer change one sound, Save to Luna, then Undo… -> check luna.json goes back (and the Opus BGM rule stays unless that save is undone)"
├── bulk-edit:
│     state: built (2026-11-05), UI not clicked by agent
│     decision: "2026-11-05 user: checkbox column (row click still locates asset); bulk first, presets as a later slice"
│     scripts: [LunaSoundOptimizerWindow.cs, LunaTextureOptimizerWindow.cs]
│     ux: "Pick column (22 px, not sortable) + bar above table: [All shown] 'N ticked (+M hidden)' | keep-or-value popups | Apply to N | Untick all. Only ticked rows the search shows change. Nothing written until Save to Luna (then Undo… covers it)"
│     sound: "Format keep/MP3/Ogg Opus/WebM Opus; Bitrate keep/list (chosen format's list, else union of ticked formats); Channels keep/Mono/Stereo/As exported. Opus risk dialog once per bulk apply. Custom-script sounds skipped; bitrate invalid for a sound's format kept -> one summary dialog"
│     texture: "Format keep/PNG best/WebP q80/JPEG q80 (JPEG skipped for alpha, summary dialog); Max size keep/Full (own largest side)/2048..32. Same-output changes collapse to 'no change' (SameResult)"
│     contracts: "ChangeRule(entry, change) -> ChangeRules(targets, (entry, rule) => …): one pass, then missing predictions in one PredictMany (progress bar if >1). ChangeFormat split into ConfirmFormat + ApplyFormat (sound)"
│     verified: "Compiles; windows draw without errors. In memory: sound 3 rows -> 48k mono pending with predictions (GameEnd 13.6->10.3 KB); texture 3 rows -> WebP max 256 pending with predictions; Refresh discards; luna.json unchanged"
│     not_verified: "UI clicks (ticks, All shown, popups, Apply); dialog paths (Opus confirm, skipped-bitrate / JPEG-alpha summaries)"
│     next: "User tries it; then presets slice (built-in vs saved: ask)"
├── lossy-quality:
│     state: built (2026-11-06), toolbar typing / slider drag not clicked by user
│     decision: "2026-11-06 user: WebP q / JPEG q toolbar fields + per-texture quality slider in the details panel"
│     scripts: [TextureOptimizerView.cs (QualityField, BulkFormatChoices, FormatOptions, ShownOptionsFor, QualitySlider, CommitQuality), UI/LunaOptimizer.uss (.lo-quality-field)]
│     ux: "Toolbar 'WebP q' / 'JPEG q' (1-100, EditorPrefs LunaOptimizer.WebpQuality/JpegQuality, default 80) drive Pick smallest, bulk Format list and switching a texture to WebP/JPEG. A texture already in WebP/JPEG shows its own quality in its dropdown. Details 'Quality' slider shown when the texture ships lossy (incl. png rule that ships as jpeg); predicts + re-renders preview 350 ms after the value rests and the drag ended"
│     facts: "alpha check 2026-11-06: JPEG only offered when HasAlpha false (alphaSource FromGrayScale, or FromInput && DoesSourceTextureHaveAlpha); pixel scan found 0 'no alpha' PNGs with any alpha<255 (themepark 78, smashdown 389 transparent PNGs none with alphaSource None or DXT1/RGB24 override). Luna itself still forces JPEG for alphaSource None or imported DXT1/RGB24 ('ships as jpeg')"
│     verified: "Compiles; in hub: fields render 80/80; WebP q 75 -> bulk 'WebP q75', pref saved; cloud_2x2_soft keeps 'WebP q80' (no JPEG: alpha); slider 60 -> rule q60 pending, 18,544 -> 17,797 B; discarded, pref restored, no console errors"
│     constraints: "ApplyBulk checks NeedsOpaque/HasAlpha directly (option lists are rebuilt per call, so no reference Contains)"
├── both_tools:
│     undo-last-save: "Back up Luna sound/texture settings before Save to Luna; 'Revert last save' button (safety net while save paths are untested)"
│     budget-bar: "Target size (network cap) -> predicted total, headroom, savings from unsaved changes"
│     bulk-presets: "Multi-select rows + apply one setting; presets e.g. 'Short SFX -> Opus 24k mono', 'UI texture -> max 512'"
│     post-build-check: "After a build compare predicted vs actual per asset; highlight mismatches (catches Luna behavior changes on updates)"
│     biggest-wins-sort: "Per row: bytes saved by one step down (96k->64k, 1024->512); sort by it"
├── sound_tool:
│     silence-trim: "Detect leading/trailing silence, suggest trim (still shrinks size)"
│     duplicate-check: "Hash exported audio; Stack.mp3 and Unstack.mp3 give identical sizes at every setting -> maybe same audio shipped twice"
│     source-warnings: "Flag embedded cover art/metadata (ButtonPress_2.wav has 1024px art) and non-wav clips whose Load Type breaks prediction"
│     ab-listen: "Switch Source/Luna at the same position, loop toggle"
└── texture_tool:
      texture-preview: "= Spec#17 (original vs Luna output side by side, zoom)"
      asset-removal: "= Spec#18 (unused shipped assets, big new assets since last build)"
```
