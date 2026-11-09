using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Assets and folders picked for exclusion but not written to Luna yet. Kept in Library so the list survives
    // script reloads and editor restarts; the Luna Exclusions window reviews and applies it.
    public static class ExclusionQueue
    {
        const string QUEUE_PATH = "Library/LunaTextureOptimizer/exclusion-queue.json";

        [Serializable]
        class Items
        {
            public List<string> paths = new List<string>();
        }

        public static event Action Changed;

        static List<string> cached;

        public static IReadOnlyList<string> Paths
        {
            get { return Load(); }
        }

        // Folders are stored without a trailing slash; anything outside Assets/ is ignored.
        // True when the asset or one of its folders waits in the Pending list.
        public static bool IsQueued(string assetPath)
        {
            foreach (var path in Load())
            {
                if (assetPath == path || assetPath.StartsWith(path + "/", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static int Add(IEnumerable<string> paths)
        {
            var list = Load();
            var added = 0;
            foreach (var raw in paths)
            {
                var path = raw.TrimEnd('/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || list.Contains(path))
                    continue;
                list.Add(path);
                added++;
            }
            if (added > 0)
                Save(list);
            return added;
        }

        public static void Remove(string path)
        {
            var list = Load();
            if (list.Remove(path))
                Save(list);
        }

        public static void Clear()
        {
            var list = Load();
            if (list.Count == 0)
                return;
            list.Clear();
            Save(list);
        }

        static List<string> Load()
        {
            if (cached != null)
                return cached;
            var path = Path.Combine(ProjectRoot, QUEUE_PATH);
            var items = File.Exists(path) ? JsonUtility.FromJson<Items>(File.ReadAllText(path)) : null;
            cached = items != null && items.paths != null ? items.paths : new List<string>();
            return cached;
        }

        static void Save(List<string> list)
        {
            var path = Path.Combine(ProjectRoot, QUEUE_PATH);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(new Items { paths = list }));
            if (Changed != null)
                Changed();
        }

        static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        [MenuItem("Assets/Luna Optimizer/Add to Luna Exclusions", false, 2000)]
        static void AddProjectSelection()
        {
            LunaExclusionWindow.AddAndShow(Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath));
        }

        [MenuItem("Assets/Luna Optimizer/Add to Luna Exclusions", true)]
        static bool CanAddProjectSelection()
        {
            return Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Any(p => p.StartsWith("Assets/", StringComparison.Ordinal));
        }
    }
}
