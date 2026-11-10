using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // The playable's size as Luna counts it, without a Luna build: Luna's own statistics task run on the last
    // build output, with scripts split by file and C# type, budgets per ad network, and module what-ifs.
    [Serializable]
    public class SizeDashboardView : OptimizerView
    {
        const string LAST_MEASUREMENT = LunaStatsRunner.WORK_FOLDER + "/last.json";
        const string WHAT_IF_FOLDER = LunaStatsRunner.WORK_FOLDER + "/whatif";
        const string NETWORK_PREF = "LunaSizeDashboard.Network";
        const string MECANIM_WASM = "mecanim-wasm";
        const int PREVIEW = 20;   // rows shown before "Show all" 
        const float BAR_HEIGHT = 18f;
        static readonly Regex MECANIM_SETTING = new Regex("(\"enableMecanimWASM\":\\s*)(true|false)");

        [Serializable]
        class LunaJson
        {
            public UnitySection unity;
        }

        [Serializable]
        class UnitySection
        {
            public bool enableMecanimWASM;
            public List<ModuleEntry> unusedModules = new List<ModuleEntry>();
        }

        [Serializable]
        class ModuleEntry
        {
            public string name;
            public bool excluded;
            public bool forceIncluded;
        }

        [Serializable]
        class ManifestEntry
        {
            public string src;
            public string ifModule;
        }

        class AssetRow
        {
            public string title;
            public string path;
            public double size;
            public string tag;   // live, new, changed, or null (last build, unchanged)
        }

        class CategoryRow
        {
            public string name;
            public double size;
            public bool live;
            public int changed;
            public int estimatedFiles;   // scripts: files changed since the exact measure (size is an estimate)
            public bool estimatedDeserializers;
            public string sceneEstimate;  // scene name when sizes come from SceneSizeEstimator (scene not built)
            public int remembered;
            public int estimated;
            public List<AssetRow> assets = new List<AssetRow>();
        }

        AssetSizeMemory sizeMemory;
        string recordedMeasurement;           // measuredAt of the measurement already added to the size memory
        string estimateKey;                   // scene|network|luna.json time|measurement of the cached estimate
        List<SceneSizeEstimator.Category> estimate;

        List<CategoryRow> rows = new List<CategoryRow>();
        List<string> forcedExcludes = new List<string>();   // plain Assets/ paths in unity.assets.excludes (not patterns or Luna's Stubs/ defaults)
        double composedTotal;
        DateTime buildOutputTime;

        LunaInstall install;
        string projectRoot;
        LunaMeasurement current;
        LunaMeasurement whatIf;
        LunaStatsRunner runner;
        bool runnerIsWhatIf;
        string message;
        MessageType messageType;
        Dictionary<string, KeyValuePair<long, long>> networks = new Dictionary<string, KeyValuePair<long, long>>();
        string[] networkNames = new string[0];
        int networkIndex;
        bool savedMecanimWasm;
        Dictionary<string, bool> savedModules = new Dictionary<string, bool>();
        bool plannedMecanimWasm;
        Dictionary<string, bool> plannedModules = new Dictionary<string, bool>();
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly HashSet<string> showAll = new HashSet<string>();
        string filter = string.Empty;
        Dictionary<string, string> typeFiles;   // C# type -> declaring file, for locating types
        [NonSerialized] GUIStyle linkLabel;
        List<TextureEntry> liveTextures = new List<TextureEntry>();
        string pendingFingerprint;
        bool pendingStatsOnly;
        List<ScriptFileState> pendingFiles;
        List<ScriptFileState> currentFiles;   // .cs files as of the last refresh (for the instant estimate)
        bool autoRefresh = true;
        double nextWatch;
        string watchStamp;
        double changeSeenAt = -1;
        const double WATCH_INTERVAL = 2.0;
        const double SETTLE_SECONDS = 1.5;
        const string AUTO_PREF = "LunaSizeDashboard.Auto";
        Vector2 scroll;
        [NonSerialized] GUIStyle rightLabel;   // GUIStyles come back colourless (black text) from a script reload: rebuild instead
        [NonSerialized] GUIStyle wrapLabel;


        public override string Title
        {
            get { return "Overview"; }
        }

        public override void OnEnable()
        {
            projectRoot = Path.GetDirectoryName(Application.dataPath);
            install = LunaInstall.Find();
            if (install != null)
            {
                networks = LunaStatsRunner.ReadNetworks(install);
                networkNames = networks.Keys.OrderBy(n => n).ToArray();
                networkIndex = Mathf.Max(0, Array.IndexOf(networkNames, EditorPrefs.GetString(NETWORK_PREF, "applovin")));
            }
            var last = Path.Combine(projectRoot, LAST_MEASUREMENT);
            if (File.Exists(last))
                current = JsonUtility.FromJson<LunaMeasurement>(File.ReadAllText(last));
            ReadModuleSettings();
            autoRefresh = EditorPrefs.GetBool(AUTO_PREF, true);
            currentFiles = current != null ? ScriptSizeEstimator.ScanFiles(projectRoot) : null;
            Compose();
            EditorApplication.update += Watch;
        }

        public override void OnDisable()
        {
            EditorApplication.update -= Watch;
            EditorApplication.update -= Poll;
            if (runner != null)
                runner.Cancel();
        }

        // Set by the hub when the last Luna build was of another scene than the one Luna builds now.
        [NonSerialized] public string StaleSceneNote;

        // After a scene switch, an undo or an exclusion: what ships may have changed.
        public void ReloadSettings()
        {
            ReadModuleSettings();
            if (install != null)
                RefreshTextures();
            Compose();
            Repaint();
        }

        void ReadExcludes()
        {
            var settings = LunaSettings.Load(projectRoot);
            forcedExcludes = settings != null ? settings.Excludes.Where(e => e.StartsWith("Assets/", StringComparison.Ordinal) && !e.Contains("*")).OrderBy(e => e, StringComparer.Ordinal).ToList() : new List<string>();
        }

        void ReadModuleSettings()
        {
            ReadExcludes();
            var path = Path.Combine(projectRoot, LunaSettings.FILE_NAME);
            if (!File.Exists(path))
                return;
            var json = JsonUtility.FromJson<LunaJson>(File.ReadAllText(path));
            savedMecanimWasm = json.unity != null && json.unity.enableMecanimWASM;
            savedModules = json.unity != null && json.unity.unusedModules != null
                ? json.unity.unusedModules.Where(m => m.name != MECANIM_WASM).ToDictionary(m => m.name, m => !m.excluded)
                : new Dictionary<string, bool>();
            plannedMecanimWasm = savedMecanimWasm;
            plannedModules = new Dictionary<string, bool>(savedModules);
        }

        // ---------------------------------------------------------------- measuring

        // Live: textures from the current settings (Luna's texture pipeline, cached), scripts compiled from the
        // current code in the sandbox (skipped when nothing script-related changed), the rest from the last build.
        void Measure(bool isWhatIf)
        {
            SetMessage(null, MessageType.None);
            string error;
            string projectPath;
            if (isWhatIf)
            {
                if (!PrepareWhatIf(out projectPath))
                    return;
            }
            else
            {
                RefreshTextures();
                pendingFiles = ScriptSizeEstimator.ScanFiles(projectRoot);
                currentFiles = pendingFiles;
                var roots = LiveScriptsSandbox.ScriptRoots(projectRoot);
                pendingFingerprint = LiveScriptsSandbox.ScriptsFingerprint(projectRoot, install, pendingFiles, roots);
                var scriptsUnchanged = current != null && current.live && current.stats != null && current.scriptsFingerprint == pendingFingerprint
                    && Directory.Exists(LiveScriptsSandbox.ProjectPath(projectRoot));
                if (scriptsUnchanged && current.network == SelectedNetwork)
                {
                    current.measuredAt = DateTime.Now.ToString("o");
                    SaveCurrent();
                    Compose();
                    SetMessage("Scripts unchanged since the last measure: reused. Textures updated live.", MessageType.Info);
                    return;
                }
                // Same scripts, other network: only Luna's statistics need to run again.
                pendingStatsOnly = scriptsUnchanged;
                if (!pendingStatsOnly && !LiveScriptsSandbox.Prepare(projectRoot, roots, out error))
                {
                    SetMessage(error, MessageType.Error);
                    return;
                }
                projectPath = LiveScriptsSandbox.ProjectPath(projectRoot);
            }

            var compile = !isWhatIf && !pendingStatsOnly;
            runner = LunaStatsRunner.Start(install, projectRoot, projectPath, isWhatIf ? DescribePlan() : SelectedNetwork, compile, SelectedNetwork, out error);
            runnerIsWhatIf = isWhatIf;
            if (error != null)
                SetMessage(error, runner == null ? MessageType.Error : MessageType.Warning);
            if (runner != null)
                EditorApplication.update += Poll;
        }

        // Auto: when scripts, luna.json or shipped textures change and then settle, update the instant estimate and
        // textures right away, then start the exact measurement in the background.
        void Watch()
        {
            if (!autoRefresh || install == null || current == null || EditorApplication.timeSinceStartup < nextWatch
                || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            nextWatch = EditorApplication.timeSinceStartup + WATCH_INTERVAL;
            var stamp = WatchStamp();
            if (watchStamp == null)
            {
                watchStamp = stamp;
                return;
            }
            if (stamp != watchStamp)
            {
                watchStamp = stamp;
                changeSeenAt = EditorApplication.timeSinceStartup;
                return;
            }
            if (changeSeenAt < 0 || EditorApplication.timeSinceStartup - changeSeenAt < SETTLE_SECONDS)
                return;
            currentFiles = ScriptSizeEstimator.ScanFiles(projectRoot);
            RefreshTextures();
            Compose();
            Repaint();
            // A change made while a measurement runs stays pending and starts the next one when it finishes.
            if (runner != null)
                return;
            changeSeenAt = -1;
            Measure(false);
        }

        // Cheap change detector: names, sizes and times of every .cs file, luna.json, and the shipped textures.
        string WatchStamp()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var file in Directory.GetFiles(Path.Combine(projectRoot, "Assets"), "*.cs", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                builder.Append(file).Append(info.Length).Append(info.LastWriteTimeUtc.Ticks);
            }
            builder.Append(File.GetLastWriteTimeUtc(Path.Combine(projectRoot, LunaSettings.FILE_NAME)).Ticks);
            // Scene edits change which scripts Luna compiles (and which assets ship).
            var settings = LunaSettings.Load(projectRoot);
            if (settings != null)
            {
                foreach (var scene in settings.Scenes)
                    builder.Append(File.GetLastWriteTimeUtc(Path.Combine(projectRoot, scene)).Ticks);
            }
            foreach (var entry in liveTextures)
            {
                var full = Path.Combine(projectRoot, entry.AssetPath);
                builder.Append(File.GetLastWriteTimeUtc(full).Ticks).Append(File.GetLastWriteTimeUtc(full + ".meta").Ticks);
            }
            return builder.ToString();
        }

        // Every texture the build ships, with Luna's predicted size for the CURRENT settings.
        void RefreshTextures()
        {
            var settings = LunaSettings.Load(projectRoot);
            if (settings == null)
                return;
            liveTextures = BuildTextureScanner.Scan(projectRoot, settings, null);
            var cache = new PredictionCache(projectRoot);
            var missing = new List<TextureEntry>();
            foreach (var entry in liveTextures)
            {
                entry.Prediction = cache.Get(PredictionCache.KeyFor(entry, projectRoot, install));
                if (entry.Prediction == null)
                    missing.Add(entry);
            }
            var predictions = LunaPipeline.PredictMany(missing, install, projectRoot, cache, "Predicting textures");
            for (int i = 0; i < missing.Count; i++)
            {
                missing[i].Prediction = predictions[i];
                if (predictions[i] != null)
                    cache.Set(PredictionCache.KeyFor(missing[i], projectRoot, install), predictions[i]);
            }
            cache.Save();
        }

        void SaveCurrent()
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, LunaStatsRunner.WORK_FOLDER));
            File.WriteAllText(Path.Combine(projectRoot, LAST_MEASUREMENT), JsonUtility.ToJson(current));
        }

        void Poll()
        {
            if (runner == null)
            {
                EditorApplication.update -= Poll;
                return;
            }
            Repaint();
            if (!runner.IsDone)
                return;

            EditorApplication.update -= Poll;
            string error;
            var measurement = runner.Finish(runnerIsWhatIf ? Path.Combine(projectRoot, WHAT_IF_FOLDER) : projectRoot, out error);
            if (measurement != null)
                measurement.network = SelectedNetwork;
            if (measurement != null && !runnerIsWhatIf)
            {
                measurement.live = true;
                measurement.noBuild = !LiveScriptsSandbox.HasBuildOutput(projectRoot);
                measurement.scriptsFingerprint = pendingFingerprint;
                measurement.scriptFiles = pendingFiles;
                double unmapped;
                measurement.fileScripts = ScriptSizeEstimator.Attribute(measurement, projectRoot, out unmapped);
                measurement.unmappedScripts = unmapped;
            }
            runner = null;
            if (error != null)
                SetMessage(error, measurement == null ? MessageType.Error : MessageType.Warning);
            if (measurement == null)
                return;

            if (runnerIsWhatIf)
            {
                whatIf = measurement;
                return;
            }
            current = measurement;
            whatIf = null;
            SaveCurrent();
            Compose();
        }

        // A project folder holding a copy of luna.json with the planned module settings, and a link to the real
        // build output. Luna's statistics task only reads both.
        // A clone of the live sandbox (current scripts already compiled) with the planned module settings.
        bool PrepareWhatIf(out string projectPath)
        {
            projectPath = Path.Combine(projectRoot, WHAT_IF_FOLDER);
            var live = LiveScriptsSandbox.ProjectPath(projectRoot);
            if (current == null || !current.live || !Directory.Exists(live))
            {
                SetMessage("Measure first: the what-if starts from the live measurement.", MessageType.Warning);
                return false;
            }
            if (Directory.Exists(projectPath))
                LiveScriptsSandbox.SafeDelete(projectPath, Path.Combine(projectRoot, LunaStatsRunner.WORK_FOLDER));
            string error;
            if (!ProcessRunner.Run("/bin/cp", new[] { "-c", "-R", live, projectPath }, out error))
            {
                SetMessage("Could not prepare the what-if copy: " + error, MessageType.Error);
                return false;
            }
            var text = File.ReadAllText(Path.Combine(projectRoot, LunaSettings.FILE_NAME));
            text = MECANIM_SETTING.Replace(text, "${1}" + (plannedMecanimWasm ? "true" : "false"));
            foreach (var module in plannedModules)
            {
                var entry = new Regex("(\"name\":\\s*\"" + Regex.Escape(module.Key) + "\",\\s*\"excluded\":\\s*)(true|false)(,\\s*\"forceIncluded\":\\s*)(true|false)");
                text = entry.Replace(text, "${1}" + (module.Value ? "false" : "true") + "${3}" + (module.Value ? "true" : "false"));
            }
            File.WriteAllText(Path.Combine(projectPath, LunaSettings.FILE_NAME), text);
            return true;
        }

        void ApplyPlan()
        {
            if (!EditorUtility.DisplayDialog("Apply to Luna", PlanConfirmation, "Apply", "Cancel"))
                return;
            ApplyPlanCore();
        }

        string PlanConfirmation
        {
            get
            {
                return "Save these engine settings to Luna?\n\n" + DescribePlan()
                    + "\n\nRemoving a module the game uses breaks it (missing physics, particles or animation). Playtest the next build.";
            }
        }

        void ApplyPlanCore()
        {
            string error;
            if (!LunaSettingsWriter.SaveModules(plannedMecanimWasm, plannedModules, projectRoot, out error))
            {
                SetMessage("Nothing was saved. " + error, MessageType.Error);
                return;
            }
            ReadModuleSettings();
            SetMessage("Saved. The numbers above update after the next Luna build and Measure.", MessageType.Info);
        }

        void UndoneModules()
        {
            ReadModuleSettings();
            whatIf = null;
            SetMessage("Undone. The numbers above update after the next Luna build and Measure.", MessageType.Info);
        }

        // ---------------------------------------------------------------- exclude from build

        void ExcludeProjectSelection()
        {
            var paths = Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToList();
            if (paths.Count == 0)
            {
                SetMessage("Select files or folders in the Project window first, then click Exclude selection….", MessageType.Info);
                return;
            }
            LunaExclusionWindow.AddAndShow(paths);
        }

        // Playable bytes the last measure attributed to this asset (Luna lists some assets by name only).
        public double SizeOf(string assetPath)
        {
            double total = 0;
            foreach (var row in rows)
            {
                foreach (var asset in row.assets)
                {
                    if (asset.path == assetPath || (string.IsNullOrEmpty(asset.path) && Locatable(asset.path, asset.title, row.name) == assetPath))
                        total += asset.size;
                }
            }
            return total;
        }

        // Plain paths in Luna's exclude list: assets/folders left out on purpose (here or in the Luna window). Patterns like **/Editor/ are Luna's own.
        void DrawExcluded()
        {
            EditorGUILayout.BeginHorizontal();
            OverviewStyles.Section("Excluded from the build", forcedExcludes.Count.ToString());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Open Exclusions", "Pending exclusions, what uses them, and what is excluded now."), GUILayout.Width(110f)))
                LunaExclusionWindow.Open();
            if (GUILayout.Button(new GUIContent("Undo…", "Put Luna's asset exclusions back to before one of the last changes made here."), EditorStyles.popup, GUILayout.Width(70f)))
                LunaSaveHistory.ShowRevertMenu(GUILayoutUtility.GetLastRect(), LunaSettingsSection.AssetExcludes, projectRoot, () =>
                {
                    ReadExcludes();
                    SetMessage("Undone. The numbers above update after the next Luna build and Measure.", MessageType.Info);
                });
            EditorGUILayout.EndHorizontal();
            if (forcedExcludes.Count == 0)
            {
                EditorGUILayout.LabelField("Nothing excluded. Right-click an asset in any tab (Add to Luna Exclusions), review it in the Luna Exclusions window, then Apply.", wrapLabel);
                return;
            }
            foreach (var path in forcedExcludes.ToList())
            {
                EditorGUILayout.BeginHorizontal();
                var exists = AssetDatabase.IsValidFolder(path.TrimEnd('/')) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
                if (GUILayout.Button(new GUIContent(path + (exists ? string.Empty : "   (missing in project)"), "Click to show in the Project window"), exists ? linkLabel : EditorStyles.label))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path.TrimEnd('/'));
                    if (asset != null)
                    {
                        EditorUtility.FocusProjectWindow();
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Re-include", "Remove it from Luna's exclude list so the next build ships it again."), GUILayout.Width(80f)))
                {
                    string error;
                    if (!LunaSettingsWriter.SaveAssetExcludes(new List<string>(), new List<string> { path }, "Re-include: " + Path.GetFileName(path.TrimEnd('/')), projectRoot, out error))
                        SetMessage("Nothing was changed. " + error, MessageType.Error);
                    else
                        SetMessage("Re-included " + path + ". The numbers above update after the next Luna build and Measure.", MessageType.Info);
                    ReadExcludes();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ---------------------------------------------------------------- drawing

        [NonSerialized] SearchBar searchBar;
        [NonSerialized] UnityEngine.UIElements.IMGUIContainer body;

        // UI Toolkit frame (the shared search bar, like the other tabs) around the IMGUI toolbar and body.
        public override UnityEngine.UIElements.VisualElement CreateUI()
        {
            var root = new UnityEngine.UIElements.VisualElement();
            root.AddToClassList("lo-tab-root");
            var toolbar = new UnityEngine.UIElements.IMGUIContainer(() =>
            {
                EnsureStyles();
                DrawToolbar();
            });
            toolbar.AddToClassList("lo-imgui-toolbar");
            root.Add(toolbar);
            var bar = new UnityEngine.UIElements.VisualElement();
            bar.AddToClassList("lo-bulkbar");
            searchBar = new SearchBar(filter, "Search assets, scripts, types, categories   (Ctrl/Cmd+F)", value =>
            {
                filter = value;
                if (body != null)
                    body.MarkDirtyRepaint();
            });
            bar.Add(searchBar);
            root.Add(bar);
            searchBar.RegisterShortcut(root);
            body = new UnityEngine.UIElements.IMGUIContainer(DrawBody);
            body.AddToClassList("lo-imgui-body");
            root.Add(body);
            return root;
        }

        public override void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            DrawBody();
        }

        void EnsureStyles()
        {
            if (rightLabel == null)
            {
                rightLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight };
                wrapLabel = new GUIStyle(EditorStyles.label) { wordWrap = true };
                linkLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.7f, 1f) : new Color(0.1f, 0.35f, 0.8f) } };
            }
            OverviewStyles.Ensure();
        }

        void DrawBody()
        {
            EnsureStyles();
            if (!string.IsNullOrEmpty(StaleSceneNote))
                EditorGUILayout.HelpBox(StaleSceneNote, MessageType.Warning);
            if (install == null)
            {
                EditorGUILayout.HelpBox("No Luna install found among this project's packages.", MessageType.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(message))
                EditorGUILayout.HelpBox(message, messageType);
            if (current == null || current.stats == null)
            {
                EditorGUILayout.HelpBox("Press Measure. Scripts and textures are measured from your current project (no Luna build); "
                    + "the first measure compiles your scripts (~30-40 s), later ones only when code changed. Needs one earlier Luna build for the other assets.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            OverviewStyles.BeginCard();
            DrawBudget();
            DrawFreshness();
            OverviewStyles.EndCard();
            OverviewStyles.BeginCard();
            DrawCategories();
            OverviewStyles.EndCard();
            OverviewStyles.BeginCard();
            DrawScripts();
            OverviewStyles.EndCard();
            OverviewStyles.BeginCard();
            DrawModules();
            OverviewStyles.EndCard();
            OverviewStyles.BeginCard();
            DrawExcluded();
            OverviewStyles.EndCard();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(install == null))
            {
                if (runner == null)
                {
                    var background = GUI.backgroundColor;
                    GUI.backgroundColor = OverviewStyles.AccentButton;
                    if (GUILayout.Button(new GUIContent("Measure", "Run Luna's own size statistics on the last build output."), OverviewStyles.PrimaryToolbarButton, GUILayout.Width(80f)))
                        Measure(false);
                    GUI.backgroundColor = background;
                }
                else if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    runner.Cancel();
                    runner = null;
                }
            }
            EditorGUI.BeginChangeCheck();
            autoRefresh = GUILayout.Toggle(autoRefresh, new GUIContent("Auto", "Re-measure in the background when scripts, Luna settings or textures change."), EditorStyles.toolbarButton, GUILayout.Width(45f));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(AUTO_PREF, autoRefresh);
            EditorGUI.BeginChangeCheck();
            var share = GUILayout.Toggle(LunaBuildAcceleration.Enabled, new GUIContent("Speed up builds",
                "Real Luna builds reuse what Measure already computed: the compiled scripts (compile cache) and Brotli results. "
                + "Outputs are identical; only identical inputs are reused."), EditorStyles.toolbarButton, GUILayout.Width(100f));
            if (EditorGUI.EndChangeCheck())
                LunaBuildAcceleration.Enabled = share;
            if (runner != null)
                GUILayout.Label("Measuring " + runner.Label + "… " + runner.ElapsedSeconds.ToString("0") + " s", EditorStyles.miniLabel);
            if (GUILayout.Button(new GUIContent("Exclude selection…", "Add the files/folders selected in the Project window to the Luna Exclusions window for review."),
                    EditorStyles.toolbarButton, GUILayout.Width(115f)))
                ExcludeProjectSelection();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Texture Optimizer", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Textures);
            if (networkNames.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                networkIndex = EditorGUILayout.Popup(networkIndex, networkNames, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(NETWORK_PREF, networkNames[networkIndex]);
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawFreshness()
        {
            if (!string.IsNullOrEmpty(StaleSceneNote))
                return;
            if (current.noBuild)
            {
                EditorGUILayout.HelpBox("No Luna build yet: scripts and textures are measured from your project; scene deserializers are estimated "
                    + "(~16 KB). Fonts, meshes, sounds, shaders and other assets are not included until the first Luna build.", MessageType.Info);
                return;
            }
            var changed = rows.Sum(r => r.changed);
            var line = "Measured " + FormatTime(current.measuredAt) + ". Live: " + (current.live ? "scripts, " : string.Empty) + "textures"
                + ". Other categories from the build of " + buildOutputTime.ToString("MMM d HH:mm") + ".";
            if (changed > 0)
                EditorGUILayout.HelpBox(line + "\n" + changed + " of those assets changed since that build (marked). A Luna Developer build refreshes them.", MessageType.Warning);
            else
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
        }

        string SelectedNetwork
        {
            get { return networkNames.Length > 0 ? networkNames[networkIndex] : "develop"; }
        }

        long LimitOf(string network)
        {
            KeyValuePair<long, long> info;
            return network != null && networks.TryGetValue(network, out info) ? info.Key : 0;
        }

        // Luna's numbers with textures replaced by live predictions, and assets changed since the build flagged.
        void Compose()
        {
            typeFiles = null;
            rows = new List<CategoryRow>();
            if (current == null || current.stats == null)
                return;
            DateTime.TryParse(current.buildOutputAt, out buildOutputTime);
            var stats = current.stats;
            if (sizeMemory == null)
                sizeMemory = new AssetSizeMemory(projectRoot);
            if (current.measuredAt != recordedMeasurement)
            {
                sizeMemory.Record(current, projectRoot);
                recordedMeasurement = current.measuredAt;
            }
            var sceneEstimate = SceneEstimate();
            var scriptsRow = new CategoryRow { name = "scripts", size = stats.scripts, live = current.live };
            if (current.scriptFiles != null && currentFiles != null)
            {
                int changedFiles;
                var estimate = ScriptSizeEstimator.Estimate(current, currentFiles, out changedFiles);
                if (changedFiles > 0)
                {
                    scriptsRow.size = estimate;
                    scriptsRow.estimatedFiles = changedFiles;
                }
            }
            if (current.noBuild)
            {
                // deserializers.js is generated from the serialized scenes during Luna's export (~16 KB in a
                // mid-size playable); estimated until the first build provides the real one.
                scriptsRow.size += DESERIALIZERS_ESTIMATE;
                scriptsRow.estimatedDeserializers = true;
            }
            rows.Add(scriptsRow);

            foreach (var type in stats.types)
            {
                // Scene not built: only textures (live) are taken from the last build's statistics.
                if (sceneEstimate != null && type.title != "textures")
                    continue;
                var row = new CategoryRow { name = type.title };
                var factors = new List<double>();
                var isTextures = type.title == "textures" && liveTextures.Count > 0;
                row.live = isTextures;
                var livePaths = new HashSet<string>();
                // Scene not built: the last build's textures that this scene does not ship are left out.
                var typeAssets = sceneEstimate != null && isTextures
                    ? type.assets.Where(a => liveTextures.Any(e => e.AssetPath == a.path)).ToList()
                    : type.assets;
                foreach (var asset in typeAssets)
                {
                    var assetRow = new AssetRow { title = asset.title, path = asset.path, size = asset.size };
                    var entry = isTextures ? liveTextures.FirstOrDefault(e => e.AssetPath == asset.path) : null;
                    var factor = EncodingFactor(asset);
                    if (factor > 0)
                        factors.Add(factor);
                    if (entry != null && entry.HasPrediction && factor > 0)
                    {
                        assetRow.size = (entry.Prediction.outputBytes + SidecarBytes(asset)) * factor;
                        assetRow.tag = "live";
                        livePaths.Add(entry.AssetPath);
                    }
                    else if (ChangedSinceBuild(asset.path))
                    {
                        assetRow.tag = "changed";
                        row.changed++;
                    }
                    row.assets.Add(assetRow);
                }
                if (isTextures)
                {
                    // Textures not in the last build: typical encoding factor of this network, typical sidecar.
                    var typical = factors.Count > 0 ? factors.OrderBy(f => f).ElementAt(factors.Count / 2) : 4.0 / 3.0;
                    foreach (var entry in liveTextures.Where(e => e.HasPrediction && !livePaths.Contains(e.AssetPath)
                                 && !type.assets.Any(a => a.path == e.AssetPath)))
                        row.assets.Add(new AssetRow { title = Path.GetFileNameWithoutExtension(entry.AssetPath), path = entry.AssetPath,
                            size = (entry.Prediction.outputBytes + TYPICAL_SIDECAR) * typical, tag = "new" });
                }
                row.size = row.assets.Count > 0 ? row.assets.Sum(a => a.size) : type.size;
                rows.Add(row);
            }
            if (!stats.types.Any(t => t.title == "textures") && liveTextures.Any(e => e.HasPrediction))
            {
                // No build: no per-network factor from Luna yet; most networks inline assets as base64.
                var textures = new CategoryRow { name = "textures", live = true };
                foreach (var entry in liveTextures.Where(e => e.HasPrediction))
                    textures.assets.Add(new AssetRow { title = Path.GetFileNameWithoutExtension(entry.AssetPath), path = entry.AssetPath,
                        size = (entry.Prediction.outputBytes + TYPICAL_SIDECAR) * BASE64, tag = "live" });
                textures.size = textures.assets.Sum(a => a.size);
                rows.Add(textures);
            }
            if (sceneEstimate != null)
            {
                var sceneName = Path.GetFileNameWithoutExtension(LunaSettings.Load(projectRoot).Scenes.FirstOrDefault() ?? "scene");
                foreach (var category in sceneEstimate)
                {
                    var row = new CategoryRow { name = category.Title, sceneEstimate = sceneName };
                    foreach (var asset in category.Assets)
                    {
                        row.assets.Add(new AssetRow { title = asset.Title, path = asset.Path, size = asset.Size, tag = asset.Tag });
                        if (asset.Tag == SceneSizeEstimator.ESTIMATED)
                            row.estimated++;
                        else
                            row.remembered++;
                    }
                    row.size = row.assets.Sum(a => a.size);
                    rows.Add(row);
                }
            }
            rows.Add(new CategoryRow { name = "html template (other)", size = stats.other });
            if (stats.preloader > 0)
                rows.Add(new CategoryRow { name = "preloader", size = stats.preloader });
            composedTotal = rows.Sum(r => r.size);
        }

        // Non-texture categories for the scene Luna builds now when the last build was of another scene; null otherwise.
        List<SceneSizeEstimator.Category> SceneEstimate()
        {
            var settings = LunaSettings.Load(projectRoot);
            if (settings == null || current.noBuild || !LunaBuildScenes.IsStale(projectRoot, settings))
                return null;
            var network = current.network ?? "develop";
            var lunaPath = Path.Combine(projectRoot, LunaSettings.FILE_NAME);
            var key = string.Join("|", settings.Scenes) + "|" + network + "|" + File.GetLastWriteTimeUtc(lunaPath).Ticks + "|" + current.measuredAt;
            if (key != estimateKey || estimate == null)
            {
                estimate = SceneSizeEstimator.Estimate(settings, network, sizeMemory, projectRoot, HalfPrecisionMeshes(lunaPath));
                estimateKey = key;
            }
            return estimate;
        }

        static readonly Regex HALF_PRECISION = new Regex("\"meshes\"\\s*:\\s*\\{\\s*\"default\"\\s*:\\s*\\{[^}]*\"halfPrecision\"\\s*:\\s*(true|false)");

        static bool HalfPrecisionMeshes(string lunaPath)
        {
            var match = HALF_PRECISION.Match(File.ReadAllText(lunaPath));
            return !match.Success || match.Groups[1].Value == "true";
        }

        const double TYPICAL_SIDECAR = 120;
        const double DESERIALIZERS_ESTIMATE = 16 * 1024;   // measured 15.9 KB in a mid-size playable
        const double BASE64 = 4.0 / 3.0;

        // How this network counts a texture: Luna's size divided by the processed file + its .json sidecar
        // (1 for raw packaging, 4/3 for base64, 8/7 for base122). 0 when the build file is not available.
        static double EncodingFactor(LunaStatsAsset asset)
        {
            if (string.IsNullOrEmpty(asset.lunaPath) || !File.Exists(asset.lunaPath))
                return 0;
            var bytes = new FileInfo(asset.lunaPath).Length + SidecarBytes(asset);
            return bytes > 0 ? asset.size / bytes : 0;
        }

        static double SidecarBytes(LunaStatsAsset asset)
        {
            var sidecar = string.IsNullOrEmpty(asset.lunaPath) ? null : Path.ChangeExtension(asset.lunaPath, ".json");
            return sidecar != null && File.Exists(sidecar) ? new FileInfo(sidecar).Length : TYPICAL_SIDECAR;
        }

        bool ChangedSinceBuild(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            var full = Path.Combine(projectRoot, assetPath);
            if (!File.Exists(full))
                return false;
            var changed = File.GetLastWriteTime(full);
            if (File.Exists(full + ".meta") && File.GetLastWriteTime(full + ".meta") > changed)
                changed = File.GetLastWriteTime(full + ".meta");
            return changed > buildOutputTime.AddSeconds(5);
        }

        // ---------------------------------------------------------------- for the browser page (LunaOptimizerWebApi)

        // Everything the Overview tab shows, as plain data for the page.
        public Dictionary<string, object> BrowserData()
        {
            var data = new Dictionary<string, object>
            {
                { "lunaFound", install != null },
                { "measured", current != null && current.stats != null },
                { "measuring", runner != null },
                { "measuringLabel", runner != null ? "Measuring " + runner.Label + "… " + runner.ElapsedSeconds.ToString("0") + " s" : null },
                { "message", message },
                { "messageType", messageType.ToString() },
                { "staleNote", StaleSceneNote },
                { "networks", networkNames.Select(n => new Dictionary<string, object> { { "name", n }, { "limit", LimitOf(n) } }).ToList() },
                { "selectedNetwork", SelectedNetwork },
                { "measuredNetwork", current != null ? current.network ?? "develop" : null },
                { "auto", autoRefresh },
                { "speedUp", LunaBuildAcceleration.Enabled },
                { "total", composedTotal },
                { "limit", Limit },
            };
            if (current == null || current.stats == null)
                return data;
            data["noBuild"] = current.noBuild;
            data["freshness"] = current.noBuild ? "No Luna build yet: scripts and textures are measured from your project; scene deserializers are estimated (~16 KB). Fonts, meshes, sounds, shaders and other assets are not included until the first Luna build."
                : "Measured " + FormatTime(current.measuredAt) + ". Live: " + (current.live ? "scripts, " : string.Empty) + "textures. Other categories from the build of " + buildOutputTime.ToString("MMM d HH:mm") + ".";
            data["changedSinceBuild"] = rows.Sum(r => r.changed);

            data["categories"] = rows.OrderByDescending(r => r.size).Select(row => new Dictionary<string, object>
            {
                { "name", row.name },
                { "size", row.size },
                { "count", row.assets.Count },
                { "live", row.live },
                { "estimated", row.sceneEstimate != null || row.estimatedFiles > 0 || row.estimatedDeserializers },
                { "note", CategoryNote(row) },
                { "assets", row.assets.OrderByDescending(a => a.size).Select(a => new Dictionary<string, object>
                    {
                        { "title", a.title }, { "path", a.path }, { "size", a.size },
                        { "tag", a.tag == "changed" ? "changed since build" : a.tag == "new" ? "new since build" : a.tag == SceneSizeEstimator.ESTIMATED || a.tag == SceneSizeEstimator.CHANGED ? a.tag : null },
                    }).ToList() },
            }).ToList();

            var scripts = new Dictionary<string, object> { { "total", current.stats.scripts } };
            var sizes = current.scripts;
            if (sizes != null && sizes.files != null && sizes.files.Count > 0)
            {
                var scale = current.stats.scripts / Math.Max(1, sizes.files.Sum(f => f.br));
                scripts["files"] = sizes.files.Where(f => f.br > 0).OrderByDescending(f => f.br)
                    .Select(f => new Dictionary<string, object> { { "name", ScriptFileLabel(f.src) }, { "size", f.br * scale } }).ToList();
                var user = sizes.user;
                var userFile = sizes.files.FirstOrDefault(f => f.src.EndsWith("UnityScriptsCompiler.js", StringComparison.Ordinal));
                if (user != null && userFile != null && user.types.Count > 0)
                {
                    var userBytes = userFile.br * scale;
                    var userScale = userBytes / Math.Max(1, user.types.Sum(t => t.br) + user.metadataBr + user.otherBr);
                    if (typeFiles == null)
                        typeFiles = ScriptSizeEstimator.TypeFileMap(projectRoot, user.types.Select(t => t.name));
                    scripts["user"] = userBytes;
                    scripts["typeCount"] = user.types.Count;
                    scripts["metadata"] = user.metadataBr * userScale;
                    scripts["namespaces"] = user.types.GroupBy(t => Namespace(t.name)).OrderByDescending(g => g.Sum(t => t.br))
                        .Select(g => new Dictionary<string, object> { { "name", g.Key }, { "count", g.Count() }, { "size", g.Sum(t => t.br) * userScale } }).ToList();
                    scripts["types"] = user.types.OrderByDescending(t => t.br).Select(t =>
                    {
                        string file;
                        typeFiles.TryGetValue(t.name, out file);
                        return new Dictionary<string, object> { { "name", t.name }, { "size", t.br * userScale }, { "path", file } };
                    }).ToList();
                    if (current.fileScripts != null)
                        scripts["csFiles"] = current.fileScripts.OrderByDescending(f => f.js)
                            .Select(f => new Dictionary<string, object> { { "path", f.path }, { "size", f.js } }).ToList();
                }
            }
            data["scripts"] = scripts;

            var moduleSizes = ModuleSizes();
            Func<string, string, bool, bool, Dictionary<string, object>> module = (label, name, planned, saved) =>
            {
                double size;
                return new Dictionary<string, object>
                {
                    { "label", label }, { "name", name }, { "planned", planned }, { "saved", saved },
                    { "size", moduleSizes.TryGetValue(name, out size) ? size : (double?)null },
                };
            };
            var modules = new List<object> { module("Mecanim WASM (fast animation runtime)", MECANIM_WASM, plannedMecanimWasm, savedMecanimWasm) };
            modules.AddRange(savedModules.Keys.OrderBy(n => n).Select(n => module(n, n, plannedModules[n], savedModules[n])));
            data["modules"] = modules;
            data["planChanged"] = plannedMecanimWasm != savedMecanimWasm || plannedModules.Any(m => savedModules[m.Key] != m.Value);
            data["planDescription"] = DescribePlan();
            if (whatIf != null && whatIf.stats != null)
                data["whatIf"] = new Dictionary<string, object>
                {
                    { "label", whatIf.label }, { "total", whatIf.stats.total }, { "now", current.stats.total },
                    { "scripts", whatIf.stats.scripts }, { "scriptsNow", current.stats.scripts },
                };
            data["excluded"] = forcedExcludes.Select(path => new Dictionary<string, object>
            {
                { "path", path },
                { "exists", AssetDatabase.IsValidFolder(path.TrimEnd('/')) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) },
            }).ToList();
            return data;
        }

        string CategoryNote(CategoryRow row)
        {
            var notes = new List<string>();
            if (row.estimatedDeserializers)
                notes.Add("incl. ~16 KB estimated scene deserializers");
            if (row.sceneEstimate != null)
                notes.Add("≈ " + row.remembered + " remembered" + (row.estimated > 0 ? ", " + row.estimated + " estimated" : string.Empty));
            if (row.estimatedFiles > 0)
                notes.Add("estimate, " + row.estimatedFiles + " file(s) changed" + (runner != null ? ", measuring…" : string.Empty));
            else if (row.live)
                notes.Add("live");
            else if (row.assets.Count > 0)
                notes.Add("last build");
            if (row.changed > 0)
                notes.Add(row.changed + " changed");
            return string.Join("  ·  ", notes);
        }

        // Selects an Overview row's asset in Unity (Luna lists some assets by name only).
        public bool Locate(string assetPath, string title, string category)
        {
            var target = Locatable(assetPath, title, category);
            if (target == null)
                return false;
            var asset = Resolve(target, title);
            if (asset == null)
                return false;
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return true;
        }

        public void SetNetwork(string name)
        {
            var index = Array.IndexOf(networkNames, name);
            if (index < 0)
                return;
            networkIndex = index;
            EditorPrefs.SetString(NETWORK_PREF, name);
        }

        public void SetAuto(bool value)
        {
            autoRefresh = value;
            EditorPrefs.SetBool(AUTO_PREF, value);
        }

        public void SetSpeedUp(bool value)
        {
            LunaBuildAcceleration.Enabled = value;
        }

        public void PlanModule(string name, bool included)
        {
            if (name == MECANIM_WASM)
                plannedMecanimWasm = included;
            else if (plannedModules.ContainsKey(name))
                plannedModules[name] = included;
        }

        public void ResetPlan()
        {
            plannedMecanimWasm = savedMecanimWasm;
            plannedModules = new Dictionary<string, bool>(savedModules);
            whatIf = null;
        }

        public void MeasureWhatIf()
        {
            if (runner == null)
                Measure(true);
        }

        public string PlanConfirmationText
        {
            get { return PlanConfirmation; }
        }

        public void ApplyPlanConfirmed()
        {
            ApplyPlanCore();
        }

        public bool ReInclude(string path, out string error)
        {
            var ok = LunaSettingsWriter.SaveAssetExcludes(new List<string>(), new List<string> { path }, "Re-include: " + Path.GetFileName(path.TrimEnd('/')), projectRoot, out error);
            ReadExcludes();
            if (ok)
                LunaOptimizerWindow.NotifyLunaSettingsChanged();
            return ok;
        }

        // ---------------------------------------------------------------- for the hub's Simple mode

        public class CategorySummary
        {
            public string Name;
            public double Bytes;
            public int Count;
            public bool Estimated;   // part of the size is estimated (scene not built, scripts changed, no build yet)
        }

        public bool HasLunaInstall
        {
            get { return install != null; }
        }

        public bool HasMeasurement
        {
            get { return current != null && current.stats != null; }
        }

        public bool IsMeasuring
        {
            get { return runner != null; }
        }

        public string MeasuredAtText
        {
            get { return HasMeasurement ? FormatTime(current.measuredAt) : null; }
        }

        // The last Measure problem (null when none).
        public string LastMessage
        {
            get { return messageType == MessageType.Error || messageType == MessageType.Warning ? message : null; }
        }

        // Network the shown sizes were measured for.
        public string Network
        {
            get { return current != null ? current.network ?? "develop" : SelectedNetwork; }
        }

        public double TotalBytes
        {
            get { return composedTotal; }
        }

        // Network cap of the measured network, 0 when it has none.
        public long Limit
        {
            get { return current != null ? LimitOf(current.network ?? "develop") : 0; }
        }

        public List<CategorySummary> Categories()
        {
            return rows.Where(r => r.size > 0).OrderByDescending(r => r.size).Select(r => new CategorySummary
            {
                Name = r.name,
                Bytes = r.size,
                Count = r.assets.Count,
                Estimated = r.sceneEstimate != null || r.estimatedFiles > 0 || r.estimatedDeserializers,
            }).ToList();
        }

        public void MeasureNow()
        {
            if (install != null && runner == null)
                Measure(false);
        }

        public void CancelMeasure()
        {
            if (runner == null)
                return;
            runner.Cancel();
            runner = null;
        }

        // For the hub header: total playable size against the network cap, or null before the first measure.
        public string BudgetSummary(out float ratio)
        {
            ratio = 0f;
            if (current == null || composedTotal <= 0)
                return null;
            var network = current.network ?? "develop";
            var limit = LimitOf(network);
            ratio = limit > 0 ? (float)(composedTotal / limit) : 0f;
            return FormatBytes(composedTotal) + (limit > 0 ? " / " + FormatBytes(limit) : string.Empty) + "  (" + network + ")";
        }

        void DrawBudget()
        {
            var network = current.network ?? "develop";
            var limit = LimitOf(network);
            OverviewStyles.Hero(FormatBytes(composedTotal), (limit > 0 ? "of " + FormatBytes(limit) + "  ·  " : string.Empty) + network);
            if (network != SelectedNetwork)
                EditorGUILayout.HelpBox("These numbers are for " + network + ". Press Measure for " + SelectedNetwork
                    + " (only Luna's statistics run again; ~15-20 s). Encoding and template differ per network.", MessageType.Info);
            if (limit <= 0)
                return;
            var ratio = (float)(composedTotal / limit);
            OverviewStyles.Meter(ratio, BAR_HEIGHT);
            GUILayout.Label((ratio * 100f).ToString("0") + "%  ·  " + (limit - composedTotal >= 0 ? FormatBytes(limit - composedTotal) + " left" : FormatBytes(composedTotal - limit) + " over"), OverviewStyles.Muted);
        }

        void DrawCategories()
        {
            OverviewStyles.Section("By category", Filtering ? "filter: " + filter : "click a category to list its assets");
            foreach (var row in rows.OrderByDescending(r => r.size))
            {
                var matching = row.assets.Where(a => Matches(a.title) || Matches(a.path)).OrderByDescending(a => a.size).ToList();
                if (Filtering && matching.Count == 0 && !Matches(row.name))
                    continue;
                var key = "category:" + row.name;
                var open = expanded.Contains(key) || (Filtering && matching.Count > 0);
                var label = row.name + "  (" + row.assets.Count + ")" + (row.estimatedDeserializers ? "   · incl. ~16 KB estimated scene deserializers" : string.Empty)
                    + (row.sceneEstimate != null ? "   · " + row.sceneEstimate + " not built: " + row.remembered + " remembered" + (row.estimated > 0 ? ", " + row.estimated + " estimated" : string.Empty) : string.Empty)
                    + (row.estimatedFiles > 0 ? "   · estimate, " + row.estimatedFiles + " file(s) changed" + (runner != null ? ", measuring…" : string.Empty)
                        : row.live ? "   · live" : row.assets.Count > 0 ? "   · last build" : string.Empty)
                    + (row.changed > 0 ? "   · " + row.changed + " changed" : string.Empty);
                if (row.assets.Count == 0)
                    label = row.name + (row.estimatedDeserializers ? "   · incl. ~16 KB estimated scene deserializers" : string.Empty)
                        + (row.estimatedFiles > 0 ? "   · estimate, " + row.estimatedFiles + " file(s) changed" + (runner != null ? ", measuring…" : string.Empty) : row.live ? "   · live" : string.Empty);
                var primary = row.name + (row.assets.Count > 0 ? "  (" + row.assets.Count + ")" : string.Empty);
                var note = label.StartsWith(primary, StringComparison.Ordinal) ? label.Substring(primary.Length).Trim().TrimStart('·').Trim().Replace("   · ", "  ·  ") : null;
                // The banner above already says the scene was not built: keep only the counts here.
                if (note != null && row.sceneEstimate != null)
                    note = note.Replace(row.sceneEstimate + " not built: ", "≈ ");
                var rect = SizeRow(label.StartsWith(primary, StringComparison.Ordinal) ? primary : label, row.size, composedTotal, row.assets.Count > 0 ? (bool?)open : null, 0, null, null, note);
                if (row.assets.Count > 0 && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    if (expanded.Contains(key)) expanded.Remove(key); else expanded.Add(key);
                    Event.current.Use();
                }
                if (!open)
                    continue;
                DrawList(key, matching, asset =>
                {
                    var tag = asset.tag == "changed" ? "   (changed since build)" : asset.tag == "new" ? "   (new since build)"
                        : asset.tag == SceneSizeEstimator.ESTIMATED || asset.tag == SceneSizeEstimator.CHANGED ? "   (" + asset.tag + ")" : string.Empty;
                    LinkRow(asset.title + tag + (string.IsNullOrEmpty(asset.path) || asset.path == asset.title ? string.Empty : "   " + asset.path),
                        asset.size, composedTotal, 1, asset.path, asset.title, row.name);
                });
            }
        }

        void DrawScripts()
        {
            OverviewStyles.Section("Scripts", FormatBytes(current.stats.scripts));
            var sizes = current.scripts;
            if (sizes == null || sizes.files == null || sizes.files.Count == 0)
            {
                EditorGUILayout.LabelField("No script breakdown in this measurement.", EditorStyles.miniLabel);
                return;
            }
            // Per-part compressed sizes overlap less than the whole bundle; scale them to Luna's exact total.
            var scale = current.stats.scripts / Math.Max(1, sizes.files.Sum(f => f.br));
            foreach (var file in sizes.files.Where(f => f.br > 0 && (Matches(f.src) || Matches(ScriptFileLabel(f.src)))).OrderByDescending(f => f.br))
                SizeRow(ScriptFileLabel(file.src), file.br * scale, current.stats.scripts, null, 0);

            var user = sizes.user;
            var userFile = sizes.files.FirstOrDefault(f => f.src.EndsWith("UnityScriptsCompiler.js", StringComparison.Ordinal));
            if (user == null || userFile == null || user.types.Count == 0)
                return;
            var userBytes = userFile.br * scale;
            var userScale = userBytes / Math.Max(1, user.types.Sum(t => t.br) + user.metadataBr + user.otherBr);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Your C# code: " + FormatBytes(userBytes) + " in " + user.types.Count + " types", EditorStyles.boldLabel);
            var groups = user.types.GroupBy(t => Namespace(t.name)).Select(g => new { name = g.Key, br = g.Sum(t => t.br), count = g.Count() });
            foreach (var group in groups.Where(g => Matches(g.name)).OrderByDescending(g => g.br))
                SizeRow(group.name + "  (" + group.count + " types)", group.br * userScale, userBytes, null, 0);
            if (user.metadataBr > 0 && !Filtering)
                SizeRow("reflection metadata (all types)", user.metadataBr * userScale, userBytes, null, 0);

            // By file: the .cs files you edit, each locatable.
            if (current.fileScripts != null && current.fileScripts.Count > 0)
            {
                var files = current.fileScripts.Where(f => Matches(f.path)).OrderByDescending(f => f.js).ToList();
                if (Foldout("files", "C# files (" + current.fileScripts.Count + ")", files.Count))
                    DrawList("files", files, file => LinkRow(Path.GetFileName(file.path) + "   " + Path.GetDirectoryName(file.path), file.js, userBytes, 1, file.path, null, "script"));
            }

            var types = user.types.Where(t => Matches(t.name)).OrderByDescending(t => t.br).ToList();
            if (Foldout("types", "C# types (" + user.types.Count + ")", types.Count))
            {
                if (typeFiles == null)
                    typeFiles = ScriptSizeEstimator.TypeFileMap(projectRoot, user.types.Select(t => t.name));
                DrawList("types", types, type =>
                {
                    string file;
                    typeFiles.TryGetValue(type.name, out file);
                    LinkRow(type.name, type.br * userScale, userBytes, 1, file, null, "script");
                });
            }
        }

        bool Filtering
        {
            get { return !string.IsNullOrEmpty(filter); }
        }

        bool Matches(string text)
        {
            return !Filtering || (!string.IsNullOrEmpty(text) && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        bool Foldout(string key, string label, int matches)
        {
            var open = EditorGUILayout.Foldout(expanded.Contains(key) || (Filtering && matches > 0), label, true);
            if (!Filtering)
            {
                if (open) expanded.Add(key); else expanded.Remove(key);
            }
            return open;
        }

        // First PREVIEW rows, then a button for the rest (everything while filtering).
        void DrawList<T>(string key, List<T> items, Action<T> draw)
        {
            var all = Filtering || showAll.Contains(key);
            foreach (var item in all ? items : items.Take(PREVIEW))
                draw(item);
            if (items.Count <= PREVIEW || Filtering)
                return;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            if (GUILayout.Button(all ? "Show fewer" : "Show all " + items.Count + " (" + (items.Count - PREVIEW) + " more)", EditorStyles.miniButton, GUILayout.Width(170f)))
            {
                if (all) showAll.Remove(key); else showAll.Add(key);
            }
            EditorGUILayout.EndHorizontal();
        }

        // A size row whose label selects and highlights the asset in the Project window when clicked.
        void LinkRow(string label, double bytes, double whole, int indent, string assetPath, string subAssetName, string kind)
        {
            var target = Locatable(assetPath, subAssetName, kind);
            var rect = SizeRow(label, bytes, whole, null, indent, target != null ? linkLabel : null,
                target != null ? "Click to show in the Project window" : "Not a project file (built-in or generated)");
            var labelRect = new Rect(rect.x, rect.y, rect.width * 0.7f, rect.height);
            if (target == null)
                return;
            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition)
                && target.StartsWith("Assets/", StringComparison.Ordinal))
            {
                var menu = new GenericMenu();
                var path = target;
                menu.AddItem(new GUIContent("Add to Luna Exclusions"), false, () => LunaExclusionWindow.AddAndShow(new[] { path }));
                menu.ShowAsContext();
                Event.current.Use();
            }
            EditorGUIUtility.AddCursorRect(labelRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && labelRect.Contains(Event.current.mousePosition))
            {
                var asset = Resolve(target, subAssetName);
                if (asset != null)
                {
                    EditorUtility.FocusProjectWindow();
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                Event.current.Use();
            }
        }

        // Project path to locate, or null. Luna lists some assets by name only (prefabs, built-ins).
        readonly Dictionary<string, string> nameLookups = new Dictionary<string, string>();
        // Luna categories whose entries can be found by name when Luna lists no path.
        static readonly Dictionary<string, string> NAME_SEARCH_TYPES = new Dictionary<string, string>
        {
            { "prefabs", "Prefab" }, { "materials", "Material" }, { "textures", "Texture" }, { "meshes", "Mesh" },
            { "animation-clips", "AnimationClip" }, { "sounds", "AudioClip" }, { "sprites", "Sprite" },
        };

        string Locatable(string assetPath, string title, string kind)
        {
            if (!string.IsNullOrEmpty(assetPath) && (assetPath.StartsWith("Assets/", StringComparison.Ordinal) || assetPath.StartsWith("Packages/", StringComparison.Ordinal)))
                return string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) ? null : assetPath;
            if (string.IsNullOrEmpty(title))
                return null;
            string found;
            if (nameLookups.TryGetValue(kind + "|" + title, out found))
                return found;
            if (kind == "shaders")
            {
                // Listed by shader name; built-in shaders have no project file.
                var shader = Shader.Find(title);
                var shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
                found = !string.IsNullOrEmpty(shaderPath) && (shaderPath.StartsWith("Assets/", StringComparison.Ordinal) || shaderPath.StartsWith("Packages/", StringComparison.Ordinal)) ? shaderPath : null;
            }
            else
            {
                string type;
                if (NAME_SEARCH_TYPES.TryGetValue(kind, out type))
                    found = AssetDatabase.FindAssets(title + " t:" + type).Select(AssetDatabase.GUIDToAssetPath)
                        .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == title);
            }
            nameLookups[kind + "|" + title] = found;
            return found;
        }

        // The sub-asset with that name (a mesh or clip inside an FBX), else the asset itself.
        static UnityEngine.Object Resolve(string path, string subAssetName)
        {
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (string.IsNullOrEmpty(subAssetName) || main == null || main.name == subAssetName)
                return main;
            var sub = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(o => o != null && o != main && o.name == subAssetName);
            return sub ?? main;
        }

        void DrawModules()
        {
            OverviewStyles.Section("Engine modules", null);
            EditorGUILayout.LabelField("Unticking ships Luna's empty stub instead. Measure the what-if to see the exact size; "
                + "only remove what the game does not use, then playtest.", wrapLabel);

            var moduleSizes = ModuleSizes();
            plannedMecanimWasm = ModuleToggle("Mecanim WASM (fast animation runtime)", MECANIM_WASM, plannedMecanimWasm, savedMecanimWasm, moduleSizes);
            foreach (var name in savedModules.Keys.OrderBy(n => n).ToList())
                plannedModules[name] = ModuleToggle(name, name, plannedModules[name], savedModules[name], moduleSizes);

            var changed = plannedMecanimWasm != savedMecanimWasm || plannedModules.Any(m => savedModules[m.Key] != m.Value);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!changed || runner != null))
            {
                if (GUILayout.Button("Measure what-if", GUILayout.Width(120f)))
                    Measure(true);
                if (GUILayout.Button("Apply to Luna…", GUILayout.Width(110f)))
                    ApplyPlan();
                if (GUILayout.Button("Reset", GUILayout.Width(60f)))
                {
                    plannedMecanimWasm = savedMecanimWasm;
                    plannedModules = new Dictionary<string, bool>(savedModules);
                    whatIf = null;
                }
            }
            using (new EditorGUI.DisabledScope(runner != null))
            {
                if (GUILayout.Button(new GUIContent("Undo…", "Put Luna's engine module settings back to before one of the last Apply to Luna."),
                        EditorStyles.popup, GUILayout.Width(70f)))
                    LunaSaveHistory.ShowRevertMenu(GUILayoutUtility.GetLastRect(), LunaSettingsSection.Modules, projectRoot, UndoneModules);
            }
            EditorGUILayout.EndHorizontal();

            if (whatIf != null && whatIf.stats != null)
            {
                var network = networkNames.Length > 0 ? networkNames[networkIndex] : null;
                var now = current.stats.total;
                var then = whatIf.stats.total;
                EditorGUILayout.HelpBox("What-if (" + whatIf.label + "): " + FormatBytes(then) + " instead of " + FormatBytes(now)
                    + "  →  " + (then <= now ? "saves " + FormatBytes(now - then) : "adds " + FormatBytes(then - now))
                    + "\nScripts " + FormatBytes(whatIf.stats.scripts) + " instead of " + FormatBytes(current.stats.scripts) + ".", MessageType.None);
            }
        }

        bool ModuleToggle(string label, string module, bool planned, bool saved, Dictionary<string, double> sizes)
        {
            EditorGUILayout.BeginHorizontal();
            var result = EditorGUILayout.ToggleLeft(label + (planned != saved ? "  (changed)" : string.Empty), planned, planned != saved ? EditorStyles.boldLabel : EditorStyles.label);
            double size;
            GUILayout.Label(sizes.TryGetValue(module, out size) ? FormatBytes(size) : saved ? string.Empty : "stub", rightLabel, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            return result;
        }

        // Scaled size of the script files each module brings, from the engine manifests in the build output.
        Dictionary<string, double> ModuleSizes()
        {
            var result = new Dictionary<string, double>();
            if (current.scripts == null || current.scripts.files == null)
                return result;
            var scale = current.stats.scripts / Math.Max(1, current.scripts.files.Sum(f => f.br));
            var manifest = Path.Combine(projectRoot, "LunaTemp", "stage3", "engine", "luna", "manifest.json");
            if (!File.Exists(manifest))
                return result;
            var entries = JsonUtility.FromJson<ManifestList>("{\"items\":" + File.ReadAllText(manifest) + "}").items;
            foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.ifModule)))
            {
                var file = current.scripts.files.FirstOrDefault(f => f.src.EndsWith("/" + entry.src, StringComparison.Ordinal));
                if (file == null)
                    continue;
                double sum;
                result.TryGetValue(entry.ifModule, out sum);
                result[entry.ifModule] = sum + file.br * scale;
            }
            return result;
        }

        [Serializable]
        class ManifestList
        {
            public List<ManifestEntry> items = new List<ManifestEntry>();
        }

        // Label | bar | size, indented; returns the row rect for click handling.
        // note: extra facts drawn muted after the label.
        Rect SizeRow(string label, double bytes, double whole, bool? foldout, int indent, GUIStyle style = null, string tooltip = null, string note = null)
        {
            var rect = EditorGUILayout.GetControlRect(false, OverviewStyles.ROW_HEIGHT);
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, OverviewStyles.Hover);
            var share = whole > 0 ? bytes / whole : 0;
            var sizeWidth = 75f;
            var shareWidth = 38f;
            var barWidth = Mathf.Min(180f, rect.width * 0.25f);
            var labelRect = new Rect(rect.x + 4f + indent * 16f, rect.y, rect.width - sizeWidth - shareWidth - barWidth - 16f - indent * 16f, rect.height);
            var barRect = new Rect(rect.xMax - sizeWidth - shareWidth - barWidth - 8f, rect.y + rect.height / 2f - 3f, barWidth, 6f);
            var sizeRect = new Rect(rect.xMax - sizeWidth - shareWidth - 4f, rect.y, sizeWidth, rect.height);
            var shareRect = new Rect(rect.xMax - shareWidth - 4f, rect.y, shareWidth, rect.height);
            var text = (foldout.HasValue ? (foldout.Value ? "▾  " : "▸  ") : string.Empty) + label;
            var labelStyle = style ?? (indent == 0 ? OverviewStyles.RowLabel : EditorStyles.label);
            GUI.Label(labelRect, new GUIContent(text, tooltip), labelStyle);
            if (!string.IsNullOrEmpty(note))
            {
                var used = Mathf.Min(labelRect.width, labelStyle.CalcSize(new GUIContent(text)).x + 8f);
                GUI.Label(new Rect(labelRect.x + used, labelRect.y, labelRect.width - used, labelRect.height), note, OverviewStyles.Muted);
            }
            OverviewStyles.Bar(barRect, (float)share);
            GUI.Label(sizeRect, FormatBytes(bytes), indent == 0 ? OverviewStyles.SizeBold : rightLabel);
            GUI.Label(shareRect, share >= 0.005 ? (share * 100).ToString("0") + "%" : "<1%", OverviewStyles.MutedRight);
            return rect;
        }

        static string ScriptFileLabel(string src)
        {
            var name = Path.GetFileName(src);
            switch (name)
            {
                case "UnityScriptsCompiler.js": return "your C# code (UnityScriptsCompiler.js)";
                case "script1.js": return "Luna engine core (script1.js)";
                case "bridge.js": return "Bridge C# runtime (bridge.js)";
                case "deserializers.js": return "scene/asset deserializers (deserializers.js)";
                case "mecanim-wasm-0.js": return "Mecanim WASM animation runtime";
                default: return name;
            }
        }

        static string Namespace(string typeName)
        {
            var parts = typeName.Split('.');
            return parts.Length > 2 ? parts[0] + "." + parts[1] : parts[0];
        }

        string DescribePlan()
        {
            var parts = new List<string>();
            if (plannedMecanimWasm != savedMecanimWasm)
                parts.Add("Mecanim WASM " + (plannedMecanimWasm ? "on" : "off"));
            parts.AddRange(plannedModules.Where(m => savedModules[m.Key] != m.Value).Select(m => m.Key + " " + (m.Value ? "on" : "off")));
            return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
        }

        void SetMessage(string text, MessageType type)
        {
            message = text;
            messageType = type;
        }

        static string FormatTime(string iso)
        {
            DateTime time;
            return DateTime.TryParse(iso, out time) ? time.ToString("MMM d HH:mm") : iso;
        }

        static string FormatBytes(double bytes)
        {
            if (Math.Abs(bytes) >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}
