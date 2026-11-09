using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // One window for everything Luna ships, built with UI Toolkit: a header that always shows the playable size and
    // unsaved changes, a tab bar, and the active tab below. Tabs not yet converted to UI Toolkit run inside an
    // IMGUIContainer. Tabs are enabled the first time they are shown.
    public class LunaOptimizerWindow : EditorWindow
    {
        const string MENU_PATH = "Supercent/Luna Optimizer";
        const string STYLE_PATH = "Packages/com.supercent.luna-texture-optimizer/Editor/UI/LunaOptimizer.uss";
        const long HEADER_REFRESH_MS = 300;
        const float WARN_RATIO = 0.85f;

        public enum Tab
        {
            Overview,
            Textures,
            Sounds,
            Assets,
        }

        [SerializeField] SizeDashboardView overview = new SizeDashboardView();
        [SerializeField] TextureOptimizerView textures = new TextureOptimizerView();
        [SerializeField] SoundOptimizerView sounds = new SoundOptimizerView();
        [SerializeField] AssetsView assets = new AssetsView();
        [SerializeField] Tab tab;

        DropdownField sceneField;
        Label staleLabel;
        LunaSettings settings;                // luna.json as of settingsTime (re-read only when the file changes)
        DateTime settingsTime;
        readonly List<string> scenePaths = new List<string>();
        Label budgetLabel;
        VisualElement budgetBar;
        VisualElement budgetFill;
        Label pendingLabel;
        ToolbarButton saveButton;
        ToolbarButton discardButton;
        ToolbarButton exclusionsButton;
        readonly List<ToolbarToggle> tabToggles = new List<ToolbarToggle>();
        VisualElement contentHost;
        readonly Dictionary<OptimizerView, VisualElement> tabElements = new Dictionary<OptimizerView, VisualElement>();

        [MenuItem(MENU_PATH)]
        static void Open()
        {
            GetWindow<LunaOptimizerWindow>("Luna Optimizer");
        }

        public static void OpenTab(Tab tab)
        {
            var window = GetWindow<LunaOptimizerWindow>("Luna Optimizer");
            window.SelectTab(tab);
        }

        // Playable bytes from the Overview (last measure, or the scene estimate when the scene was not built);
        // 0 when unknown or the hub is closed.
        public static double SizeOf(string assetPath)
        {
            var hub = Resources.FindObjectsOfTypeAll<LunaOptimizerWindow>().FirstOrDefault();
            return hub != null && hub.overview.IsEnabled ? hub.overview.SizeOf(assetPath) : 0;
        }

        static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        LunaSettings Settings
        {
            get
            {
                var path = Path.Combine(ProjectRoot, LunaSettings.FILE_NAME);
                var time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (settings == null || time != settingsTime)
                {
                    settings = LunaSettings.Load(ProjectRoot);
                    settingsTime = time;
                }
                return settings;
            }
        }

        public static void NotifyLunaSettingsChanged()
        {
            foreach (var hub in Resources.FindObjectsOfTypeAll<LunaOptimizerWindow>())
            {
                hub.overview.ReloadSettings();
                hub.Repaint();
            }
        }

        OptimizerView[] Views
        {
            get { return new OptimizerView[] { overview, textures, sounds, assets }; }
        }

        OptimizerView Current
        {
            get { return Views[(int)tab]; }
        }

        void OnEnable()
        {
            // The overview feeds the header's size line, so it runs even while another tab is shown.
            overview.Attach(this, RepaintContent);
            Current.Attach(this, RepaintContent);
        }

        void OnDisable()
        {
            foreach (var view in Views)
                view.Detach();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(STYLE_PATH);
            if (style != null)
                root.styleSheets.Add(style);
            root.EnableInClassList("lo-light", !EditorGUIUtility.isProSkin);
            root.Add(CreateHeader());
            root.Add(CreateTabs());

            contentHost = new VisualElement();
            contentHost.AddToClassList("lo-content");
            root.Add(contentHost);
            tabElements.Clear();
            ShowCurrentTab();

            root.schedule.Execute(RefreshHeader).Every(HEADER_REFRESH_MS);
            RefreshHeader();
        }

        VisualElement CreateHeader()
        {
            var header = new Toolbar();
            header.AddToClassList("lo-header");

            sceneField = new DropdownField("Scene") { tooltip = "The scene Luna builds (luna.json). Changing it is saved to Luna and can be undone." };
            sceneField.AddToClassList("lo-scene-field");
            sceneField.RegisterValueChangedCallback(change =>
            {
                // UI Toolkit reuses event objects after dispatch: copy the value before deferring. The window's own
                // scheduler runs it right after this event (EditorApplication.delayCall can stall while Unity is unfocused).
                var label = change.newValue;
                rootVisualElement.schedule.Execute(() => ChangeBuildScene(label));
            });
            header.Add(sceneField);
            header.Add(new ToolbarButton(OpenBuildScene) { text = "Open", tooltip = "Open the build scene in the editor." });
            staleLabel = new Label();
            staleLabel.AddToClassList("lo-stale");
            header.Add(staleLabel);

            budgetLabel = new Label { tooltip = "Playable size from the Overview tab (last Measure)." };
            budgetLabel.AddToClassList("lo-budget-label");
            header.Add(budgetLabel);
            budgetBar = new VisualElement();
            budgetBar.AddToClassList("lo-budget-bar");
            budgetFill = new VisualElement();
            budgetFill.AddToClassList("lo-budget-fill");
            budgetBar.Add(budgetFill);
            header.Add(budgetBar);

            var spacer = new VisualElement();
            spacer.AddToClassList("lo-spacer");
            header.Add(spacer);

            pendingLabel = new Label();
            pendingLabel.AddToClassList("lo-pending");
            header.Add(pendingLabel);
            saveButton = new ToolbarButton(SaveAll) { text = "Save all", tooltip = "Write every tab's unsaved changes to Luna (each one can be undone)." };
            header.Add(saveButton);
            discardButton = new ToolbarButton(DiscardAll) { text = "Discard all", tooltip = "Forget every tab's unsaved changes." };
            header.Add(discardButton);
            exclusionsButton = new ToolbarButton(LunaExclusionWindow.Open) { tooltip = "Open the Luna Exclusions window: pending exclusions, what uses them, and what is excluded now." };
            header.Add(exclusionsButton);
            ToolbarButton undoButton = null;
            undoButton = new ToolbarButton(() => LunaSaveHistory.ShowRevertMenuAll(undoButton.worldBound, Path.GetDirectoryName(Application.dataPath), Reload))
            {
                text = "Undo ▾",
                tooltip = "Put a part of Luna's settings back to before one of this tool's saves.",
            };
            header.Add(undoButton);
            return header;
        }

        VisualElement CreateTabs()
        {
            var bar = new VisualElement();
            bar.AddToClassList("lo-tabs");
            tabToggles.Clear();
            foreach (Tab value in Enum.GetValues(typeof(Tab)))
            {
                var tabValue = value;
                var toggle = new ToolbarToggle { value = tabValue == tab };
                toggle.AddToClassList("lo-tab");
                toggle.RegisterValueChangedCallback(change =>
                {
                    if (change.newValue)
                        SelectTab(tabValue);
                    else if (tab == tabValue)
                        toggle.SetValueWithoutNotify(true);
                });
                tabToggles.Add(toggle);
                bar.Add(toggle);
            }
            return bar;
        }

        void SelectTab(Tab value)
        {
            tab = value;
            for (int i = 0; i < tabToggles.Count; i++)
                tabToggles[i].SetValueWithoutNotify(i == (int)value);
            GUI.FocusControl(null);
            ShowCurrentTab();
            RefreshHeader();
            Repaint();
        }

        // Each tab's element is built once: its own UI Toolkit tree, or an IMGUIContainer around its OnGUI.
        void ShowCurrentTab()
        {
            if (contentHost == null)
                return;
            var current = Current;
            current.Attach(this, RepaintContent);
            VisualElement element;
            if (!tabElements.TryGetValue(current, out element))
            {
                element = current.CreateUI();
                if (element == null)
                {
                    var view = current;
                    element = new IMGUIContainer(() =>
                    {
                        view.Attach(this, RepaintContent);
                        view.OnGUI();
                    });
                }
                element.style.flexGrow = 1;
                tabElements[current] = element;
                contentHost.Add(element);
            }
            foreach (var pair in tabElements)
                pair.Value.style.display = pair.Key == current ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RepaintContent()
        {
            foreach (var element in tabElements.Values.OfType<IMGUIContainer>())
                element.MarkDirtyRepaint();
            Repaint();
        }

        // Labels that follow the views' state: scene, size line, unsaved count, tab badges, exclusions count.
        void RefreshHeader()
        {
            if (budgetLabel == null)
                return;
            RefreshScene();
            float ratio;
            var budget = overview.BudgetSummary(out ratio);
            budgetLabel.text = budget ?? "Size: not measured yet";
            budgetBar.style.display = budget != null && ratio > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            budgetFill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
            budgetFill.EnableInClassList("lo-budget-fill--warn", ratio > WARN_RATIO && ratio <= 1f);
            budgetFill.EnableInClassList("lo-budget-fill--over", ratio > 1f);

            var views = Views;
            var pending = views.Where(v => v.IsEnabled).Sum(v => v.PendingCount);
            pendingLabel.text = pending > 0 ? pending + " unsaved change(s)" : string.Empty;
            saveButton.SetEnabled(pending > 0);
            discardButton.SetEnabled(pending > 0);

            var queued = ExclusionQueue.Paths.Count;
            exclusionsButton.text = "Exclusions" + (queued > 0 ? " (" + queued + ")" : string.Empty);

            for (int i = 0; i < tabToggles.Count && i < views.Length; i++)
            {
                var count = views[i].IsEnabled ? views[i].PendingCount : 0;
                tabToggles[i].text = views[i].Title + (count > 0 ? "  (" + count + ")" : string.Empty);
            }
        }

        // Build Settings scenes (plus Luna's own), named by file; folders added only when two scenes share a name.
        void RefreshScene()
        {
            var current = Settings;
            scenePaths.Clear();
            scenePaths.AddRange(EditorBuildSettings.scenes.Select(s => s.path));
            if (current != null)
                scenePaths.AddRange(current.Scenes);
            var distinct = scenePaths.Where(File.Exists).Distinct().ToList();
            scenePaths.Clear();
            scenePaths.AddRange(distinct);
            var labels = scenePaths.Select(SceneLabel).ToList();
            if (sceneField.choices == null || !sceneField.choices.SequenceEqual(labels))
                sceneField.choices = labels;
            var buildScene = current != null && current.Scenes.Count > 0 ? current.Scenes[0] : null;
            var index = buildScene != null ? scenePaths.IndexOf(buildScene) : -1;
            sceneField.SetValueWithoutNotify(index >= 0 ? labels[index] : "(none)");

            var built = LunaBuildScenes.LastBuild(ProjectRoot);
            var stale = LunaBuildScenes.IsStale(ProjectRoot, current);
            staleLabel.style.display = stale ? DisplayStyle.Flex : DisplayStyle.None;
            var sceneName = current != null && current.Scenes.Count > 0 ? Path.GetFileNameWithoutExtension(current.Scenes[0]) : "this scene";
            staleLabel.text = "≈ " + sceneName + " not built: sizes partly estimated";
            staleLabel.tooltip = "The last Luna build was of " + string.Join(", ", built.Select(Path.GetFileNameWithoutExtension)) + ". Scripts and textures are live; "
                + "other assets use sizes remembered from earlier measures (exact for unchanged assets) or are estimated. Build once and Measure for exact numbers.";
            overview.StaleSceneNote = stale
                ? sceneName + " has not been built (last build: " + string.Join(", ", built.Select(Path.GetFileNameWithoutExtension)) + "). Scripts and textures are live; "
                    + "every other category is estimated for " + sceneName + ": each asset is 'remembered' (its exact size from an earlier measure) or 'estimated' "
                    + "(meshes: exact raw size × typical compression; others: typical size). Build once and Measure for exact numbers."
                : null;
        }

        string SceneLabel(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return scenePaths.Count(p => Path.GetFileNameWithoutExtension(p) == name) > 1 ? name + "  (" + Path.GetDirectoryName(path) + ")" : name;
        }

        // Asks about unsaved changes first: they belong to the current scene's asset list.
        void ChangeBuildScene(string label)
        {
            var index = sceneField.choices.IndexOf(label);
            var current = Settings;
            if (index < 0 || current == null || (current.Scenes.Count > 0 && current.Scenes[0] == scenePaths[index]))
                return;
            var path = scenePaths[index];
            var pending = Views.Where(v => v.IsEnabled).Sum(v => v.PendingCount);
            if (pending > 0)
            {
                var choice = EditorUtility.DisplayDialogComplex("Change build scene", pending + " unsaved change(s) belong to the current scene's assets. Save them first?",
                    "Save all", "Cancel", "Discard");
                if (choice == 1)
                {
                    RefreshHeader();
                    return;
                }
                if (choice == 0)
                    SaveAll();
            }
            string error;
            if (!LunaSettingsWriter.SaveBuildScene(path, ProjectRoot, out error))
            {
                EditorUtility.DisplayDialog("Change build scene", "Nothing was changed.\n\n" + error, "OK");
                RefreshHeader();
                return;
            }
            Reload();
            ShowNotification(new GUIContent("Luna now builds " + Path.GetFileNameWithoutExtension(path)));
        }

        void OpenBuildScene()
        {
            var current = Settings;
            if (current == null || current.Scenes.Count == 0 || !File.Exists(current.Scenes[0]))
                return;
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(current.Scenes[0]);
        }

        void SaveAll()
        {
            foreach (var view in Views.Where(v => v.IsEnabled && v.PendingCount > 0))
                view.SavePending();
            RefreshHeader();
        }

        void DiscardAll()
        {
            foreach (var view in Views.Where(v => v.IsEnabled && v.PendingCount > 0))
                view.DiscardPending();
            RefreshHeader();
        }

        // After an undo every enabled tab reloads what Luna now has saved.
        void Reload()
        {
            foreach (var view in Views.Where(v => v.IsEnabled))
                view.DiscardPending();
            overview.ReloadSettings();
            RefreshHeader();
            Repaint();
        }
    }
}
