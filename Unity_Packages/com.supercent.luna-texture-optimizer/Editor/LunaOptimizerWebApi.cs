using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // What the browser page reads and does, on Unity's main thread, through the same hub views as the Unity window
    // (so predictions, saves and undo history are shared). LunaOptimizerWebServer only moves bytes.
    // Actions that would open a Unity dialog are split: the page shows the text and asks, then calls the confirmed path.
    public static class LunaOptimizerWebApi
    {
        public class Response
        {
            public int Status = 200;
            public string ContentType = "application/json";
            public byte[] Body;
            public string FilePath;   // served from disk instead of Body
        }

        [Serializable]
        public class ActionRequest
        {
            public string name;
            public string path;
            public string value;
            public string value2;
            public string[] paths;
            public int a;
            public int b;
            public int c;
            public float start;
            public float end;
            public bool flag;
            public bool confirmed;
        }

        const int THUMB_SIZE = 64;
        const int ASSET_PREVIEW_SIZE = 256;
        static readonly Dictionary<string, byte[]> thumbnails = new Dictionary<string, byte[]>();
        static readonly Dictionary<string, BlastRadius> usedBy = new Dictionary<string, BlastRadius>();
        static string usedByStamp;
        static string exclusionsKey;
        static BlastRadius pendingResult;
        static BlastRadius excludedResult;

        static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath); }
        }

        // ---------------------------------------------------------------- GET

        public static Response Get(string path, NameValueCollection query)
        {
            var hub = LunaOptimizerWindow.Hub();
            switch (path)
            {
                case "/api/state": return Json(State(hub));
                case "/api/overview": return Json(hub.OverviewView.BrowserData());
                case "/api/texture": return Json(TextureDetail(hub, query["path"]));
                case "/api/texture-image": return TextureImage(hub, query["path"], query["kind"]);
                case "/api/thumb": return Png(Thumbnail(query["path"]));
                case "/api/sound": return Json(SoundDetail(hub, query["path"]));
                case "/api/audio": return Audio(hub, query["path"], query["kind"]);
                case "/api/sound-bitrates": return Json(hub.SoundsView.BulkBitrateValues(Paths(query["paths"]), ParseInt(query["format"])));
                case "/api/assets": return Json(Assets(hub));
                case "/api/asset": return Json(AssetDetail(hub, query["path"]));
                case "/api/asset-preview": return AssetPreviewImage(query["path"]);
                case "/api/exclusions": return Json(Exclusions(hub));
                case "/api/history": return Json(History());
                default: return Text(404, "Not found");
            }
        }

        static Dictionary<string, object> State(LunaOptimizerWindow hub)
        {
            var overview = hub.OverviewView;
            var textures = hub.TexturesView;
            var sounds = hub.SoundsView;
            int webp, jpeg;
            string hint;
            SimpleModeView.PresetQualities(SimpleModeView.ChosenPreset, out webp, out jpeg, out hint);
            var state = new Dictionary<string, object>
            {
                { "project", Path.GetFileName(ProjectRoot) },
                { "scene", hub.BuildSceneName },
                { "scenePath", hub.BuildScenePath },
                { "scenes", hub.ScenePaths() },
                { "sceneStale", hub.BuildSceneStale },
                { "network", overview.Network },
                { "lunaFound", overview.HasLunaInstall },
                { "measured", overview.HasMeasurement && overview.TotalBytes > 0 },
                { "measuring", overview.IsMeasuring },
                { "measuredAt", overview.MeasuredAtText },
                { "problem", overview.LastMessage },
                { "total", overview.TotalBytes },
                { "limit", overview.Limit },
                { "pending", hub.PendingTotal },
                { "exclusionsPending", ExclusionQueue.Paths.Count },
                { "presets", SimpleModeView.PresetNames },
                { "preset", SimpleModeView.ChosenPreset },
                { "presetHint", hint },
                { "webpQuality", textures.WebpQuality },
                { "jpegQuality", textures.JpegQuality },
                { "textureBulkFormats", textures.BulkFormatLabels() },
                { "textureBulkSizes", textures.BulkSizeLabels() },
                { "canOptimizeSources", textures.CanOptimizeSourcesNow },
                { "hasSourceBackups", textures.HasSourceBackups },
                { "soundFormats", SoundOptimizerView.FormatLabels() },
                { "soundBulkChannels", SoundOptimizerView.BulkChannelLabels() },
                { "staleSoundOverrides", sounds.StaleOverrides.ToList() },
                { "staleSoundPlan", sounds.StaleOverrides.Count > 0 ? sounds.StaleRepairPlan() : null },
                { "opusCount", sounds.OpusCount },
                { "opusRisks", LunaSoundFormat.RISKS },
                { "textures", new Dictionary<string, object>
                    {
                        { "count", textures.Count }, { "predictedCount", textures.PredictedCount }, { "predictedBytes", textures.PredictedTotalBytes },
                        { "pending", textures.PendingCount }, { "saving", textures.PendingSavingBytes }, { "canPredict", textures.CanPredictSizes },
                        { "compressedImport", textures.UnityCompressedCount },
                    } },
                { "sounds", new Dictionary<string, object>
                    {
                        { "count", sounds.Count }, { "predictedCount", sounds.PredictedCount }, { "predictedBytes", sounds.PredictedTotalBytes },
                        { "pending", sounds.PendingCount }, { "saving", sounds.PendingSavingBytes }, { "canPredict", sounds.CanPredictSizes },
                    } },
                { "categories", overview.HasMeasurement
                    ? overview.Categories().Select(c => (object)new Dictionary<string, object> { { "name", c.Name }, { "bytes", c.Bytes }, { "count", c.Count }, { "estimated", c.Estimated } }).ToList()
                    : new List<object>() },
            };
            state["textureRows"] = textures.Entries.Select(entry =>
            {
                var output = entry.OutputSizeFor(entry.LunaRule.maxWidth, entry.LunaRule.maxHeight);
                var predicted = entry.HasPrediction ? entry.Prediction.PlayableBytes : -1;
                int formatSelected, sizeSelected;
                var formats = textures.FormatChoices(entry, out formatSelected);
                var sizes = textures.SizeLabels(entry, out sizeSelected);
                return (object)new Dictionary<string, object>
                {
                    { "name", entry.FileName }, { "path", entry.AssetPath }, { "file", entry.FileBytes },
                    { "size", entry.Width + "×" + entry.Height }, { "output", entry.LunaOutputFormat },
                    { "quality", entry.LunaOutputFormat != "png" ? "q" + entry.LunaRule.quality : string.Empty },
                    { "outSize", output.x + "×" + output.y }, { "predicted", predicted },
                    { "failed", entry.Prediction != null && !entry.Prediction.success ? entry.Prediction.error : null },
                    { "delta", entry.HasPendingChange && entry.PendingFromBytes >= 0 && predicted >= 0 ? predicted - entry.PendingFromBytes : 0 },
                    { "lastBuild", entry.HasLastBuild ? entry.LastBuildBytes : -1 }, { "lastBuildOld", entry.LastBuildOutdated },
                    { "pending", entry.HasPendingChange }, { "compressedImport", entry.UnityCompressionAffectsLuna },
                    { "unityCompression", entry.UnityCompression + (entry.HasPlatformOverride ? " (platform)" : string.Empty) },
                    { "overrideRule", entry.IsLunaOverride }, { "excluding", ExclusionQueue.IsQueued(entry.AssetPath) },
                    { "formats", formats }, { "format", formatSelected }, { "sizes", sizes }, { "sizeIndex", sizeSelected },
                };
            }).ToList();
            state["soundRows"] = sounds.Entries.Select(entry =>
            {
                var predicted = entry.HasPrediction ? entry.PlayableBytes : -1;
                int bitrateSelected, channelSelected;
                var bitrates = sounds.BitrateLabels(entry, out bitrateSelected);
                var channels = sounds.ChannelLabels(entry, out channelSelected);
                return (object)new Dictionary<string, object>
                {
                    { "name", entry.FileName }, { "path", entry.AssetPath }, { "file", entry.FileBytes }, { "ext", entry.Extension.TrimStart('.') },
                    { "format", LunaSoundFormat.Label(entry.Format) }, { "custom", entry.Format == SoundFormat.Custom },
                    { "bitrate", entry.LunaRule.bitrate }, { "bitrates", bitrates }, { "bitrateIndex", bitrateSelected },
                    { "channels", channels[channelSelected] }, { "channelChoices", channels },
                    { "length", entry.LengthSeconds }, { "trimStart", SoundTrim.Start(entry) }, { "trimEnd", SoundTrim.End(entry) },
                    { "predicted", predicted }, { "failed", entry.Prediction != null && !entry.Prediction.success ? entry.Prediction.error : null },
                    { "delta", entry.HasPendingChange && entry.PendingFromBytes >= 0 && predicted >= 0 ? predicted - entry.PendingFromBytes : 0 },
                    { "lastBuild", entry.HasLastBuild ? entry.LastBuildBytes : -1 }, { "pending", entry.HasPendingChange },
                    { "excluding", ExclusionQueue.IsQueued(entry.AssetPath) }, { "decodedImport", !entry.UsesSourceFile },
                };
            }).ToList();
            return state;
        }

        static Dictionary<string, object> TextureDetail(LunaOptimizerWindow hub, string path)
        {
            var textures = hub.TexturesView;
            var entry = textures.Find(path);
            if (entry == null)
                return null;
            var prediction = entry.Prediction;
            var output = entry.OutputSizeFor(entry.LunaRule.maxWidth, entry.LunaRule.maxHeight);
            return new Dictionary<string, object>
            {
                { "path", entry.AssetPath }, { "name", entry.FileName },
                { "source", entry.Width + "×" + entry.Height + " · " + Bytes(entry.FileBytes) },
                { "unityImport", entry.UnityCompression + (entry.HasPlatformOverride ? " (platform)" : string.Empty) },
                { "compressedImport", entry.UnityCompressionAffectsLuna },
                { "hasAlpha", entry.HasAlpha },
                { "lossy", entry.LunaOutputFormat != "png" }, { "quality", entry.LunaRule.quality },
                { "lunaOutput", prediction != null && prediction.success ? prediction.outputExtension.TrimStart('.') + " · " + output.x + "×" + output.y + " · " + Bytes(prediction.outputBytes) + " file"
                    : prediction == null ? "not predicted yet" : "prediction failed: " + prediction.error },
                { "lunaInput", prediction != null && prediction.success ? (prediction.usedSourceFile ? "your source file" : "Unity's imported texture") + " · " + Bytes(prediction.inputBytes) : null },
                { "savedBytes", entry.HasPendingChange ? entry.PendingFromBytes : -1 },
                { "lastBuild", entry.HasLastBuild ? Bytes(entry.LastBuildBytes) + (entry.LastBuildOutdated ? " (texture changed since)" : string.Empty) : "–" },
                { "usedBy", UsedBy(textures.Settings, entry.AssetPath) },
            };
        }

        static Dictionary<string, object> SoundDetail(LunaOptimizerWindow hub, string path)
        {
            var sounds = hub.SoundsView;
            var entry = sounds.Find(path);
            if (entry == null)
                return null;
            var prediction = entry.Prediction;
            return new Dictionary<string, object>
            {
                { "path", entry.AssetPath }, { "name", entry.FileName },
                { "source", entry.Extension.TrimStart('.') + " · " + Bytes(entry.FileBytes) + " · " + entry.LengthSeconds.ToString("0.00") + " s" },
                { "lunaInput", (entry.Channels == 1 ? "mono" : entry.Channels == 2 ? "stereo" : entry.Channels + " ch") + " " + (entry.Frequency / 1000f).ToString("0.#") + " kHz"
                    + (entry.UsesSourceFile ? " (your .wav, copied unchanged)" : " (Unity's decoded import: import settings apply)") },
                { "lunaOutput", prediction != null && prediction.success ? (prediction.outputExtension ?? ".mp3").TrimStart('.') + " · " + Bytes(prediction.outputBytes) + " file"
                    : prediction == null ? "not predicted yet" : "prediction failed: " + prediction.error },
                { "opus", LunaSoundFormat.IsOpus(entry.Format) }, { "custom", entry.Format == SoundFormat.Custom },
                { "canRender", sounds.CanRenderAudio },
                { "lastBuild", entry.HasLastBuild ? Bytes(entry.LastBuildBytes) + (entry.LastBuildOutdated ? " (sound changed since)" : string.Empty) : "–" },
                { "usedBy", UsedBy(sounds.Settings, entry.AssetPath) },
            };
        }

        static Dictionary<string, object> Assets(LunaOptimizerWindow hub)
        {
            var rows = hub.AssetsViewAttached.BrowserRows();
            return new Dictionary<string, object>
            {
                { "rows", rows },
                { "kinds", rows.Select(r => (string)r["kind"]).Distinct().OrderBy(k => k).ToList() },
            };
        }

        static Dictionary<string, object> AssetDetail(LunaOptimizerWindow hub, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            var full = Path.Combine(ProjectRoot, path);
            var dependencies = AssetDatabase.GetDependencies(path, false).Where(d => d != path && !d.EndsWith(".cs", StringComparison.Ordinal)).ToList();
            var playable = LunaOptimizerWindow.SizeOf(path);
            return new Dictionary<string, object>
            {
                { "path", path }, { "name", Path.GetFileName(path) },
                { "file", File.Exists(full) ? new FileInfo(full).Length : 0 },
                { "playable", playable },
                { "uses", dependencies },
                { "usedBy", UsedBy(hub.AssetsViewAttached.Settings, path) },
            };
        }

        static Dictionary<string, object> Exclusions(LunaOptimizerWindow hub)
        {
            var settings = LunaSettings.Load(ProjectRoot);
            if (settings == null)
                return new Dictionary<string, object> { { "missing", true } };
            var pending = ExclusionQueue.Paths.ToList();
            var excluded = settings.Excludes.Where(e => e.StartsWith("Assets/", StringComparison.Ordinal) && !e.Contains("*")).OrderBy(e => e, StringComparer.Ordinal).ToList();
            var key = string.Join("|", pending) + "#" + string.Join("|", excluded) + "#" + File.GetLastWriteTimeUtc(settings.FilePath).Ticks;
            if (key != exclusionsKey)
            {
                exclusionsKey = key;
                pendingResult = pending.Count > 0 ? BlastRadius.Analyze(pending, settings) : null;
                excludedResult = excluded.Count > 0 ? BlastRadius.Analyze(excluded.Select(e => e.TrimEnd('/')).ToList(), settings, excluded) : null;
            }
            return new Dictionary<string, object>
            {
                { "pending", pending.Select(p => ExclusionItem(p, pendingResult, true)).ToList() },
                { "excluded", excluded.Select(e => ExclusionItem(e.TrimEnd('/'), excludedResult, false)).ToList() },
                { "blockers", pendingResult != null ? pendingResult.Blockers : new List<string>() },
            };
        }

        static Dictionary<string, object> ExclusionItem(string item, BlastRadius result, bool isPending)
        {
            var isFolder = AssetDatabase.IsValidFolder(item);
            var targets = result == null ? new List<BlastRadius.Target>()
                : result.Targets.Where(t => t.AssetPath == item || (isFolder && t.AssetPath.StartsWith(item + "/", StringComparison.Ordinal))).ToList();
            return new Dictionary<string, object>
            {
                { "path", item }, { "folder", isFolder },
                { "exists", isFolder || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(item)) },
                { "notShipped", result != null && isPending && result.NotShipped.Contains(item) },
                { "size", targets.Sum(t => LunaOptimizerWindow.SizeOf(t.AssetPath)) },
                { "inUse", targets.Count(t => t.Users.Count > 0) },
                { "targets", targets.Select(TargetData).ToList() },
            };
        }

        static List<object> History()
        {
            var list = new List<object>();
            foreach (LunaSettingsSection section in Enum.GetValues(typeof(LunaSettingsSection)))
            {
                var records = LunaSaveHistory.For(section, ProjectRoot);
                for (int i = 0; i < records.Count; i++)
                    list.Add(new Dictionary<string, object>
                    {
                        { "section", section.ToString() }, { "sectionName", LunaSaveHistory.SectionName(section) }, { "index", i },
                        { "savedAt", records[i].SavedAt.ToString("MM-dd HH:mm:ss") }, { "label", records[i].label }, { "detail", records[i].detail },
                    });
            }
            return list;
        }

        // ---------------------------------------------------------------- Used by

        static Dictionary<string, object> UsedBy(LunaSettings settings, string path)
        {
            if (settings == null)
                return null;
            var stamp = settings.FilePath + File.GetLastWriteTimeUtc(settings.FilePath).Ticks;
            if (stamp != usedByStamp)
            {
                usedBy.Clear();
                usedByStamp = stamp;
            }
            BlastRadius result;
            if (!usedBy.TryGetValue(path, out result))
            {
                result = BlastRadius.Analyze(new[] { path }, settings);
                usedBy[path] = result;
            }
            return result != null && result.Targets.Count > 0 ? TargetData(result.Targets[0]) : null;
        }

        static Dictionary<string, object> TargetData(BlastRadius.Target target)
        {
            return new Dictionary<string, object>
            {
                { "path", target.AssetPath },
                { "consequence", target.Users.Count == 0 ? null : target.Consequence },
                { "users", target.Users.Select(LinkData).ToList() },
            };
        }

        static Dictionary<string, object> LinkData(BlastRadius.Link link)
        {
            return new Dictionary<string, object>
            {
                { "path", link.AssetPath }, { "where", link.Where }, { "root", link.RootKind }, { "elsewhere", link.ShownElsewhere },
                { "users", link.Users.Select(LinkData).ToList() },
            };
        }

        // ---------------------------------------------------------------- files and images

        static Response TextureImage(LunaOptimizerWindow hub, string path, string kind)
        {
            var textures = hub.TexturesView;
            var entry = textures.Find(path);
            if (entry == null)
                return Text(404, "Not listed");
            string input, output, error;
            if (!textures.RenderPreview(entry, out input, out output, out error))
                return Text(500, error ?? "Preview failed");
            var file = kind == "output" ? output : input;
            return File.Exists(file) ? new Response { ContentType = "image/png", FilePath = file } : Text(404, "No image");
        }

        static Response Audio(LunaOptimizerWindow hub, string path, string kind)
        {
            var sounds = hub.SoundsView;
            var entry = sounds.Find(path);
            if (entry == null)
                return Text(404, "Not listed");
            string file;
            if (kind == "luna")
            {
                string error;
                file = sounds.LunaAudioPath(entry, out error);
                if (file == null)
                    return Text(500, error);
            }
            else
            {
                file = Path.Combine(ProjectRoot, entry.AssetPath);
            }
            return new Response { ContentType = AudioType(file), FilePath = file };
        }

        static string AudioType(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".wav": return "audio/wav";
                case ".mp3": return "audio/mpeg";
                case ".ogg": return "audio/ogg";
                case ".webm": return "audio/webm";
                case ".aif":
                case ".aiff": return "audio/aiff";
                default: return "application/octet-stream";
            }
        }

        // Small PNG of a texture, scaled on the GPU (works for compressed and unreadable textures).
        static byte[] Thumbnail(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return null;
            var key = assetPath + "|" + AssetSizeMemory.FileTicks(ProjectRoot, assetPath);
            byte[] png;
            if (thumbnails.TryGetValue(key, out png))
                return png;
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            png = texture != null ? Encode(texture, THUMB_SIZE) : null;
            thumbnails[key] = png;
            return png;
        }

        // Unity's asset preview (models, prefabs, materials); 202 while Unity is still rendering it.
        static Response AssetPreviewImage(string assetPath)
        {
            var asset = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
                return Text(404, "Not found");
            var preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null && AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                return Text(202, "Rendering");
            Texture image = preview != null ? (Texture)preview : AssetPreview.GetMiniThumbnail(asset);
            var png = image != null ? Encode(image, ASSET_PREVIEW_SIZE) : null;
            return Png(png);
        }

        static byte[] Encode(Texture texture, int maxSize)
        {
            var scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(texture.width, texture.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                var png = ImageConversion.EncodeToPNG(copy);
                UnityEngine.Object.DestroyImmediate(copy);
                return png;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // ---------------------------------------------------------------- POST /api/action

        public static Dictionary<string, object> Run(ActionRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.name))
                return Result(false, "No action.");
            var hub = LunaOptimizerWindow.Hub();
            var textures = hub.TexturesView;
            var sounds = hub.SoundsView;
            var overview = hub.OverviewView;
            var paths = request.paths ?? new string[0];
            string error;
            switch (request.name)
            {
                // Size
                case "measure":
                    if (!overview.HasLunaInstall)
                        return Result(false, "No Luna install found among this project's packages.");
                    overview.MeasureNow();
                    return Result(true, "Measuring… the first run compiles your scripts (~30-40 s).");
                case "cancel-measure":
                    overview.CancelMeasure();
                    return Result(true, "Measure canceled.");

                // Simple actions
                case "set-preset":
                    SimpleModeView.ChosenPreset = request.a;
                    return Result(true, null);
                case "shrink":
                {
                    int webp, jpeg;
                    string hint;
                    SimpleModeView.PresetQualities(SimpleModeView.ChosenPreset, out webp, out jpeg, out hint);
                    var changed = textures.PickSmallestAll(webp, jpeg);
                    return Result(true, changed == 0 ? "Every texture already uses its smallest format." : changed + " texture(s) changed: review, then Save to Luna.");
                }
                case "predict-textures":
                    textures.PredictAll();
                    return Result(true, "Texture sizes predicted.");
                case "predict-sounds":
                    sounds.PredictAll();
                    return Result(true, "Sound sizes predicted.");
                case "trim-silence":
                {
                    var changed = paths.Length > 0 ? sounds.TrimSilence(paths) : sounds.TrimSilenceAll();
                    return Result(true, changed == 0 ? "No silence to trim." : changed + " sound(s) trimmed: review, then Save to Luna.");
                }
                case "mono":
                {
                    var changed = sounds.MonoAll();
                    return Result(true, changed == 0 ? "Every sound is already mono." : changed + " sound(s) set to mono: review, then Save to Luna.");
                }
                case "save":
                {
                    var before = hub.PendingTotal;
                    hub.SaveAllPending();
                    return hub.PendingTotal == 0
                        ? Result(true, before + " change(s) saved to Luna. Each one can be undone (Undo).")
                        : Result(false, "Not everything was saved; see the dialog in Unity.");
                }
                case "discard":
                    hub.DiscardAllPending();
                    return Result(true, "Unsaved changes discarded.");

                // Textures
                case "tex-format":
                    return WithTexture(textures, request.path, entry => textures.PickFormat(entry, request.value));
                case "tex-size":
                    return WithTexture(textures, request.path, entry => textures.PickSize(entry, request.value));
                case "tex-quality":
                    return WithTexture(textures, request.path, entry => textures.SetQuality(entry, request.a));
                case "tex-bulk":
                {
                    var note = textures.ApplyBulk(paths, request.a, request.b);
                    return Result(true, note ?? paths.Length + " texture(s) changed: review, then Save to Luna.");
                }
                case "tex-smallest":
                {
                    var changed = textures.PickSmallest(paths);
                    return Result(true, changed == 0 ? "These textures already use their smallest format." : changed + " format change(s) to review.");
                }
                case "tex-qualities":
                    textures.SetLossyQualities(request.a, request.b);
                    return Result(true, null);
                case "tex-optimize-sources":
                    textures.OptimizeSourcesInUnity();
                    return Result(true, "Done in Unity.");
                case "tex-restore":
                    textures.RestoreOriginalsInUnity();
                    return Result(true, "Done in Unity.");
                case "rescan-textures":
                    textures.Rescan();
                    return Result(true, "Textures scanned again.");

                // Sounds
                case "snd-format":
                {
                    var entry = sounds.Find(request.path);
                    if (entry == null)
                        return Result(false, "That sound is no longer listed.");
                    var outcome = sounds.PickFormat(entry, request.value, request.confirmed);
                    if (outcome == SoundOptimizerView.NEEDS_CONFIRM)
                        return Confirm("Use " + request.value + " for " + entry.FileName + "?\n\n" + LunaSoundFormat.RISKS);
                    return outcome == null ? Result(true, null) : Result(false, outcome);
                }
                case "snd-bitrate":
                    return WithSound(sounds, request.path, entry => sounds.PickBitrate(entry, request.value));
                case "snd-channels":
                    return WithSound(sounds, request.path, entry => sounds.PickChannels(entry, request.value));
                case "snd-trim":
                    return WithSound(sounds, request.path, entry => sounds.SetTrimSeconds(entry, request.start, request.end));
                case "snd-bulk":
                {
                    var outcome = sounds.ApplyBulk(paths, request.a, request.b, request.c, request.confirmed);
                    if (outcome == SoundOptimizerView.NEEDS_CONFIRM)
                        return Confirm("Switch " + paths.Length + " sound(s) to Opus?\n\n" + LunaSoundFormat.RISKS);
                    return Result(true, outcome ?? paths.Length + " sound(s) changed: review, then Save to Luna.");
                }
                case "snd-repair-stale":
                    return sounds.RepairStale(out error) ? Result(true, "Stale sound overrides fixed.") : Result(false, "Nothing was changed. " + error);
                case "rescan-sounds":
                    sounds.Rescan();
                    return Result(true, "Sounds scanned again.");

                // Overview
                case "ov-network":
                    overview.SetNetwork(request.value);
                    return Result(true, "Network set. Press Measure for its numbers.");
                case "ov-auto":
                    overview.SetAuto(request.flag);
                    return Result(true, null);
                case "ov-speedup":
                    overview.SetSpeedUp(request.flag);
                    return Result(true, null);
                case "ov-module":
                    overview.PlanModule(request.value, request.flag);
                    return Result(true, null);
                case "ov-reset-plan":
                    overview.ResetPlan();
                    return Result(true, null);
                case "ov-whatif":
                    overview.MeasureWhatIf();
                    return Result(true, "Measuring the what-if…");
                case "ov-apply-plan":
                    if (!request.confirmed)
                        return Confirm(overview.PlanConfirmationText);
                    overview.ApplyPlanConfirmed();
                    return Result(true, "Engine settings saved. Numbers update after the next Luna build and Measure.");
                case "ov-reinclude":
                    return overview.ReInclude(request.path, out error)
                        ? Result(true, "Re-included. Numbers update after the next Luna build and Measure.")
                        : Result(false, "Nothing was changed. " + error);
                case "ov-locate":
                    return overview.Locate(request.path, request.value, request.value2) ? Result(true, "Selected in Unity's Project window.") : Result(false, "Not a project file (built-in or generated).");

                // Models & Other
                case "rescan-assets":
                    hub.AssetsViewAttached.Rescan();
                    return Result(true, "Assets scanned again.");

                // Exclusions
                case "excl-add":
                    ExclusionQueue.Add(paths.Length > 0 ? paths : new[] { request.path });
                    LunaExclusionWindow.ReloadOpen();
                    return Result(true, "Added to pending exclusions. Review and apply them in Exclusions.");
                case "excl-remove":
                    ExclusionQueue.Remove(request.path);
                    LunaExclusionWindow.ReloadOpen();
                    return Result(true, null);
                case "excl-clear":
                    ExclusionQueue.Clear();
                    LunaExclusionWindow.ReloadOpen();
                    return Result(true, null);
                case "excl-apply":
                {
                    var settings = LunaSettings.Load(ProjectRoot);
                    var pending = ExclusionQueue.Paths.ToList();
                    if (settings == null || pending.Count == 0)
                        return Result(false, "Nothing pending.");
                    var result = BlastRadius.Analyze(pending, settings);
                    if (result.Blockers.Count > 0)
                        return Result(false, string.Join("\n", result.Blockers));
                    var count = LunaExclusionWindow.ApplyPending(result, ProjectRoot, out error);
                    if (error != null)
                        return Result(false, "Nothing was changed. " + error);
                    LunaExclusionWindow.ReloadOpen();
                    LunaOptimizerWindow.NotifyLunaSettingsChanged();
                    return Result(true, count + " exclusion(s) saved. Sizes update after the next Luna build.");
                }
                case "excl-reinclude":
                {
                    var settings = LunaSettings.Load(ProjectRoot);
                    var entry = settings != null ? settings.Excludes.FirstOrDefault(e => e.TrimEnd('/') == request.path) : null;
                    if (entry == null)
                        return Result(false, "It is no longer excluded.");
                    if (!LunaSettingsWriter.SaveAssetExcludes(new List<string>(), new List<string> { entry }, "Re-include: " + Path.GetFileName(request.path), ProjectRoot, out error))
                        return Result(false, "Nothing was changed. " + error);
                    LunaExclusionWindow.ReloadOpen();
                    LunaOptimizerWindow.NotifyLunaSettingsChanged();
                    return Result(true, "Re-included. Sizes update after the next Luna build.");
                }

                // Header
                case "scene":
                {
                    var outcome = hub.ChangeBuildSceneTo(request.path);
                    return outcome == null ? Result(true, "Luna now builds " + Path.GetFileNameWithoutExtension(request.path) + ".") : Result(false, outcome);
                }
                case "undo":
                {
                    LunaSettingsSection section;
                    if (!Enum.TryParse(request.value, out section))
                        return Result(false, "Unknown section.");
                    if (!request.confirmed)
                    {
                        var message = LunaSaveHistory.RevertMessage(section, request.a, ProjectRoot, out error);
                        return message != null ? Confirm(message) : Result(false, "Nothing was changed. " + error);
                    }
                    if (!LunaSaveHistory.Revert(section, request.a, ProjectRoot, out error))
                        return Result(false, "Nothing was changed. " + error);
                    hub.ReloadAll();
                    LunaExclusionWindow.ReloadOpen();
                    return Result(true, "Undone. The undo is recorded too, so it can be undone.");
                }
                case "show":
                {
                    LunaOptimizerWindow.Tab tab;
                    if (!Enum.TryParse(request.value, out tab))
                        tab = LunaOptimizerWindow.Tab.Overview;
                    hub.ShowTab(tab);
                    return Result(true, "Opened in Unity.");
                }
                case "ping":
                    OptimizerUI.Ping(request.path);
                    return Result(true, "Selected in Unity's Project window.");
                default:
                    return Result(false, "Unknown action: " + request.name);
            }
        }

        static Dictionary<string, object> WithTexture(TextureOptimizerView textures, string path, Action<TextureEntry> change)
        {
            var entry = textures.Find(path);
            if (entry == null)
                return Result(false, "That texture is no longer listed.");
            change(entry);
            return Result(true, null);
        }

        static Dictionary<string, object> WithSound(SoundOptimizerView sounds, string path, Action<SoundEntry> change)
        {
            var entry = sounds.Find(path);
            if (entry == null)
                return Result(false, "That sound is no longer listed.");
            change(entry);
            return Result(true, null);
        }

        static Dictionary<string, object> Result(bool ok, string message)
        {
            return new Dictionary<string, object> { { "ok", ok }, { "message", message } };
        }

        // The page shows the text and calls again with confirmed = true.
        static Dictionary<string, object> Confirm(string text)
        {
            return new Dictionary<string, object> { { "ok", false }, { "confirm", text } };
        }

        // ---------------------------------------------------------------- helpers

        static List<string> Paths(string joined)
        {
            return string.IsNullOrEmpty(joined) ? new List<string>() : joined.Split('\n').Where(p => p.Length > 0).ToList();
        }

        static int ParseInt(string text)
        {
            int value;
            return int.TryParse(text, out value) ? value : 0;
        }

        static Response Json(object value)
        {
            return new Response { Body = System.Text.Encoding.UTF8.GetBytes(MiniJson.Serialize(value)) };
        }

        static Response Png(byte[] png)
        {
            return png != null ? new Response { ContentType = "image/png", Body = png } : Text(404, "No preview");
        }

        public static Response Text(int status, string text)
        {
            return new Response { Status = status, ContentType = "text/plain; charset=utf-8", Body = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty) };
        }

        static string Bytes(double bytes)
        {
            return OptimizerUI.Bytes(bytes);
        }
    }
}
