using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Assets and folders to leave out of the Luna build. Any tab adds to the Pending list; this window shows what
    // uses each one (the blast radius) and applies the batch to Luna's unity.assets.excludes (undoable).
    // Already excluded entries are listed with what they currently leave broken, and can be re-included.
    public class LunaExclusionWindow : EditorWindow
    {
        const string MENU_PATH = "Supercent/Luna Exclusions";
        const float INDENT = 16f;

        string projectRoot;
        LunaSettings settings;
        List<string> excluded = new List<string>();
        BlastRadius pendingResult;
        BlastRadius excludedResult;
        DateTime settingsTime;
        Vector2 scroll;
        readonly HashSet<string> expanded = new HashSet<string>();
        [NonSerialized] GUIStyle linkStyle;
        [NonSerialized] GUIStyle smallStyle;
        [NonSerialized] GUIStyle sectionStyle;
        [NonSerialized] GUIStyle warningStyle;

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            var window = GetWindow<LunaExclusionWindow>("Luna Exclusions");
            window.Reload();
        }

        // Adds to the Pending list and shows the window without taking the user out of their current window.
        public static void AddAndShow(IEnumerable<string> paths)
        {
            ExclusionQueue.Add(paths);
            var window = Resources.FindObjectsOfTypeAll<LunaExclusionWindow>().FirstOrDefault();
            if (window == null)
            {
                window = CreateInstance<LunaExclusionWindow>();
                window.titleContent = new GUIContent("Luna Exclusions");
                window.Show();
            }
            window.Reload();
        }

        void OnEnable()
        {
            ExclusionQueue.Changed += Reload;
            Reload();
        }

        void OnDisable()
        {
            ExclusionQueue.Changed -= Reload;
        }

        void Reload()
        {
            projectRoot = Path.GetDirectoryName(Application.dataPath);
            settings = LunaSettings.Load(projectRoot);
            settingsTime = settings != null ? File.GetLastWriteTimeUtc(settings.FilePath) : DateTime.MinValue;
            excluded = settings != null
                ? settings.Excludes.Where(e => e.StartsWith("Assets/", StringComparison.Ordinal) && !e.Contains("*")).OrderBy(e => e, StringComparer.Ordinal).ToList()
                : new List<string>();
            pendingResult = null;
            excludedResult = null;
            Repaint();
        }

        void OnGUI()
        {
            CreateStyles();
            if (settings != null && File.GetLastWriteTimeUtc(settings.FilePath) != settingsTime)
                Reload();
            DrawToolbar();
            if (settings == null)
            {
                EditorGUILayout.HelpBox(LunaSettings.FILE_NAME + " not found in the project root.", MessageType.Warning);
                return;
            }
            var pending = ExclusionQueue.Paths.ToList();
            if (pendingResult == null && pending.Count > 0)
                pendingResult = BlastRadius.Analyze(pending, settings);
            if (excludedResult == null && excluded.Count > 0)
                excludedResult = BlastRadius.Analyze(excluded.Select(e => e.TrimEnd('/')).ToList(), settings, excluded);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPending(pending);
            EditorGUILayout.Space(12f);
            DrawExcluded();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(new GUIContent("Refresh", "Analyze again (after scene or prefab edits)."), EditorStyles.toolbarButton, GUILayout.Width(60f)))
                Reload();
            if (GUILayout.Button(new GUIContent("Add Project selection", "Add the files/folders selected in the Project window to Pending."), EditorStyles.toolbarButton, GUILayout.Width(135f)))
                ExclusionQueue.Add(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Undo…", "Put Luna's asset exclusions back to before one of the last changes."), EditorStyles.toolbarDropDown, GUILayout.Width(60f)))
                LunaSaveHistory.ShowRevertMenu(GUILayoutUtility.GetLastRect(), LunaSettingsSection.AssetExcludes, projectRoot, AfterLunaChange);
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- pending

        void DrawPending(List<string> pending)
        {
            var targets = pendingResult != null ? pendingResult.Targets : new List<BlastRadius.Target>();
            var saved = targets.Sum(t => LunaOptimizerWindow.SizeOf(t.AssetPath));
            var uses = targets.Count(t => t.Users.Count > 0);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("PENDING (" + pending.Count + ")" + (saved > 0 ? "   saves ~" + FormatBytes(saved) : string.Empty)
                + (uses > 0 ? "   ·   " + uses + " asset(s) in use" : string.Empty), sectionStyle);
            GUILayout.FlexibleSpace();
            var blocked = pendingResult != null && pendingResult.Blockers.Count > 0;
            using (new EditorGUI.DisabledScope(targets.Count == 0 || blocked))
            {
                if (GUILayout.Button(new GUIContent("Apply to Luna", "Write the pending exclusions to luna.json (one undoable change). Your project files are not changed."), GUILayout.Width(110f)))
                    EditorApplication.delayCall += Apply;
            }
            using (new EditorGUI.DisabledScope(pending.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(55f)))
                    ExclusionQueue.Clear();
            }
            EditorGUILayout.EndHorizontal();

            if (pending.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing pending. Right-click an asset in any Luna Optimizer tab (or in the Project window: Luna Optimizer › Add to Luna Exclusions) to add it here.", MessageType.None);
                return;
            }
            EditorGUILayout.LabelField("Luna drops excluded assets silently: whatever uses them keeps a broken reference (an excluded texture renders white). Review the uses below.", smallStyle);
            if (pendingResult != null)
            {
                foreach (var blocker in pendingResult.Blockers)
                    EditorGUILayout.HelpBox(blocker, MessageType.Error);
            }
            foreach (var item in pending)
                DrawItem(item, pendingResult, true);
        }

        void Apply()
        {
            if (pendingResult == null)
                return;
            string error;
            var count = ApplyPending(pendingResult, projectRoot, out error);
            if (error != null)
            {
                EditorUtility.DisplayDialog("Apply to Luna", "Nothing was changed.\n\n" + error, "OK");
                return;
            }
            if (count == 0)
                return;
            AfterLunaChange();
            ShowNotification(new GUIContent(count + " exclusion(s) saved. Sizes update after the next Luna build."));
        }

        // Writes the pending items that Luna ships (one undoable save) and takes them off the list. Returns how many.
        public static int ApplyPending(BlastRadius result, string projectRoot, out string error)
        {
            error = null;
            var entries = new List<string>();
            var applied = new List<string>();
            foreach (var item in ExclusionQueue.Paths)
            {
                var isFolder = AssetDatabase.IsValidFolder(item);
                var prefix = item + "/";
                if (!result.Targets.Any(t => t.AssetPath == item || (isFolder && t.AssetPath.StartsWith(prefix, StringComparison.Ordinal))))
                    continue;
                entries.Add(isFolder ? prefix : item);
                applied.Add(item);
            }
            if (entries.Count == 0)
                return 0;
            var label = "Exclude: " + LunaSaveHistory.Names(applied.Select(p => Path.GetFileName(p) + (AssetDatabase.IsValidFolder(p) ? "/" : string.Empty)));
            if (!LunaSettingsWriter.SaveAssetExcludes(entries, new List<string>(), label, projectRoot, out error))
                return 0;
            foreach (var item in applied)
                ExclusionQueue.Remove(item);
            return entries.Count;
        }

        // Refreshes an open Exclusions window after a change made elsewhere (the browser page).
        public static void ReloadOpen()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<LunaExclusionWindow>())
                window.Reload();
        }

        // ---------------------------------------------------------------- excluded

        void DrawExcluded()
        {
            GUILayout.Label("EXCLUDED (" + excluded.Count + ")", sectionStyle);
            if (excluded.Count == 0)
            {
                EditorGUILayout.LabelField("Nothing is excluded yet.", smallStyle);
                return;
            }
            EditorGUILayout.LabelField("Left out of the build now. Uses listed here are references that are broken in the playable.", smallStyle);
            foreach (var entry in excluded)
                DrawItem(entry.TrimEnd('/'), excludedResult, false);
        }

        void ReInclude(string item)
        {
            var entry = excluded.FirstOrDefault(e => e.TrimEnd('/') == item);
            if (entry == null)
                return;
            string error;
            if (!LunaSettingsWriter.SaveAssetExcludes(new List<string>(), new List<string> { entry }, "Re-include: " + Path.GetFileName(item), projectRoot, out error))
            {
                EditorUtility.DisplayDialog("Re-include", "Nothing was changed.\n\n" + error, "OK");
                return;
            }
            AfterLunaChange();
        }

        void AfterLunaChange()
        {
            Reload();
            LunaOptimizerWindow.NotifyLunaSettingsChanged();
        }

        // ---------------------------------------------------------------- one item (file or folder)

        void DrawItem(string item, BlastRadius result, bool isPending)
        {
            var isFolder = AssetDatabase.IsValidFolder(item);
            var targets = result == null ? new List<BlastRadius.Target>()
                : result.Targets.Where(t => t.AssetPath == item || (isFolder && t.AssetPath.StartsWith(item + "/", StringComparison.Ordinal))).ToList();
            var key = (isPending ? "p:" : "x:") + item;
            var open = expanded.Contains(key);
            var uses = targets.Count(t => t.Users.Count > 0);
            var size = targets.Sum(t => LunaOptimizerWindow.SizeOf(t.AssetPath));
            var exists = isFolder || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(item));

            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(open ? "▼" : "▶", EditorStyles.label, GUILayout.Width(16f)))
            {
                if (open) expanded.Remove(key); else expanded.Add(key);
            }
            AssetLink(item, Path.GetFileName(item) + (isFolder ? "/  (folder, " + targets.Count + " asset(s))" : string.Empty), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!exists)
                GUILayout.Label("missing in project", warningStyle);
            else if (result == null)
                GUILayout.Label("…", smallStyle);
            else if (targets.Count == 0)
                GUILayout.Label(isPending ? "not in the build" : "nothing shipped under it", smallStyle);
            else
            {
                GUILayout.Label(size > 0 ? FormatBytes(size) : "size unknown", smallStyle);
                GUILayout.Label(uses == 0 ? "unused" : uses + " in use", uses == 0 ? smallStyle : warningStyle);
            }
            if (isPending)
            {
                if (GUILayout.Button("Remove", GUILayout.Width(65f)))
                    EditorApplication.delayCall += () => ExclusionQueue.Remove(item);
            }
            else if (GUILayout.Button(new GUIContent("Re-include", "Remove it from Luna's exclude list so the next build ships it again."), GUILayout.Width(80f)))
            {
                EditorApplication.delayCall += () => ReInclude(item);
            }
            EditorGUILayout.EndHorizontal();

            if (!open)
                return;
            if (result != null && isPending && result.NotShipped.Contains(item))
                EditorGUILayout.LabelField("    Luna does not ship it anyway, so excluding it changes nothing.", smallStyle);
            foreach (var target in targets)
            {
                if (isFolder)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(INDENT);
                    AssetLink(target.AssetPath, target.AssetPath.Substring(item.Length + 1), linkStyle);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(INDENT * (isFolder ? 2 : 1));
                GUILayout.Label(target.Users.Count == 0 ? "Nothing in the build uses it." : target.Consequence, smallStyle);
                EditorGUILayout.EndHorizontal();
                foreach (var link in target.Users)
                    DrawLink(link, isFolder ? 2 : 1);
            }
        }

        void DrawLink(BlastRadius.Link link, int depth)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * INDENT);
            GUILayout.Label("←", GUILayout.Width(14f));
            AssetLink(link.AssetPath, Path.GetFileName(link.AssetPath), linkStyle);
            if (link.RootKind != null)
                GUILayout.Label("[" + link.RootKind + "]", EditorStyles.miniBoldLabel);
            if (link.ShownElsewhere)
                GUILayout.Label("(users listed above)", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            foreach (var where in link.Where)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * INDENT + 30f);
                GUILayout.Label(new GUIContent("@ " + where, where), smallStyle);
                EditorGUILayout.EndHorizontal();
            }
            foreach (var user in link.Users)
                DrawLink(user, depth + 1);
        }

        static void AssetLink(string path, string label, GUIStyle style)
        {
            if (GUILayout.Button(new GUIContent(label, path + "\nClick to show in the Project window"), style))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                {
                    EditorUtility.FocusProjectWindow();
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        }

        void CreateStyles()
        {
            if (linkStyle != null)
                return;
            var linkColor = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.7f, 1f) : new Color(0.1f, 0.35f, 0.8f);
            linkStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = linkColor }, hover = { textColor = linkColor } };
            smallStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            warningStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.95f, 0.6f, 0.1f) } };
        }

        static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}
