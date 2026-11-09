using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Collects the assets a Luna build ships: dependencies of Luna's enabled scenes, of Resources folders
    // (when Luna exports them) and of Luna's explicit includes. Scan keeps the file-based textures.
    public static class BuildTextureScanner
    {
        const string RESOURCES_SEGMENT = "/Resources/";
        const string EDITOR_SEGMENT = "/Editor/";

        public static List<TextureEntry> Scan(string projectRoot, LunaSettings settings, LunaBuildStats stats)
        {
            var entries = new List<TextureEntry>();
            foreach (var path in CollectShippedAssets(settings))
            {
                var entry = CreateEntry(projectRoot, path, settings, stats);
                if (entry != null)
                    entries.Add(entry);
            }
            return entries;
        }

        // Every asset path a Luna build exports (any type), minus Luna's excludes.
        public static List<string> CollectShippedAssets(LunaSettings settings)
        {
            var roots = CollectRoots(settings);
            var paths = new List<string>();
            if (roots.Count == 0)
                return paths;

            foreach (var path in CollectDependencies(roots))
            {
                if (!settings.IsExcluded(path))
                    paths.Add(path);
            }
            return paths;
        }

        // What a Luna build starts from: its scenes, the always-included assets and (when exported) every Resources asset.
        public static List<string> CollectRoots(LunaSettings settings)
        {
            var roots = new List<string>();
            AddExisting(roots, settings.Scenes);
            AddExisting(roots, settings.Includes);
            if (settings.ExportsResources)
            {
                foreach (var path in AssetDatabase.GetAllAssetPaths())
                {
                    if (path.StartsWith("Assets/") && path.Contains(RESOURCES_SEGMENT) && !path.Contains(EDITOR_SEGMENT)
                        && !AssetDatabase.IsValidFolder(path))
                        roots.Add(path);
                }
            }
            return roots;
        }

        // Null when the asset is not a file-based texture.
        public static TextureEntry CreateEntry(string projectRoot, string path, LunaSettings settings, LunaBuildStats stats)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return null;
            return CreateEntry(projectRoot, path, importer, TexturePlatformName(EditorUserBuildSettings.activeBuildTarget), settings, stats);
        }

        // Like AssetDatabase.GetDependencies(roots, true), but does not walk into scripts: a script's
        // dependencies are its editor icons, which a build never ships.
        static HashSet<string> CollectDependencies(List<string> roots)
        {
            var visited = new HashSet<string>();
            var pending = new Stack<string>(roots);
            while (pending.Count > 0)
            {
                var path = pending.Pop();
                if (!visited.Add(path) || IsScript(path))
                    continue;
                foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (!visited.Contains(dependency))
                        pending.Push(dependency);
                }
            }
            return visited;
        }

        public static bool IsScript(string path)
        {
            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(MonoScript);
        }

        static void AddExisting(List<string> roots, IReadOnlyList<string> paths)
        {
            foreach (var path in paths)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    roots.Add(path);
            }
        }

        static TextureEntry CreateEntry(string projectRoot, string path, TextureImporter importer, string platformName,
            LunaSettings settings, LunaBuildStats stats)
        {
            var fullPath = Path.Combine(projectRoot, path);
            var entry = new TextureEntry
            {
                AssetPath = path,
                FileName = Path.GetFileName(path),
                Extension = Path.GetExtension(path).ToLowerInvariant(),
                FileBytes = new FileInfo(fullPath).Length,
                HasAlpha = importer.alphaSource == TextureImporterAlphaSource.FromGrayScale
                    || (importer.alphaSource == TextureImporterAlphaSource.FromInput && importer.DoesSourceTextureHaveAlpha()),
                TextureType = importer.textureType,
                UnityCompression = importer.textureCompression,
                AlphaSource = importer.alphaSource,
                NpotScale = importer.npotScale,
                IsSrgb = importer.sRGBTexture,
            };
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                entry.ImportedFormat = texture.format;
                entry.ImportedWidth = texture.width;
                entry.ImportedHeight = texture.height;
            }
            importer.GetSourceTextureWidthAndHeight(out entry.Width, out entry.Height);

            var platformSettings = importer.GetPlatformTextureSettings(platformName);
            if (platformSettings.overridden)
            {
                entry.HasPlatformOverride = true;
                entry.UnityCompression = platformSettings.textureCompression;
            }

            entry.LunaRule = settings.RuleFor(path, out entry.IsLunaOverride);

            if (stats != null && stats.TryGetBytes(path, out entry.LastBuildBytes))
            {
                entry.HasLastBuild = true;
                var lastChange = Max(File.GetLastWriteTime(fullPath), File.GetLastWriteTime(fullPath + ".meta"));
                entry.LastBuildOutdated = lastChange > stats.BuiltAt;
            }
            return entry;
        }

        // Luna exports textures as imported for the active build target.
        static string TexturePlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iPhone";
                case BuildTarget.WebGL: return "WebGL";
                default: return "Standalone";
            }
        }

        static DateTime Max(DateTime a, DateTime b)
        {
            return a > b ? a : b;
        }
    }
}
