using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Every asset's size in the playable as Luna's statistics reported it, per network, from every measure of every
    // scene. A scene that was never built reuses these exact sizes for the assets it shares with scenes that were.
    // The asset file's write time is kept so a changed file can be flagged.
    public class AssetSizeMemory
    {
        const string MEMORY_PATH = LunaStatsRunner.WORK_FOLDER + "/asset-memory.json";

        [Serializable]
        public class Entry
        {
            public string network;
            public string category;
            public string path;
            public string title;
            public double size;
            public long fileTicks;   // asset file write time when measured (0 when the path is not a file)
        }

        [Serializable]
        class Entries
        {
            public List<Entry> items = new List<Entry>();
        }

        readonly string filePath;
        readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>();

        public AssetSizeMemory(string projectRoot)
        {
            filePath = Path.Combine(projectRoot, MEMORY_PATH);
            if (!File.Exists(filePath))
                return;
            var entries = JsonUtility.FromJson<Entries>(File.ReadAllText(filePath));
            if (entries == null || entries.items == null)
                return;
            foreach (var entry in entries.items)
                byKey[Key(entry.network, entry.category, entry.path, entry.title)] = entry;
        }

        static string Key(string network, string category, string path, string title)
        {
            return network + "|" + category + "|" + (path ?? string.Empty) + "|" + (title ?? string.Empty);
        }

        // Adds (or refreshes) every asset of a measurement. Textures are kept too: they serve as a fallback.
        public void Record(LunaMeasurement measurement, string projectRoot)
        {
            if (measurement == null || measurement.stats == null || measurement.noBuild)
                return;
            var network = measurement.network ?? "develop";
            foreach (var type in measurement.stats.types)
            {
                foreach (var asset in type.assets)
                {
                    if (asset.size <= 0)
                        continue;
                    byKey[Key(network, type.title, asset.path, asset.title)] = new Entry
                    {
                        network = network,
                        category = type.title,
                        path = asset.path,
                        title = asset.title,
                        size = asset.size,
                        fileTicks = FileTicks(projectRoot, asset.path),
                    };
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonUtility.ToJson(new Entries { items = byKey.Values.ToList() }));
        }

        // Exact match on path and title, else every entry of that path in the category (an FBX's meshes or clips).
        public List<Entry> Find(string network, string category, string path, string title)
        {
            Entry entry;
            if (title != null && byKey.TryGetValue(Key(network, category, path, title), out entry))
                return new List<Entry> { entry };
            return byKey.Values.Where(e => e.network == network && e.category == category && e.path == path
                && (title == null || e.title == title)).ToList();
        }

        public List<Entry> ForCategory(string network, string category)
        {
            return byKey.Values.Where(e => e.network == network && e.category == category).ToList();
        }

        // Typical size of one asset of a category on a network (median), 0 when nothing was measured.
        public double Median(string network, string category)
        {
            var sizes = byKey.Values.Where(e => e.network == network && e.category == category).Select(e => e.size).OrderBy(s => s).ToList();
            return sizes.Count > 0 ? sizes[sizes.Count / 2] : 0;
        }

        public static long FileTicks(string projectRoot, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return 0;
            var full = Path.Combine(projectRoot, assetPath);
            return File.Exists(full) ? File.GetLastWriteTimeUtc(full).Ticks : 0;
        }
    }
}
