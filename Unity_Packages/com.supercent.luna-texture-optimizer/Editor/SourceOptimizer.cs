using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public class SourceCandidate
    {
        public string Label;
        public string FilePath;      // null keeps the current source file
        public bool Uncompressed;    // set Unity Compression to None
        public LunaPrediction Prediction;

        public bool ChangesSomething
        {
            get { return FilePath != null || Uncompressed; }
        }
    }

    public class OptimizationResult
    {
        public TextureEntry Entry;
        public List<SourceCandidate> Candidates = new List<SourceCandidate>();
        public SourceCandidate Best;
        public string Error;
    }

    // Compresses PNG sources in place, but only when Luna's predicted output actually gets smaller.
    // Candidates are judged on temporary asset copies, so the real asset is only touched by Apply.
    public static class SourceOptimizer
    {
        const double MIN_SAVING_BYTES = 256;
        const double MIN_SAVING_RATIO = 0.02;
        const string WORK_FOLDER = "Library/LunaTextureOptimizer/Optimize";
        const string BACKUP_FOLDER = "Library/LunaTextureOptimizer/Backup";
        const string QUANTIZE_CACHE_FOLDER = "Library/LunaTextureOptimizer/QuantizeCache";
        const string TEMP_ASSET_PARENT = "Assets";
        const string TEMP_ASSET_FOLDER_NAME = "__LunaTextureOptimizerTemp";
        const string TEMP_ASSET_FOLDER = TEMP_ASSET_PARENT + "/" + TEMP_ASSET_FOLDER_NAME;
        static readonly string[] PLATFORM_NAMES = { "Standalone", "iPhone", "Android", "WebGL" };
        static readonly Regex META_GUID = new Regex("guid: [0-9a-f]{32}");

        public static bool CanOptimize(TextureEntry entry)
        {
            return entry.Extension == ".png";
        }

        // Evaluates all textures together: pngquant variants run in parallel, Sprite-type candidates are imported
        // in one batch, Default-type PNG candidates skip the import (Luna reads their file as is), and every
        // candidate is predicted in one parallel pass. Returns null when canceled.
        public static List<OptimizationResult> EvaluateMany(IList<TextureEntry> entries, LunaInstall install, string projectRoot,
            LunaSettings settings, PredictionCache cache)
        {
            const string TITLE = "Evaluating sources";
            var results = entries.Select(e => new OptimizationResult { Entry = e }).ToList();
            var variants = new Dictionary<TextureEntry, List<KeyValuePair<string, string>>>();
            foreach (var entry in entries)
            {
                var workPath = Path.Combine(projectRoot, WORK_FOLDER, AssetDatabase.AssetPathToGUID(entry.AssetPath));
                if (Directory.Exists(workPath))
                    Directory.Delete(workPath, true);
                Directory.CreateDirectory(workPath);
                variants[entry] = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("original", null),
                    new KeyValuePair<string, string>("pngquant", Path.Combine(workPath, "dither.png")),
                    new KeyValuePair<string, string>("pngquant no dither", Path.Combine(workPath, "nodither.png")),
                };
            }

            // 1. Quantize every source in parallel; a variant whose tool failed is dropped.
            var quantizeJobs = variants.SelectMany(v => v.Value.Where(f => f.Value != null)
                .Select(f => new KeyValuePair<string, string>(Path.Combine(projectRoot, v.Key.AssetPath), f.Value))).ToList();
            var quantizeCache = Path.Combine(projectRoot, QUANTIZE_CACHE_FOLDER);
            Directory.CreateDirectory(quantizeCache);
            if (!ParallelWork.Run(quantizeJobs, job => Quantize(install, quantizeCache, job.Key, job.Value), TITLE, "Compressing with pngquant"))
                return null;

            // 2. One entry to predict per candidate.
            var toPredict = new List<TextureEntry>();
            var owners = new List<KeyValuePair<OptimizationResult, SourceCandidate>>();
            var temporaryImports = new List<KeyValuePair<TextureEntry, SourceCandidate>>();
            foreach (var result in results)
            {
                var entry = result.Entry;
                var current = new SourceCandidate { Label = "current", Prediction = entry.HasPrediction ? entry.Prediction : null };
                result.Candidates.Add(current);
                if (current.Prediction == null)
                {
                    toPredict.Add(entry);
                    owners.Add(new KeyValuePair<OptimizationResult, SourceCandidate>(result, current));
                }

                var compressionModes = entry.UnityCompressionAffectsLuna ? new[] { false, true } : new[] { false };
                foreach (var file in variants[entry])
                {
                    if (file.Value != null && !File.Exists(file.Value))
                        continue;
                    foreach (var uncompressed in compressionModes)
                    {
                        if (file.Value == null && !uncompressed)
                            continue;
                        var candidate = new SourceCandidate
                        {
                            Label = file.Key + (uncompressed ? " + Unity Compression None" : string.Empty),
                            FilePath = file.Value,
                            Uncompressed = uncompressed,
                        };
                        result.Candidates.Add(candidate);
                        if (LunaInputExporter.UsesSourceFile(entry))
                        {
                            var direct = entry.Clone();
                            direct.SourceFileOverride = file.Value ?? Path.Combine(projectRoot, entry.AssetPath);
                            toPredict.Add(direct);
                            owners.Add(new KeyValuePair<OptimizationResult, SourceCandidate>(result, candidate));
                        }
                        else
                            temporaryImports.Add(new KeyValuePair<TextureEntry, SourceCandidate>(entry, candidate));
                    }
                }
            }

            // 3. Import the candidates Luna reads through Unity's importer in one batch, then predict everything.
            try
            {
                var imported = ImportTemporary(temporaryImports, projectRoot, settings);
                for (int i = 0; i < imported.Count; i++)
                {
                    toPredict.Add(imported[i]);
                    owners.Add(new KeyValuePair<OptimizationResult, SourceCandidate>(results.First(r => r.Entry == temporaryImports[i].Key), temporaryImports[i].Value));
                }
                var predictions = LunaPipeline.PredictMany(toPredict, install, projectRoot, cache, TITLE);
                for (int i = 0; i < owners.Count; i++)
                    owners[i].Value.Prediction = predictions[i] ?? new LunaPrediction { error = "canceled" };
            }
            finally
            {
                AssetDatabase.DeleteAsset(TEMP_ASSET_FOLDER);
            }

            foreach (var result in results)
                PickBest(result);
            return results;
        }

        // A change must save a meaningful amount; otherwise the source stays untouched.
        static void PickBest(OptimizationResult result)
        {
            var baseline = result.Candidates[0].Prediction;
            if (!baseline.success)
            {
                result.Error = baseline.error;
                return;
            }
            var smallest = result.Candidates.Where(c => c.Prediction.success).OrderBy(c => c.Prediction.PlayableBytes).First();
            var saving = baseline.PlayableBytes - smallest.Prediction.PlayableBytes;
            var worthIt = saving >= MIN_SAVING_BYTES && saving >= baseline.PlayableBytes * MIN_SAVING_RATIO;
            result.Best = worthIt ? smallest : result.Candidates[0];
        }

        // Worker thread. pngquant output depends only on the source bytes and the options, so it is cached by both.
        static void Quantize(LunaInstall install, string cacheFolder, string sourcePath, string outputPath)
        {
            var options = new List<string> { "--force", "--strip", "--speed", "1" };
            if (outputPath.EndsWith("nodither.png"))
                options.Add("--nofs");
            options.Add("256");

            string cachedPath;
            using (var sha = SHA1.Create())
            {
                var key = sha.ComputeHash(File.ReadAllBytes(sourcePath).Concat(Encoding.UTF8.GetBytes(install.Version + string.Join(" ", options))).ToArray());
                cachedPath = Path.Combine(cacheFolder, BitConverter.ToString(key).Replace("-", string.Empty) + ".png");
            }
            if (File.Exists(cachedPath))
            {
                File.Copy(cachedPath, outputPath, true);
                return;
            }

            string error;
            if (!ProcessRunner.Run(install.PngquantPath, options.Concat(new[] { sourcePath, "--output", outputPath }), out error))
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                return;
            }
            // Copy then move, so no other worker ever reads a half-written cache file.
            var partialPath = cachedPath + "." + Guid.NewGuid().ToString("N");
            File.Copy(outputPath, partialPath);
            try
            {
                File.Move(partialPath, cachedPath);
            }
            catch (IOException)
            {
                // Another worker cached the same result first.
                File.Delete(partialPath);
            }
        }

        // Copies each candidate next to a clone of the original's .meta, so Unity imports it with the same settings.
        // All copies import in one batch; the Compression None ones get a second batch with the changed setting.
        static List<TextureEntry> ImportTemporary(List<KeyValuePair<TextureEntry, SourceCandidate>> candidates, string projectRoot,
            LunaSettings settings)
        {
            var imported = new List<TextureEntry>();
            if (candidates.Count == 0)
                return imported;
            if (!AssetDatabase.IsValidFolder(TEMP_ASSET_FOLDER))
                AssetDatabase.CreateFolder(TEMP_ASSET_PARENT, TEMP_ASSET_FOLDER_NAME);

            var paths = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var original = candidates[i].Key;
                var tempPath = TEMP_ASSET_FOLDER + "/" + i + Path.GetExtension(original.AssetPath);
                var tempFullPath = Path.Combine(projectRoot, tempPath);
                File.Copy(candidates[i].Value.FilePath ?? Path.Combine(projectRoot, original.AssetPath), tempFullPath, true);
                var meta = File.ReadAllText(Path.Combine(projectRoot, original.AssetPath + ".meta"));
                File.WriteAllText(tempFullPath + ".meta", META_GUID.Replace(meta, "guid: " + GUID.Generate(), 1));
                paths.Add(tempPath);
            }

            EditorUtility.DisplayProgressBar("Evaluating sources", "Importing " + paths.Count + " candidates", 0f);
            ImportBatch(paths, path => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate));
            ImportBatch(paths.Where((p, i) => candidates[i].Value.Uncompressed).ToList(), path =>
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                MakeUncompressed(importer);
                importer.SaveAndReimport();
            });

            for (int i = 0; i < candidates.Count; i++)
            {
                var entry = BuildTextureScanner.CreateEntry(projectRoot, paths[i], settings, null);
                entry.LunaRule = candidates[i].Key.LunaRule;
                entry.IsLunaOverride = candidates[i].Key.IsLunaOverride;
                imported.Add(entry);
            }
            return imported;
        }

        static void ImportBatch(List<string> paths, Action<string> import)
        {
            if (paths.Count == 0)
                return;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                    import(path);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        // Backs up the original file and .meta (first time only), then writes the winning candidate.
        public static void Apply(OptimizationResult result, string projectRoot)
        {
            var best = result.Best;
            if (best == null || !best.ChangesSomething)
                return;

            var path = result.Entry.AssetPath;
            var fullPath = Path.Combine(projectRoot, path);
            Backup(projectRoot, path);
            if (best.FilePath != null)
                File.Copy(best.FilePath, fullPath, true);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (best.Uncompressed)
            {
                MakeUncompressed(importer);
                importer.SaveAndReimport();
            }
            else
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        public static bool HasBackups(string projectRoot)
        {
            var folder = Path.Combine(projectRoot, BACKUP_FOLDER);
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
        }

        // Puts every backed-up file and .meta back and reimports it.
        public static int RestoreAll(string projectRoot)
        {
            var folder = Path.Combine(projectRoot, BACKUP_FOLDER);
            if (!Directory.Exists(folder))
                return 0;

            var restored = 0;
            foreach (var backup in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (backup.EndsWith(".meta"))
                    continue;
                var assetPath = backup.Substring(folder.Length + 1).Replace('\\', '/');
                File.Copy(backup, Path.Combine(projectRoot, assetPath), true);
                if (File.Exists(backup + ".meta"))
                    File.Copy(backup + ".meta", Path.Combine(projectRoot, assetPath + ".meta"), true);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                restored++;
            }
            Directory.Delete(folder, true);
            return restored;
        }

        static void MakeUncompressed(TextureImporter importer)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            foreach (var platformName in PLATFORM_NAMES)
            {
                var platformSettings = importer.GetPlatformTextureSettings(platformName);
                if (!platformSettings.overridden)
                    continue;
                platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                platformSettings.format = TextureImporterFormat.Automatic;
                importer.SetPlatformTextureSettings(platformSettings);
            }
        }

        static void Backup(string projectRoot, string assetPath)
        {
            var backupPath = Path.Combine(projectRoot, BACKUP_FOLDER, assetPath);
            if (File.Exists(backupPath))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            File.Copy(Path.Combine(projectRoot, assetPath), backupPath);
            File.Copy(Path.Combine(projectRoot, assetPath + ".meta"), backupPath + ".meta");
        }
    }
}
