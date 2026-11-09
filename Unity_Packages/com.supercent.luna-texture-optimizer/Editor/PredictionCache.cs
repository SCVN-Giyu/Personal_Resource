using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Predictions stay valid until the asset, its import settings, its Luna rule, the build target (textures) or Luna change.
    public class PredictionCache
    {
        const string CACHE_PATH = "Library/LunaTextureOptimizer/predictions.json";

        [Serializable]
        class Record
        {
            public string key;
            public LunaPrediction prediction;
        }

        [Serializable]
        class Records
        {
            public List<Record> items = new List<Record>();
        }

        readonly string filePath;
        readonly Dictionary<string, LunaPrediction> predictions = new Dictionary<string, LunaPrediction>();

        public PredictionCache(string projectRoot)
        {
            filePath = Path.Combine(projectRoot, CACHE_PATH);
            if (!File.Exists(filePath))
                return;
            var records = JsonUtility.FromJson<Records>(File.ReadAllText(filePath));
            if (records == null || records.items == null)
                return;
            foreach (var record in records.items)
                predictions[record.key] = record.prediction;
        }

        public static string KeyFor(TextureEntry entry, string projectRoot, LunaInstall install)
        {
            var fullPath = Path.Combine(projectRoot, entry.AssetPath);
            var rule = entry.LunaRule;
            return string.Join("|", new[]
            {
                entry.AssetPath,
                File.GetLastWriteTimeUtc(fullPath).Ticks.ToString(),
                File.GetLastWriteTimeUtc(fullPath + ".meta").Ticks.ToString(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                install != null ? install.Version : string.Empty,
                rule.maxWidth + "x" + rule.maxHeight, rule.format, rule.compression, rule.quality.ToString(),
            });
        }

        public static string KeyFor(SoundEntry entry, string projectRoot, LunaInstall install)
        {
            var fullPath = Path.Combine(projectRoot, entry.AssetPath);
            var rule = entry.LunaRule;
            return string.Join("|", new[]
            {
                "sound", entry.AssetPath,
                File.GetLastWriteTimeUtc(fullPath).Ticks.ToString(),
                File.GetLastWriteTimeUtc(fullPath + ".meta").Ticks.ToString(),
                install != null ? install.Version : string.Empty,
                rule.bitrate.ToString(), rule.channels.ToString(), rule.startTime, rule.endTime,
                rule.lengthInSeconds.ToString("R"), rule.script, rule.ext,
            });
        }

        public LunaPrediction Get(string key)
        {
            LunaPrediction prediction;
            return predictions.TryGetValue(key, out prediction) ? prediction : null;
        }

        public void Set(string key, LunaPrediction prediction)
        {
            predictions[key] = prediction;
        }

        public void Save()
        {
            var records = new Records();
            foreach (var pair in predictions)
                records.items.Add(new Record { key = pair.Key, prediction = pair.Value });
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonUtility.ToJson(records));
        }
    }
}
