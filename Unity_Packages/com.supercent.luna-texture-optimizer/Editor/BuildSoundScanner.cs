using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // The audio clips a Luna build ships, found the same way as textures.
    public static class BuildSoundScanner
    {
        public static List<SoundEntry> Scan(string projectRoot, LunaSettings settings, LunaBuildStats stats)
        {
            var entries = new List<SoundEntry>();
            foreach (var path in BuildTextureScanner.CollectShippedAssets(settings))
            {
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer != null)
                    entries.Add(CreateEntry(projectRoot, path, importer, settings, stats));
            }
            return entries;
        }

        static SoundEntry CreateEntry(string projectRoot, string path, AudioImporter importer, LunaSettings settings, LunaBuildStats stats)
        {
            var fullPath = Path.Combine(projectRoot, path);
            var entry = new SoundEntry
            {
                AssetPath = path,
                FileName = Path.GetFileName(path),
                Extension = Path.GetExtension(path).ToLowerInvariant(),
                FileBytes = new FileInfo(fullPath).Length,
                LoadType = importer.defaultSampleSettings.loadType,
                ForceToMono = importer.forceToMono,
            };
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                entry.Channels = clip.channels;
                entry.Frequency = clip.frequency;
                entry.LengthSeconds = clip.length;
            }
            entry.LunaRule = settings.SoundRuleFor(path, out entry.IsLunaOverride);

            if (stats != null && stats.TryGetBytes(path, out entry.LastBuildBytes))
            {
                entry.HasLastBuild = true;
                var lastChange = Max(File.GetLastWriteTime(fullPath), File.GetLastWriteTime(fullPath + ".meta"));
                entry.LastBuildOutdated = lastChange > stats.BuiltAt;
            }
            return entry;
        }

        static DateTime Max(DateTime a, DateTime b)
        {
            return a > b ? a : b;
        }
    }
}
