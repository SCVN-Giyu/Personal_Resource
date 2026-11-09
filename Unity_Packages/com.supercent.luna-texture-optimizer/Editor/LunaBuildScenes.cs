using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Which scenes the last Luna build exported (LunaTemp/stage1/assets/scenes/<id>.json carry their "path"),
    // so the hub can tell when sizes from that build belong to a different scene than the one Luna builds now.
    public static class LunaBuildScenes
    {
        const string SCENES_FOLDER = "LunaTemp/stage1/assets/scenes";

        [Serializable]
        class SceneFile
        {
            public string path;
        }

        static string cachedFolder;
        static DateTime cachedTime;
        static List<string> cached = new List<string>();

        // Empty when there is no build yet.
        public static IReadOnlyList<string> LastBuild(string projectRoot)
        {
            var folder = Path.Combine(projectRoot, SCENES_FOLDER);
            if (!Directory.Exists(folder))
                return new List<string>();
            var time = Directory.GetLastWriteTimeUtc(folder);
            if (folder == cachedFolder && time == cachedTime)
                return cached;
            var scenes = new List<string>();
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var scene = JsonUtility.FromJson<SceneFile>(File.ReadAllText(file));
                    if (scene != null && !string.IsNullOrEmpty(scene.path))
                        scenes.Add(scene.path);
                }
                catch (Exception)
                {
                    // A scene file being rewritten by a running build: read it next time.
                }
            }
            cachedFolder = folder;
            cachedTime = time;
            cached = scenes.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            return cached;
        }

        // True when a build exists and it exported other scenes than Luna's current build scenes.
        public static bool IsStale(string projectRoot, LunaSettings settings)
        {
            if (settings == null)
                return false;
            var built = LastBuild(projectRoot);
            if (built.Count == 0)
                return false;
            var current = settings.Scenes.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            return !built.SequenceEqual(current);
        }
    }
}
