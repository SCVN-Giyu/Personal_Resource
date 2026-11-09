using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Texture and sound sizes from the last Luna build (LunaTemp/stage3/stats.json, the data behind Luna's Size Breakdown).
    // Sizes are what each asset costs inside the playable, i.e. already including base64 inflation.
    public class LunaBuildStats
    {
        const string STATS_PATH = "LunaTemp/stage3/stats.json";
        const string TEXTURES_TYPE = "textures";
        const string SOUNDS_TYPE = "sounds";

        [Serializable]
        class Root
        {
            public TypeEntry[] types;
        }

        [Serializable]
        class TypeEntry
        {
            public string title;
            public AssetEntry[] assets;
        }

        [Serializable]
        class AssetEntry
        {
            public string path;
            public double size;
        }

        public DateTime BuiltAt { get; private set; }

        readonly Dictionary<string, double> assetBytes = new Dictionary<string, double>();

        public static LunaBuildStats Load(string projectRoot)
        {
            var path = Path.Combine(projectRoot, STATS_PATH);
            if (!File.Exists(path))
                return null;

            var root = JsonUtility.FromJson<Root>(File.ReadAllText(path));
            var stats = new LunaBuildStats { BuiltAt = File.GetLastWriteTime(path) };
            if (root.types == null)
                return stats;

            foreach (var type in root.types)
            {
                if ((type.title != TEXTURES_TYPE && type.title != SOUNDS_TYPE) || type.assets == null)
                    continue;
                foreach (var asset in type.assets)
                {
                    if (string.IsNullOrEmpty(asset.path))
                        continue;
                    double bytes;
                    stats.assetBytes.TryGetValue(asset.path, out bytes);
                    stats.assetBytes[asset.path] = bytes + asset.size;
                }
            }
            return stats;
        }

        public bool TryGetBytes(string assetPath, out double bytes)
        {
            return assetBytes.TryGetValue(assetPath, out bytes);
        }
    }
}
