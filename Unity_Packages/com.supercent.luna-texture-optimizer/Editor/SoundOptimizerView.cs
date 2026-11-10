using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Sounds tab of the Luna Optimizer (UI Toolkit): every sound Luna ships in a sortable table, and a details panel
    // for the selected one (Source / Luna waveforms and playback, trim, format, bitrate, channels, what uses it).
    // Changes stay in memory and are predicted; the hub's Save all writes them as Luna per-sound overrides.
    [Serializable]
    public class SoundOptimizerView : OptimizerView
    {
        const float ROW_HEIGHT = 24f;
        const int LOAD_TIMEOUT_MS = 5000;
        const float DETAILS_WIDTH = 400f;
        const int WAVE_WIDTH = 512;
        const int WAVE_HEIGHT = 64;
        const string KEEP_FORMAT = "Format: keep";
        const string KEEP_BITRATE = "Bitrate: keep";
        const string KEEP_CHANNELS = "Channels: keep";
        const long TRIM_COMMIT_DELAY_MS = 350;   // range slider: predict once the handles rest (and the drag ended)
        static readonly Color SOURCE_WAVE = new Color(0.55f, 0.75f, 0.95f);
        static readonly Color LUNA_WAVE = new Color(0.45f, 0.85f, 0.5f);

        enum Column
        {
            Pick,
            Name,
            Source,
            Format,
            Bitrate,
            Channels,
            Trim,
            Predicted,
            LastBuild,
            Listen,
        }

        // Bitrates Luna's ffmpeg accepts at every sample rate these clips use (MPEG-1 layer III: 32-320 kbps).
        static readonly int[] BITRATES = { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160 };
        static readonly int[] CHANNEL_VALUES = { 1, 2, 0 };
        static readonly string[] CHANNEL_LABELS = { "Mono", "Stereo", "As exported" };
        static readonly SoundFormat[] FORMATS = { SoundFormat.Mp3, SoundFormat.OggOpus, SoundFormat.WebmOpus };

        List<SoundEntry> entries = new List<SoundEntry>();
        List<SoundEntry> visibleEntries = new List<SoundEntry>();
        List<string> staleOverrides = new List<string>();
        LunaSettings settings;
        LunaBuildStats stats;
        LunaInstall install;
        PredictionCache cache;
        string projectRoot;
        string searchText = string.Empty;
        HashSet<string> ticked = new HashSet<string>();   // asset paths ticked for bulk edit
        SoundFormat? bulkFormat;                          // null = keep each sound's own value
        [SerializeField] string selectedPath;             // sound shown in the details panel
        AudioClip previewClip;                            // the predicted output currently loaded for listening
        readonly Dictionary<string, BlastRadius> usedByCache = new Dictionary<string, BlastRadius>();
        readonly Dictionary<string, Texture2D> waveforms = new Dictionary<string, Texture2D>();   // file path + time -> texture

        // UI Toolkit elements, rebuilt by CreateUI after every reload.
        MultiColumnListView table;
        VisualElement statStrip;
        VisualElement bulkBar;
        VisualElement bulkEdit;
        Label bulkHint;
        SearchBar searchBar;
        double largestPredicted;
        Label summaryLabel;
        HelpBox staleBox;
        VisualElement staleRow;
        HelpBox opusBox;
        VisualElement opusRow;
        Toggle allShownToggle;
        Label tickedLabel;
        DropdownField bulkFormatField;
        DropdownField bulkBitrateField;
        DropdownField bulkChannelsField;
        Button bulkApplyButton;
        Button untickButton;
        Button bulkTrimButton;
        ToolbarButton predictButton;
        ScrollView details;
        IVisualElementScheduledItem trimCommit;

        public override string Title
        {
            get { return "Sounds"; }
        }

        public override int PendingCount
        {
            get { return entries.Count(e => e.HasPendingChange); }
        }

        public override void SavePending()
        {
            SaveRules();
        }

        public override void DiscardPending()
        {
            Refresh();
        }

        public override void OnEnable()
        {
            ExclusionQueue.Changed += OnExclusionQueueChanged;
            Refresh();
        }

        public override void OnDisable()
        {
            ExclusionQueue.Changed -= OnExclusionQueueChanged;
            StopPreview();
            ClearWaveforms();
        }

        void Refresh()
        {
            projectRoot = Path.GetDirectoryName(Application.dataPath);
            settings = LunaSettings.Load(projectRoot);
            stats = LunaBuildStats.Load(projectRoot);
            install = LunaInstall.Find();
            cache = new PredictionCache(projectRoot);
            entries = settings != null ? BuildSoundScanner.Scan(projectRoot, settings, stats) : new List<SoundEntry>();
            foreach (var entry in entries)
                entry.Prediction = cache.Get(PredictionCache.KeyFor(entry, projectRoot, install));
            staleOverrides = settings != null
                ? settings.SoundOverrides.Where(r => LunaSettings.IsStaleOverride(r.name, projectRoot)).Select(r => r.name).ToList()
                : new List<string>();
            ticked.IntersectWith(entries.Select(e => e.AssetPath));
            usedByCache.Clear();
            ClearWaveforms();
            ApplySortAndFilter();
        }

        bool CanPredict
        {
            get { return install != null && !string.IsNullOrEmpty(install.FfmpegPath); }
        }

        // Runs Luna's sound step locally for every sound without an up-to-date prediction.
        void PredictMissing()
        {
            Predict(entries.Where(e => e.Prediction == null).ToList(), "Predicting Luna output");
            ApplySortAndFilter();
        }

        void Predict(List<SoundEntry> targets, string progressTitle)
        {
            var predictions = LunaSoundPipeline.PredictMany(targets, install, projectRoot, cache, progressTitle);
            for (int i = 0; i < targets.Count; i++)
            {
                if (predictions[i] == null)
                    continue;
                targets[i].Prediction = predictions[i];
                cache.Set(PredictionCache.KeyFor(targets[i], projectRoot, install), predictions[i]);
            }
            cache.Save();
        }

        // A stale override follows its file when exactly one shipped sound has the same file name and no override
        // of its own; any other stale override is removed. Asks first.
        void RepairStaleOverrides()
        {
            Dictionary<string, string> moves;
            List<string> removals;
            PlanStaleRepair(out moves, out removals);
            var lines = moves.Select(m => "Move  " + m.Key + "\n   → " + m.Value)
                .Concat(removals.Select(r => "Remove  " + r)).ToList();
            if (!EditorUtility.DisplayDialog("Fix stale sound overrides",
                    string.Join("\n", lines) + "\n\nMoved overrides keep their bitrate and channels. Write these changes to "
                    + LunaSettings.FILE_NAME + "?", "Fix", "Cancel"))
                return;
            string error;
            if (!ApplyStaleRepair(moves, removals, out error))
                EditorUtility.DisplayDialog("Fix stale sound overrides", "Nothing was changed.\n\n" + error, "OK");
        }

        void PlanStaleRepair(out Dictionary<string, string> moves, out List<string> removals)
        {
            moves = new Dictionary<string, string>();
            removals = new List<string>();
            foreach (var name in staleOverrides)
            {
                var fileName = Path.GetFileName(name);
                var matches = entries.Where(e => e.FileName == fileName).ToList();
                if (matches.Count == 1 && !settings.SoundOverrides.Any(r => r.name == matches[0].AssetPath))
                    moves[name] = matches[0].AssetPath;
                else
                    removals.Add(name);
            }
        }

        bool ApplyStaleRepair(Dictionary<string, string> moves, List<string> removals, out string error)
        {
            if (!LunaSettingsWriter.RepairSoundOverrides(moves, removals, projectRoot, out error))
                return false;
            Refresh();
            return true;
        }

        static string PickOpusFfmpeg()
        {
            var path = EditorUtility.OpenFilePanel("ffmpeg with libopus", "/opt/homebrew/bin", string.Empty);
            if (string.IsNullOrEmpty(path))
                return null;
            string error;
            if (!LunaSoundFormat.SetOpusFfmpeg(path, out error))
            {
                EditorUtility.DisplayDialog("Opus encoder", error, "OK");
                return null;
            }
            return path;
        }

        // Opus is offered, not recommended: the user confirms the risks each time a sound switches to it.
        void ChangeFormat(SoundEntry entry, SoundFormat format)
        {
            string ffmpeg;
            if (!ConfirmFormat(format, entry.FileName, out ffmpeg))
                return;
            ChangeRule(entry, rule => ApplyFormat(entry, rule, format, ffmpeg));
        }

        // Finds the Opus encoder and shows the risks; true for MP3 without asking.
        bool ConfirmFormat(SoundFormat format, string what, out string ffmpeg)
        {
            ffmpeg = null;
            if (!LunaSoundFormat.IsOpus(format))
                return true;
            ffmpeg = LunaSoundFormat.FindOpusFfmpeg();
            if (ffmpeg == null)
            {
                if (!EditorUtility.DisplayDialog("Opus encoder", "No ffmpeg with the libopus encoder was found (Luna's own ffmpeg has none). "
                        + "Install one (for example: brew install ffmpeg) or pick it.", "Pick ffmpeg…", "Cancel"))
                    return false;
                ffmpeg = PickOpusFfmpeg();
                if (ffmpeg == null)
                    return false;
            }
            return EditorUtility.DisplayDialog("Use " + LunaSoundFormat.Label(format) + " for " + what + "?",
                LunaSoundFormat.RISKS + "\n\nEncoder: " + ffmpeg, "Use " + LunaSoundFormat.Label(format), "Cancel");
        }

        // Back to MP3 restores the saved MP3 bitrate (or Luna's default); Opus starts at its default bitrate.
        void ApplyFormat(SoundEntry entry, LunaSoundRule rule, SoundFormat format, string ffmpeg)
        {
            var saved = entry.SavedRule ?? entry.LunaRule;
            var mp3Bitrate = LunaSoundFormat.Detect(saved) == SoundFormat.Mp3 ? saved.bitrate : settings.DefaultSoundRule.bitrate;
            LunaSoundFormat.Apply(rule, format, ffmpeg, mp3Bitrate);
        }

        // Keeps the change in memory and predicts it; nothing is written until Save to Luna.
        void ChangeRule(SoundEntry entry, Action<LunaSoundRule> change)
        {
            ChangeRules(new List<SoundEntry> { entry }, (e, rule) => change(rule));
        }

        void ChangeRules(IList<SoundEntry> targets, Action<SoundEntry, LunaSoundRule> change)
        {
            foreach (var entry in targets)
            {
                var saved = entry.SavedRule ?? entry.LunaRule;
                var rule = entry.LunaRule.Clone();
                change(entry, rule);
                LunaSoundFormat.RefreshScript(rule);
                if (rule.bitrate == saved.bitrate && rule.channels == saved.channels
                    && (rule.script ?? string.Empty) == (saved.script ?? string.Empty) && (rule.ext ?? string.Empty) == (saved.ext ?? string.Empty)
                    && SameTrim(rule, saved))
                {
                    entry.LunaRule = saved;
                    entry.SavedRule = null;
                }
                else
                {
                    entry.SavedRule = saved;
                    entry.LunaRule = rule;
                }
                entry.Prediction = cache.Get(PredictionCache.KeyFor(entry, projectRoot, install));
            }

            var missing = targets.Where(e => e.Prediction == null).ToList();
            if (missing.Count > 0 && CanPredict)
                Predict(missing, missing.Count > 1 ? "Predicting Luna output" : null);
            foreach (var entry in targets)
                entry.PendingFromBytes = entry.HasPendingChange ? PlayableBytesFor(entry, entry.SavedRule) : -1;
            cache.Save();
            ApplySortAndFilter();
        }

        static bool SameTrim(LunaSoundRule a, LunaSoundRule b)
        {
            var aWhole = string.IsNullOrEmpty(a.endTime) || a.endTime == LunaSoundPipeline.NO_TIME;
            var bWhole = string.IsNullOrEmpty(b.endTime) || b.endTime == LunaSoundPipeline.NO_TIME;
            return aWhole && bWhole || !aWhole && !bWhole && a.startTime == b.startTime && a.endTime == b.endTime;
        }

        void SetTrim(SoundEntry entry, float start, float end)
        {
            if (entry.Format == SoundFormat.Custom)
                return;
            ChangeRule(entry, rule => SoundTrim.Apply(rule, start, end, entry.LengthSeconds));
        }

        // Leaves out leading and trailing silence, inside any trim already set (never makes a sound longer).
        // Returns how many sounds changed.
        int TrimSilence(IList<SoundEntry> targets)
        {
            var ranges = new Dictionary<SoundEntry, Vector2>();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var entry = targets[i];
                    if (targets.Count > 1 && EditorUtility.DisplayCancelableProgressBar("Trim silence", entry.FileName, (float)i / targets.Count))
                        break;
                    if (entry.Format == SoundFormat.Custom)
                        continue;
                    var clip = LoadClip(Path.GetFullPath(entry.AssetPath));
                    float start;
                    float end;
                    var found = SoundTrim.FindSound(clip, out start, out end);
                    if (clip != null)
                        UnityEngine.Object.DestroyImmediate(clip);
                    if (!found)
                        continue;
                    start = Mathf.Max(start, SoundTrim.Start(entry));
                    end = Mathf.Min(end, SoundTrim.End(entry));
                    if (start - SoundTrim.Start(entry) + SoundTrim.End(entry) - end >= SoundTrim.MIN_LENGTH)
                        ranges[entry] = new Vector2(start, end);
                }
            }
            finally
            {
                if (targets.Count > 1)
                    EditorUtility.ClearProgressBar();
            }
            if (ranges.Count > 0)
                ChangeRules(ranges.Keys.ToList(), (entry, rule) => SoundTrim.Apply(rule, ranges[entry].x, ranges[entry].y, entry.LengthSeconds));
            return ranges.Count;
        }

        // ---------------------------------------------------------------- for the browser page (LunaOptimizerWebApi)

        public const string NEEDS_CONFIRM = "confirm";

        public LunaSettings Settings
        {
            get { return settings; }
        }

        public bool CanRenderAudio
        {
            get { return CanPredict; }
        }

        public SoundEntry Find(string assetPath)
        {
            return entries.FirstOrDefault(e => e.AssetPath == assetPath);
        }

        public static List<string> FormatLabels()
        {
            return FORMATS.Select(LunaSoundFormat.Label).ToList();
        }

        // Null when done; NEEDS_CONFIRM when Opus risks must be shown first; else what went wrong.
        public string PickFormat(SoundEntry entry, string label, bool confirmed)
        {
            if (entry.Format == SoundFormat.Custom)
                return "This sound uses a custom Luna script; it is left as it is.";
            var index = FormatLabels().IndexOf(label);
            if (index < 0 || FORMATS[index] == entry.Format)
                return null;
            var format = FORMATS[index];
            string ffmpeg = null;
            if (LunaSoundFormat.IsOpus(format))
            {
                if (!confirmed)
                    return NEEDS_CONFIRM;
                ffmpeg = LunaSoundFormat.FindOpusFfmpeg();
                if (ffmpeg == null)
                    return "No ffmpeg with the libopus encoder was found (Luna's own ffmpeg has none). Install one (brew install ffmpeg) or pick it in Unity: Sounds tab › Encoder….";
            }
            ChangeRule(entry, rule => ApplyFormat(entry, rule, format, ffmpeg));
            return null;
        }

        public List<string> BitrateLabels(SoundEntry entry, out int selected)
        {
            var values = BitratesFor(entry.Format).ToList();
            var current = entry.LunaRule.bitrate;
            if (!values.Contains(current))
                values.Insert(0, current);
            selected = values.IndexOf(current);
            return values.Select(v => v > 0 ? v + " kbps" : "VBR (q8)").ToList();
        }

        public void PickBitrate(SoundEntry entry, string label)
        {
            var bitrate = ParseBitrate(label);
            if (entry.Format != SoundFormat.Custom && bitrate.HasValue && bitrate.Value != entry.LunaRule.bitrate)
                ChangeRule(entry, rule => rule.bitrate = bitrate.Value);
        }

        public List<string> ChannelLabels(SoundEntry entry, out int selected)
        {
            var labels = CHANNEL_LABELS.ToList();
            selected = Array.IndexOf(CHANNEL_VALUES, entry.LunaRule.channels);
            if (selected < 0)
            {
                labels.Add(entry.LunaRule.channels + " ch");
                selected = labels.Count - 1;
            }
            return labels;
        }

        public void PickChannels(SoundEntry entry, string label)
        {
            var index = Array.IndexOf(CHANNEL_LABELS, label);
            if (entry.Format != SoundFormat.Custom && index >= 0 && CHANNEL_VALUES[index] != entry.LunaRule.channels)
                ChangeRule(entry, rule => rule.channels = CHANNEL_VALUES[index]);
        }

        public void SetTrimSeconds(SoundEntry entry, float start, float end)
        {
            SetTrim(entry, start, end);
        }

        public int TrimSilence(IEnumerable<string> paths)
        {
            return TrimSilence(paths.Select(Find).Where(e => e != null).ToList());
        }

        public static List<string> BulkChannelLabels()
        {
            return new[] { KEEP_CHANNELS }.Concat(CHANNEL_LABELS).ToList();
        }

        public List<int> BulkBitrateValues(IEnumerable<string> paths, int formatIndex)
        {
            var saved = bulkFormat;
            bulkFormat = formatIndex > 0 ? FORMATS[formatIndex - 1] : (SoundFormat?)null;
            try
            {
                return BulkBitrates(paths.Select(Find).Where(e => e != null).ToList());
            }
            finally
            {
                bulkFormat = saved;
            }
        }

        // formatIndex / channelIndex: into FormatLabels / CHANNEL_LABELS plus 1 (0 = keep); bitrate 0 = keep.
        public string ApplyBulk(IEnumerable<string> paths, int formatIndex, int bitrate, int channelIndex, bool confirmed)
        {
            var targets = paths.Select(Find).Where(e => e != null).ToList();
            SoundFormat? format = formatIndex > 0 ? FORMATS[formatIndex - 1] : (SoundFormat?)null;
            string ffmpeg = null;
            if (format.HasValue && LunaSoundFormat.IsOpus(format.Value) && targets.Any(e => e.Format != SoundFormat.Custom && e.Format != format.Value))
            {
                if (!confirmed)
                    return NEEDS_CONFIRM;
                ffmpeg = LunaSoundFormat.FindOpusFfmpeg();
                if (ffmpeg == null)
                    return "No ffmpeg with the libopus encoder was found. Install one (brew install ffmpeg) or pick it in Unity: Sounds tab › Encoder….";
            }
            return ApplyBulkCore(targets, format, bitrate > 0 ? bitrate : (int?)null, channelIndex > 0 ? CHANNEL_VALUES[channelIndex - 1] : (int?)null, ffmpeg);
        }

        // The file the browser plays as "Luna": Luna's output for the current rule (decoded to .wav for Opus), made on demand.
        public string LunaAudioPath(SoundEntry entry, out string error)
        {
            error = null;
            if (!CanPredict)
            {
                error = "No Luna ffmpeg found.";
                return null;
            }
            var path = LunaSoundPipeline.PreviewPathFor(entry, projectRoot, install);
            if (!File.Exists(path))
            {
                var predictions = LunaSoundPipeline.PredictMany(new[] { entry }, install, projectRoot, null, null);
                if (predictions[0] != null)
                    entry.Prediction = predictions[0];
            }
            if (!File.Exists(path))
            {
                error = "Could not make the Luna output" + (entry.Prediction != null ? ": " + entry.Prediction.error : ".");
                return null;
            }
            return path;
        }

        public IReadOnlyList<string> StaleOverrides
        {
            get { return staleOverrides; }
        }

        public string StaleRepairPlan()
        {
            Dictionary<string, string> moves;
            List<string> removals;
            PlanStaleRepair(out moves, out removals);
            return string.Join("\n", moves.Select(m => "Move " + m.Key + " → " + m.Value).Concat(removals.Select(r => "Remove " + r)));
        }

        public bool RepairStale(out string error)
        {
            Dictionary<string, string> moves;
            List<string> removals;
            PlanStaleRepair(out moves, out removals);
            return ApplyStaleRepair(moves, removals, out error);
        }

        public int OpusCount
        {
            get { return entries.Count(e => LunaSoundFormat.IsOpus(e.Format)); }
        }

        public void Rescan()
        {
            Refresh();
        }

        // ---------------------------------------------------------------- for the hub's Simple mode

        public int Count
        {
            get { return entries.Count; }
        }

        public int PredictedCount
        {
            get { return entries.Count(e => e.HasPrediction); }
        }

        public double PredictedTotalBytes
        {
            get { return entries.Where(e => e.HasPrediction).Sum(e => e.PlayableBytes); }
        }

        // Predicted bytes the unsaved changes save (negative when they add).
        public double PendingSavingBytes
        {
            get { return entries.Where(e => e.HasPendingChange && e.HasPrediction && e.PendingFromBytes >= 0).Sum(e => e.PendingFromBytes - e.PlayableBytes); }
        }

        public IReadOnlyList<SoundEntry> Entries
        {
            get { return entries; }
        }

        public bool CanPredictSizes
        {
            get { return CanPredict && entries.Any(e => e.Prediction == null); }
        }

        public void PredictAll()
        {
            if (CanPredict)
                PredictMissing();
        }

        public int TrimSilenceAll()
        {
            return TrimSilence(entries);
        }

        public int MonoAll()
        {
            var targets = entries.Where(e => e.Format != SoundFormat.Custom && e.LunaRule.channels != 1).ToList();
            if (targets.Count > 0)
                ChangeRules(targets, (entry, rule) => rule.channels = 1);
            return targets.Count;
        }

        // Bitrates offered in the bar: those of the chosen format, else every bitrate valid for at least one ticked sound's format.
        List<int> BulkBitrates(List<SoundEntry> targets)
        {
            IEnumerable<int> values;
            if (bulkFormat.HasValue)
                values = BitratesFor(bulkFormat.Value);
            else
                values = targets.Select(e => e.Format).Where(f => f != SoundFormat.Custom).Distinct().SelectMany(BitratesFor);
            return values.Distinct().OrderBy(v => v).ToList();
        }

        static int[] BitratesFor(SoundFormat format)
        {
            return LunaSoundFormat.IsOpus(format) ? LunaSoundFormat.OPUS_BITRATES : BITRATES;
        }

        void ApplyBulk(List<SoundEntry> targets, SoundFormat? format, int? bitrate, int? channels)
        {
            string ffmpeg = null;
            if (format.HasValue && targets.Any(e => e.Format != SoundFormat.Custom && e.Format != format.Value)
                && !ConfirmFormat(format.Value, targets.Count + " sound(s)", out ffmpeg))
                return;
            var note = ApplyBulkCore(targets, format, bitrate, channels, ffmpeg);
            if (note != null)
                EditorUtility.DisplayDialog("Apply to ticked sounds", note, "OK");
        }

        // ffmpeg: the Opus encoder when switching to Opus (already confirmed). Returns what was left as it was, or null.
        string ApplyBulkCore(List<SoundEntry> targets, SoundFormat? format, int? bitrate, int? channels, string ffmpeg)
        {
            var custom = targets.Where(e => e.Format == SoundFormat.Custom).ToList();
            targets = targets.Except(custom).ToList();
            var bitrateKept = new List<string>();
            ChangeRules(targets, (entry, rule) =>
            {
                if (format.HasValue && entry.Format != format.Value)
                    ApplyFormat(entry, rule, format.Value, ffmpeg);
                if (bitrate.HasValue)
                {
                    if (BitratesFor(LunaSoundFormat.Detect(rule)).Contains(bitrate.Value))
                        rule.bitrate = bitrate.Value;
                    else
                        bitrateKept.Add(entry.FileName);
                }
                if (channels.HasValue)
                    rule.channels = channels.Value;
            });

            var notes = new List<string>();
            if (custom.Count > 0)
                notes.Add("Left as they are (custom Luna script): " + LunaSaveHistory.Names(custom.Select(e => e.FileName)));
            if (bitrateKept.Count > 0)
                notes.Add(bitrate + " kbps does not exist for their format (MP3 32–160, Opus 16–128), bitrate kept: " + LunaSaveHistory.Names(bitrateKept));
            return notes.Count > 0 ? string.Join("\n\n", notes) : null;
        }

        double PlayableBytesFor(SoundEntry entry, LunaSoundRule rule)
        {
            var variant = entry.Clone();
            variant.LunaRule = rule;
            variant.Prediction = cache.Get(PredictionCache.KeyFor(variant, projectRoot, install));
            return variant.PlayableBytes;
        }

        void SaveRules()
        {
            var pending = entries.Where(e => e.HasPendingChange).ToList();
            string error;
            if (!LunaSettingsWriter.SaveSoundRules(pending, projectRoot, out error))
            {
                EditorUtility.DisplayDialog("Save to Luna", "Nothing was saved.\n\n" + error, "OK");
                return;
            }
            Refresh();
        }

        void OnExclusionQueueChanged()
        {
            if (table != null)
                table.RefreshItems();
        }

        // ---------------------------------------------------------------- UI

        public override VisualElement CreateUI()
        {
            var root = new VisualElement();
            root.AddToClassList("lo-tab-root");
            root.Add(CreateToolbar());
            statStrip = OptimizerUI.StatStrip();
            root.Add(statStrip);
            summaryLabel = new Label();
            summaryLabel.AddToClassList("lo-summary");
            summaryLabel.AddToClassList("lo-warning");
            root.Add(summaryLabel);

            staleBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            staleRow = BannerRow(staleBox, new Button(() => EditorApplication.delayCall += RepairStaleOverrides) { text = "Fix…" });
            root.Add(staleRow);
            opusBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { tooltip = LunaSoundFormat.RISKS };
            opusRow = BannerRow(opusBox, new Button(() => EditorApplication.delayCall += () => PickOpusFfmpeg())
            {
                text = "Encoder…",
                tooltip = "Pick the ffmpeg (with libopus) that new Opus rules call.",
            });
            root.Add(opusRow);
            root.Add(CreateBulkBar());
            searchBar.RegisterShortcut(root);

            var split = new TwoPaneSplitView(1, DETAILS_WIDTH, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "lo-sounds-split" };
            split.AddToClassList("lo-split");
            split.Add(CreateTable());
            details = new ScrollView();
            details.AddToClassList("lo-details");
            details.contentContainer.AddToClassList("lo-details-content");
            split.Add(details);
            root.Add(split);

            RefreshUI();
            return root;
        }

        static VisualElement BannerRow(HelpBox box, Button button)
        {
            var row = new VisualElement();
            row.AddToClassList("lo-banner");
            box.AddToClassList("lo-banner-box");
            row.Add(box);
            button.AddToClassList("lo-banner-button");
            row.Add(button);
            return row;
        }

        VisualElement CreateToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("lo-tab-toolbar");
            toolbar.Add(OptimizerUI.IconButton("d_Refresh", "Refresh", "Scan again: sounds, Luna settings and the last build.", Refresh));
            predictButton = new ToolbarButton(PredictMissing) { text = "Predict", tooltip = "Run Luna's sound step locally for sounds without an up-to-date prediction." };
            toolbar.Add(predictButton);
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new ToolbarButton(StopPreview) { text = "■ Stop", tooltip = "Stop the sound being played." });
            return toolbar;
        }

        VisualElement CreateBulkBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("lo-bulkbar");
            bulkBar = bar;
            searchBar = new SearchBar(searchText, "Search sounds by name or path   (Ctrl/Cmd+F)", value =>
            {
                searchText = value;
                ApplySortAndFilter();
            });
            bar.Add(searchBar);
            var gap = new VisualElement();
            gap.AddToClassList("lo-spacer");
            bar.Add(gap);
            // Lives in the tick column's header (see CreateTable).
            allShownToggle = new Toggle { tooltip = "Tick or untick every sound the search shows." };
            allShownToggle.AddToClassList("lo-header-toggle");
            allShownToggle.RegisterValueChangedCallback(change =>
            {
                foreach (var entry in visibleEntries)
                {
                    if (change.newValue)
                        ticked.Add(entry.AssetPath);
                    else
                        ticked.Remove(entry.AssetPath);
                }
                RefreshUI();
            });
            tickedLabel = new Label();
            tickedLabel.AddToClassList("lo-bulk-count");
            bar.Add(tickedLabel);
            bulkHint = new Label("Tick sounds to change their format, bitrate, channels or trim together.");
            bulkHint.AddToClassList("lo-muted");
            bulkHint.AddToClassList("lo-bulk-hint");
            bar.Add(bulkHint);
            bulkEdit = new VisualElement();
            bulkEdit.AddToClassList("lo-bulk-edit");
            bar.Add(bulkEdit);
            bulkFormatField = new DropdownField(new[] { KEEP_FORMAT }.Concat(FORMATS.Select(LunaSoundFormat.Label)).ToList(), 0);
            bulkFormatField.AddToClassList("lo-bulk-field");
            bulkFormatField.RegisterValueChangedCallback(change =>
            {
                var index = bulkFormatField.choices.IndexOf(change.newValue) - 1;
                bulkFormat = index >= 0 ? FORMATS[index] : (SoundFormat?)null;
                RefreshBulkBar();
            });
            bulkEdit.Add(bulkFormatField);
            bulkBitrateField = new DropdownField(new List<string> { KEEP_BITRATE }, 0);
            bulkBitrateField.AddToClassList("lo-bulk-field");
            bulkBitrateField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bulkEdit.Add(bulkBitrateField);
            bulkChannelsField = new DropdownField(new[] { KEEP_CHANNELS }.Concat(CHANNEL_LABELS).ToList(), 0);
            bulkChannelsField.AddToClassList("lo-bulk-field");
            bulkChannelsField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bulkEdit.Add(bulkChannelsField);
            bulkApplyButton = new Button(() =>
            {
                var targets = TickedVisible();
                var format = bulkFormat;
                var bitrate = ParseBitrate(bulkBitrateField.value);
                var channelIndex = bulkChannelsField.choices.IndexOf(bulkChannelsField.value) - 1;
                var channels = channelIndex >= 0 ? CHANNEL_VALUES[channelIndex] : (int?)null;
                EditorApplication.delayCall += () => ApplyBulk(targets, format, bitrate, channels);
            }) { tooltip = "Change the ticked sounds; nothing is written until Save all." };
            bulkApplyButton.AddToClassList("lo-primary");
            bulkEdit.Add(bulkApplyButton);
            bulkTrimButton = new Button(() =>
            {
                var targets = TickedVisible();
                EditorApplication.delayCall += () => ShowTrimResult(TrimSilence(targets));
            }) { text = "Trim silence", tooltip = "Leave out leading and trailing silence of the ticked sounds (within any trim already set). Nothing is written until Save all." };
            bulkEdit.Add(bulkTrimButton);
            untickButton = new Button(() =>
            {
                ticked.Clear();
                RefreshUI();
            }) { text = "Clear" };
            bulkEdit.Add(untickButton);
            return bar;
        }

        static int? ParseBitrate(string label)
        {
            int value;
            return label != null && int.TryParse(label.Replace(" kbps", string.Empty), out value) ? value : (int?)null;
        }

        MultiColumnListView CreateTable()
        {
            table = new MultiColumnListView
            {
                fixedItemHeight = ROW_HEIGHT,
                selectionType = SelectionType.Single,
                sortingEnabled = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                viewDataKey = "lo-sounds-table",
            };
            table.AddToClassList("lo-table");
            AddColumn(Column.Pick, string.Empty, 26f, false, () => new Toggle(), (element, entry) =>
                ((Toggle)element).SetValueWithoutNotify(ticked.Contains(entry.AssetPath)),
                element => ((Toggle)element).RegisterValueChangedCallback(change =>
                {
                    var entry = EntryOf(element);
                    if (entry == null)
                        return;
                    if (change.newValue) ticked.Add(entry.AssetPath); else ticked.Remove(entry.AssetPath);
                    RefreshBulkBar();
                }));
            AddColumn(Column.Name, "Sound", 200f, false, () => new Label(), (element, e) =>
            {
                var label = (Label)element;
                var queued = ExclusionQueue.IsQueued(e.AssetPath);
                label.text = e.FileName + (queued ? "   ⦸ excluding" : string.Empty);
                label.tooltip = e.AssetPath + (queued ? "\nWaiting in the Luna Exclusions window (Pending)." : string.Empty);
                label.EnableInClassList("lo-excluding", queued);
            }, null);
            AddLabelColumn(Column.Source, "Source", 100f, true, e => e.Extension.TrimStart('.') + " · " + FormatBytes(e.FileBytes), null);
            AddColumn(Column.Format, "Format", 100f, false, () => new DropdownField(), (element, entry) => BindFormatField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnFormatPicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Bitrate, "Bitrate", 90f, false, () => new DropdownField(), (element, entry) => BindBitrateField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnBitratePicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Channels, "Channels", 100f, false, () => new DropdownField(), (element, entry) => BindChannelsField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnChannelsPicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Trim, "Trim (s)", 132f, false, () =>
            {
                var row = new VisualElement();
                row.AddToClassList("lo-trim-cell");
                row.Add(TrimField());
                row.Add(new Label("–"));
                row.Add(TrimField());
                return row;
            }, BindTrimCell, element =>
            {
                ((FloatField)element[0]).RegisterValueChangedCallback(change => OnTrimEdited(element, change.newValue, null));
                ((FloatField)element[2]).RegisterValueChangedCallback(change => OnTrimEdited(element, null, change.newValue));
            });
            AddColumn(Column.Predicted, "Predicted", 120f, true, OptimizerUI.SizeCell, (element, entry) =>
            {
                BindPrediction((Label)element[1], entry);
                OptimizerUI.SetHeat(element, entry.PlayableBytes, largestPredicted);
            }, null);
            AddLabelColumn(Column.LastBuild, "Last build", 86f, true,
                e => e.HasLastBuild ? FormatBytes(e.LastBuildBytes) + (e.LastBuildOutdated ? " (old)" : string.Empty) : "–",
                e => e.LastBuildOutdated ? "The sound changed after the last Luna build." : null);
            AddColumn(Column.Listen, "Listen", 116f, false, () =>
            {
                var buttons = new VisualElement();
                buttons.AddToClassList("lo-listen");
                buttons.Add(new Button { text = "▶ Src", tooltip = "Play the clip as Unity imported it." });
                buttons.Add(new Button { text = "▶ Luna", tooltip = "Play what Luna would ship with the current settings." });
                return buttons;
            }, (element, entry) => element[1].SetEnabled(CanPredict), element =>
            {
                ((Button)element[0]).clicked += () => { var entry = EntryOf(element); if (entry != null) PlaySource(entry); };
                ((Button)element[1]).clicked += () => { var entry = EntryOf(element); if (entry != null) EditorApplication.delayCall += () => PlayLuna(entry); };
            });

            table.sortColumnDescriptions.Add(new SortColumnDescription(Column.Predicted.ToString(), SortDirection.Descending));
            table.columnSortingChanged += ApplySortAndFilter;
            table.selectionChanged += selection =>
            {
                var entry = selection.OfType<SoundEntry>().FirstOrDefault();
                selectedPath = entry != null ? entry.AssetPath : null;
                RefreshDetails();
            };
            table.itemsChosen += chosen =>
            {
                var entry = chosen.OfType<SoundEntry>().FirstOrDefault();
                if (entry != null)
                    OptimizerUI.Ping(entry.AssetPath);
            };
            return table;
        }

        // Every cell sits in a container that remembers its row's entry, for callbacks and the right-click menu.
        void AddColumn(Column column, string title, float width, bool rightAligned, Func<VisualElement> make,
            Action<VisualElement, SoundEntry> bind, Action<VisualElement> registerOnce)
        {
            table.columns.Add(new UnityEngine.UIElements.Column
            {
                name = column.ToString(),
                makeHeader = column == Column.Pick ? () => allShownToggle : (Func<VisualElement>)null,
                title = title,
                width = width,
                minWidth = Mathf.Min(width, 40f),
                sortable = column != Column.Pick && column != Column.Listen,
                stretchable = column == Column.Name,
                makeCell = () =>
                {
                    var cell = new VisualElement();
                    cell.AddToClassList("lo-cell");
                    if (rightAligned)
                        cell.AddToClassList("lo-right");
                    var control = make();
                    control.AddToClassList("lo-cell-control");
                    cell.Add(control);
                    cell.AddManipulator(new ContextualMenuManipulator(evt => BuildRowMenu(evt, EntryOf(cell))));
                    if (registerOnce != null)
                        registerOnce(control);
                    return cell;
                },
                bindCell = (cell, index) =>
                {
                    var entry = index >= 0 && index < visibleEntries.Count ? visibleEntries[index] : null;
                    cell.userData = entry;
                    if (entry != null)
                        bind(cell[0], entry);
                },
            });
        }

        void AddLabelColumn(Column column, string title, float width, bool rightAligned, Func<SoundEntry, string> text,
            Func<SoundEntry, string> tooltip, Func<SoundEntry, bool> warn = null)
        {
            AddColumn(column, title, width, rightAligned, () => new Label(), (element, entry) =>
            {
                var label = (Label)element;
                label.text = text(entry);
                label.tooltip = tooltip != null ? tooltip(entry) : null;
                label.EnableInClassList("lo-warning", warn != null && warn(entry));
            }, null);
        }

        static SoundEntry EntryOf(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                var entry = current.userData as SoundEntry;
                if (entry != null)
                    return entry;
            }
            return null;
        }

        void BuildRowMenu(ContextualMenuPopulateEvent evt, SoundEntry entry)
        {
            if (entry == null)
                return;
            var path = entry.AssetPath;
            evt.menu.AppendAction("Add to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(new[] { path }));
            var tickedPaths = TickedVisible().Select(e => e.AssetPath).ToList();
            if (tickedPaths.Count > 1)
                evt.menu.AppendAction("Add " + tickedPaths.Count + " ticked sounds to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(tickedPaths));
            evt.menu.AppendAction("Show in Project", _ => OptimizerUI.Ping(path));
        }

        // What Luna feeds ffmpeg: the .wav file as it is, or Unity's decoded import (orange: import settings matter).
        static string ExportedText(SoundEntry entry)
        {
            var channels = entry.Channels == 1 ? "mono" : entry.Channels == 2 ? "stereo" : entry.Channels + " ch";
            var text = channels + " " + (entry.Frequency / 1000f).ToString("0.#") + " kHz";
            return entry.UsesSourceFile ? text : text + " (import)";
        }

        static string ExportedTooltip(SoundEntry entry)
        {
            return entry.UsesSourceFile
                ? "Luna copies this .wav file unchanged; Unity's import settings do not apply."
                : "Luna decodes Unity's imported clip (Load Type " + entry.LoadType + (entry.ForceToMono ? ", Force To Mono" : string.Empty)
                    + "), so Unity's import settings and compression reach the build.";
        }

        static FloatField TrimField()
        {
            var field = new FloatField { isDelayed = true, formatString = "0.00" };
            field.AddToClassList("lo-trim-field");
            return field;
        }

        // Start and end in seconds of the part Luna keeps; editing either one trims the sound.
        void BindTrimCell(VisualElement element, SoundEntry entry)
        {
            var start = (FloatField)element[0];
            var end = (FloatField)element[2];
            start.SetValueWithoutNotify(SoundTrim.Start(entry));
            end.SetValueWithoutNotify(SoundTrim.End(entry));
            var editable = entry.Format != SoundFormat.Custom;
            start.SetEnabled(editable);
            end.SetEnabled(editable);
            element.EnableInClassList("lo-trimmed", entry.HasTrim && SoundTrim.Kept(entry) < entry.LengthSeconds - 0.01f);
            element.tooltip = editable
                ? "Seconds Luna keeps of the " + entry.LengthSeconds.ToString("0.00") + " s clip. Type a start or end to trim."
                : "Custom Luna script: trim is left as it is.";
        }

        void OnTrimEdited(VisualElement element, float? start, float? end)
        {
            var entry = EntryOf(element);
            if (entry == null)
                return;
            var newStart = start ?? SoundTrim.Start(entry);
            var newEnd = end ?? SoundTrim.End(entry);
            EditorApplication.delayCall += () => SetTrim(entry, newStart, newEnd);
        }

        void BindFormatField(DropdownField field, SoundEntry entry)
        {
            if (entry.Format == SoundFormat.Custom)
            {
                field.choices = new List<string> { LunaSoundFormat.Label(SoundFormat.Custom) };
                field.SetValueWithoutNotify(field.choices[0]);
                field.tooltip = "Custom Luna script, left as it is:\n" + entry.LunaRule.script;
                field.SetEnabled(false);
                return;
            }
            field.SetEnabled(true);
            field.choices = FORMATS.Select(LunaSoundFormat.Label).ToList();
            field.SetValueWithoutNotify(LunaSoundFormat.Label(entry.Format));
            field.tooltip = LunaSoundFormat.IsOpus(entry.Format) ? LunaSoundFormat.RISKS : "Luna's own mp3 encoding";
        }

        void OnFormatPicked(DropdownField field, string label)
        {
            var entry = EntryOf(field);
            if (entry == null || entry.Format == SoundFormat.Custom)
                return;
            var index = FORMATS.Select(LunaSoundFormat.Label).ToList().IndexOf(label);
            if (index < 0 || FORMATS[index] == entry.Format)
                return;
            var format = FORMATS[index];
            // The risk dialog may be declined: show the current format again until the change is made.
            field.SetValueWithoutNotify(LunaSoundFormat.Label(entry.Format));
            EditorApplication.delayCall += () => ChangeFormat(entry, format);
        }

        void BindBitrateField(DropdownField field, SoundEntry entry)
        {
            if (entry.Format == SoundFormat.Custom)
            {
                field.choices = new List<string> { "–" };
                field.SetValueWithoutNotify("–");
                field.SetEnabled(false);
                return;
            }
            field.SetEnabled(true);
            var values = BitratesFor(entry.Format).ToList();
            var current = entry.LunaRule.bitrate;
            if (!values.Contains(current))
                values.Insert(0, current);
            field.choices = values.Select(v => v > 0 ? v + " kbps" : "VBR (q8)").ToList();
            field.SetValueWithoutNotify(current > 0 ? current + " kbps" : "VBR (q8)");
            field.tooltip = (entry.IsLunaOverride ? "Per-sound override in Luna" : "Luna's default rule")
                + (LunaSoundFormat.IsOpus(entry.Format) ? " (Opus bitrate, written into the script)" : string.Empty);
        }

        void OnBitratePicked(DropdownField field, string label)
        {
            var entry = EntryOf(field);
            var bitrate = ParseBitrate(label);
            if (entry == null || !bitrate.HasValue || bitrate.Value == entry.LunaRule.bitrate)
                return;
            var value = bitrate.Value;
            EditorApplication.delayCall += () => ChangeRule(entry, rule => rule.bitrate = value);
        }

        void BindChannelsField(DropdownField field, SoundEntry entry)
        {
            if (entry.Format == SoundFormat.Custom)
            {
                field.choices = new List<string> { "–" };
                field.SetValueWithoutNotify("–");
                field.SetEnabled(false);
                return;
            }
            field.SetEnabled(true);
            var labels = CHANNEL_LABELS.ToList();
            var selected = Array.IndexOf(CHANNEL_VALUES, entry.LunaRule.channels);
            if (selected < 0)
            {
                labels.Add(entry.LunaRule.channels + " ch");
                selected = labels.Count - 1;
            }
            field.choices = labels;
            field.SetValueWithoutNotify(labels[selected]);
        }

        void OnChannelsPicked(DropdownField field, string label)
        {
            var entry = EntryOf(field);
            var index = Array.IndexOf(CHANNEL_LABELS, label);
            if (entry == null || index < 0 || CHANNEL_VALUES[index] == entry.LunaRule.channels)
                return;
            var channels = CHANNEL_VALUES[index];
            EditorApplication.delayCall += () => ChangeRule(entry, rule => rule.channels = channels);
        }

        void BindPrediction(Label label, SoundEntry entry)
        {
            var prediction = entry.Prediction;
            label.RemoveFromClassList("lo-warning");
            label.RemoveFromClassList("lo-pending-cell");
            if (prediction == null)
            {
                label.text = "–";
                label.tooltip = null;
                return;
            }
            if (!prediction.success)
            {
                label.text = "failed";
                label.tooltip = prediction.error;
                label.AddToClassList("lo-warning");
                return;
            }
            var tooltip = "Luna output: " + (prediction.outputExtension ?? ".mp3").TrimStart('.') + ", " + FormatBytes(prediction.outputBytes) + " file + " + entry.SidecarBytes + " B json, ×4/3 base64\n"
                + "Luna input: " + (prediction.usedSourceFile ? "your .wav file" : "Unity's decoded import") + ", " + FormatBytes(prediction.inputBytes)
                + (LunaSoundFormat.IsOpus(entry.Format) ? "\nOpus: same script Luna will run; not yet compared with a real Luna build" : string.Empty);
            label.text = FormatBytes(entry.PlayableBytes);
            if (entry.HasPendingChange && entry.PendingFromBytes >= 0)
                label.text += "  " + OptimizerUI.Delta(entry.PlayableBytes - entry.PendingFromBytes);
            if (entry.HasPendingChange)
            {
                tooltip = "Unsaved change" + (entry.PendingFromBytes >= 0 ? ", was " + FormatBytes(entry.PendingFromBytes) : string.Empty) + "\n" + tooltip;
                label.AddToClassList("lo-pending-cell");
            }
            label.tooltip = tooltip;
        }

        List<SoundEntry> TickedVisible()
        {
            return visibleEntries.Where(e => ticked.Contains(e.AssetPath)).ToList();
        }

        // Everything that follows the data: table rows, summary, banners, bulk bar, toolbar states, details panel.
        void RefreshUI()
        {
            if (table == null)
                return;
            largestPredicted = visibleEntries.Count > 0 ? visibleEntries.Max(e => e.PlayableBytes) : 0;
            table.itemsSource = visibleEntries;
            table.RefreshItems();
            var index = selectedPath != null ? visibleEntries.FindIndex(e => e.AssetPath == selectedPath) : -1;
            if (index >= 0)
                table.SetSelectionWithoutNotify(new[] { index });
            else
                table.ClearSelection();

            RefreshStats();

            staleRow.style.display = staleOverrides.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            staleBox.text = staleOverrides.Count + " Luna sound override(s) name files that no longer exist, so Luna ignores them and those sounds use the default rule.";
            staleBox.tooltip = string.Join("\n", staleOverrides);

            var opus = entries.Where(e => LunaSoundFormat.IsOpus(e.Format)).ToList();
            opusRow.style.display = opus.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (opus.Count > 0)
            {
                var ogg = opus.Count(e => e.Format == SoundFormat.OggOpus);
                opusBox.text = opus.Count + " sound(s) use Opus (" + ogg + " Ogg, " + (opus.Count - ogg) + " WebM). Phones that cannot decode it play them silently: "
                    + "Ogg needs iOS 18.4+, WebM about iOS 17.4+. Builds need ffmpeg with libopus at "
                    + (LunaSoundFormat.ScriptExecutable(opus[0].LunaRule.script) ?? "?") + ". Hover for details.";
            }

            predictButton.SetEnabled(CanPredict && entries.Any(e => e.Prediction == null));
            RefreshBulkBar();
            RefreshDetails();
        }

        void RefreshStats()
        {
            statStrip.Clear();
            string problem = null;
            if (settings == null)
                problem = LunaSettings.FILE_NAME + " not found in the project root. Open the Luna window once to create it.";
            else if (!CanPredict)
                problem = "No Luna install with ffmpeg found, so sizes cannot be predicted.";
            summaryLabel.text = problem ?? string.Empty;
            summaryLabel.style.display = problem != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (settings == null)
                return;

            OptimizerUI.AddStat(statStrip, entries.Count.ToString(), "sounds");
            OptimizerUI.AddStat(statStrip, FormatBytes(entries.Sum(e => e.FileBytes)), "source files");
            OptimizerUI.AddStat(statStrip, entries.Sum(e => SoundTrim.Kept(e)).ToString("0.0") + " s", "audio kept", "Total length Luna ships after trims.");
            var predicted = entries.Where(e => e.HasPrediction).ToList();
            if (predicted.Count > 0)
                OptimizerUI.AddStat(statStrip, FormatBytes(predicted.Sum(e => e.PlayableBytes)),
                    "predicted in playable" + (predicted.Count < entries.Count ? " (" + predicted.Count + "/" + entries.Count + ")" : string.Empty),
                    "What Luna would ship for these sounds (base64) with the current (unsaved included) settings.", "lo-stat--key");
            if (PendingCount > 0)
                OptimizerUI.AddStat(statStrip, OptimizerUI.Delta(-PendingSavingBytes), PendingCount + " unsaved", "Predicted change once saved.", "lo-stat--accent");
            if (stats != null)
                OptimizerUI.AddStat(statStrip, FormatBytes(entries.Where(e => e.HasLastBuild).Sum(e => e.LastBuildBytes)), "last build",
                    "Built " + stats.BuiltAt.ToString("yyyy-MM-dd HH:mm"));
            var excluded = OptimizerUI.ExcludedCount(settings);
            if (excluded > 0)
                OptimizerUI.AddStat(statStrip, excluded.ToString(), "excluded", "Excluded assets/folders are not listed: see Exclusions.");
        }

        void RefreshBulkBar()
        {
            if (tickedLabel == null)
                return;
            searchBar.SetCount(visibleEntries.Count, entries.Count);
            var targets = TickedVisible();
            var hidden = ticked.Count - targets.Count;
            allShownToggle.SetValueWithoutNotify(visibleEntries.Count > 0 && targets.Count == visibleEntries.Count);
            tickedLabel.text = targets.Count > 0 ? targets.Count + " selected" + (hidden > 0 ? " (+" + hidden + " hidden)" : string.Empty) : string.Empty;
            bulkBar.EnableInClassList("lo-bulkbar--active", targets.Count > 0);
            bulkEdit.style.display = targets.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            bulkHint.style.display = targets.Count > 0 ? DisplayStyle.None : DisplayStyle.Flex;
            tickedLabel.tooltip = hidden > 0 ? "Ticked sounds the search hides are not changed." : null;

            var bitrates = new[] { KEEP_BITRATE }.Concat(BulkBitrates(targets).Select(b => b + " kbps")).ToList();
            var currentBitrate = bulkBitrateField.value;
            bulkBitrateField.choices = bitrates;
            bulkBitrateField.SetValueWithoutNotify(bitrates.Contains(currentBitrate) ? currentBitrate : KEEP_BITRATE);

            var anyChange = bulkFormat.HasValue || bulkBitrateField.value != KEEP_BITRATE || bulkChannelsField.value != KEEP_CHANNELS;
            bulkApplyButton.text = "Apply to " + targets.Count;
            bulkApplyButton.SetEnabled(targets.Count > 0 && anyChange);
            untickButton.SetEnabled(ticked.Count > 0);
            bulkTrimButton.SetEnabled(targets.Count > 0);
        }

        void ShowTrimResult(int changed)
        {
            ShowNotification(new GUIContent(changed == 0 ? "No silence to trim" : changed + " sound(s) trimmed: review, then Save all"));
        }

        // ---------------------------------------------------------------- details panel

        SoundEntry SelectedEntry
        {
            get { return selectedPath != null ? entries.FirstOrDefault(e => e.AssetPath == selectedPath) : null; }
        }

        void RefreshDetails()
        {
            if (details == null)
                return;
            details.Clear();
            var entry = SelectedEntry;
            if (entry == null)
            {
                var empty = new Label("Select a sound to hear Luna's output, trim it, change its format, bitrate and channels, and see what uses it.");
                empty.AddToClassList("lo-empty");
                details.Add(empty);
                return;
            }

            var badges = new List<Label>
            {
                entry.IsLunaOverride ? OptimizerUI.Badge("Override", null, "Per-sound override in Luna") : OptimizerUI.Badge("Default rule", null, "Uses Luna's default sound rule"),
                OptimizerUI.Badge(LunaSoundFormat.Label(entry.Format), null, "Format Luna ships"),
            };
            if (entry.HasTrim && SoundTrim.Kept(entry) < entry.LengthSeconds - 0.01f)
                badges.Add(OptimizerUI.Badge("Trimmed", null, "Luna keeps " + SoundTrim.Kept(entry).ToString("0.00") + " of " + entry.LengthSeconds.ToString("0.00") + " s"));
            if (entry.HasPendingChange)
                badges.Add(OptimizerUI.Badge("Unsaved", "accent"));
            if (!entry.UsesSourceFile)
                badges.Add(OptimizerUI.Badge("Decoded import", "warn", ExportedTooltip(entry)));
            if (ExclusionQueue.IsQueued(entry.AssetPath))
                badges.Add(OptimizerUI.Badge("Excluding", "warn", "Waiting in the Luna Exclusions window (Pending)."));
            details.Add(OptimizerUI.DetailsHeader(entry.FileName, entry.AssetPath, badges));
            if (entry.HasPrediction)
                details.Add(OptimizerUI.Metric(FormatBytes(entry.PlayableBytes), "in playable",
                    entry.HasPendingChange && entry.PendingFromBytes >= 0 ? OptimizerUI.Delta(entry.PlayableBytes - entry.PendingFromBytes) + " unsaved" : null,
                    entry.PlayableBytes <= entry.PendingFromBytes));

            details.Add(OptimizerUI.SectionTitle("Listen & trim"));
            var sourceRow = WaveRow("Source", Path.GetFullPath(entry.AssetPath), SOURCE_WAVE, () => PlaySource(entry), null);
            details.Add(sourceRow);
            if (entry.Format != SoundFormat.Custom)
                details.Add(TrimEditor(entry, sourceRow[1]));
            var lunaPath = CanPredict ? LunaSoundPipeline.PreviewPathFor(entry, projectRoot, install) : null;
            details.Add(WaveRow("Luna", lunaPath != null && File.Exists(lunaPath) ? lunaPath : null, LUNA_WAVE,
                () => EditorApplication.delayCall += () => PlayLuna(entry),
                CanPredict ? "Press Play to render Luna's output for the current settings." : "No Luna ffmpeg found."));
            var stop = new Button(StopPreview) { text = "Stop" };
            stop.AddToClassList("lo-small-button");
            details.Add(stop);

            details.Add(OptimizerUI.SectionTitle("Luna settings"));
            var format = new DropdownField("Format") { userData = entry };
            BindFormatField(format, entry);
            format.RegisterValueChangedCallback(change => OnFormatPicked(format, change.newValue));
            details.Add(format);
            var bitrate = new DropdownField("Bitrate") { userData = entry };
            BindBitrateField(bitrate, entry);
            bitrate.RegisterValueChangedCallback(change => OnBitratePicked(bitrate, change.newValue));
            details.Add(bitrate);
            var channels = new DropdownField("Channels") { userData = entry };
            BindChannelsField(channels, entry);
            channels.RegisterValueChangedCallback(change => OnChannelsPicked(channels, change.newValue));
            details.Add(channels);
            if (LunaSoundFormat.IsOpus(entry.Format))
            {
                var risks = new HelpBox(LunaSoundFormat.RISKS, HelpBoxMessageType.Warning);
                risks.AddToClassList("lo-small");
                details.Add(risks);
            }

            details.Add(OptimizerUI.SectionTitle("Size"));
            details.Add(OptimizerUI.InfoRow("Source", entry.Extension.TrimStart('.') + " · " + FormatBytes(entry.FileBytes) + " · " + entry.LengthSeconds.ToString("0.00") + " s"));
            var exported = OptimizerUI.InfoRow("Luna input", ExportedText(entry));
            exported.tooltip = ExportedTooltip(entry);
            exported.EnableInClassList("lo-warning", !entry.UsesSourceFile);
            details.Add(exported);
            var prediction = entry.Prediction;
            if (prediction != null && prediction.success)
            {
                details.Add(OptimizerUI.InfoRow("Luna output", (prediction.outputExtension ?? ".mp3").TrimStart('.') + " · " + FormatBytes(prediction.outputBytes) + " file"));
                if (entry.HasPendingChange && entry.PendingFromBytes >= 0)
                    details.Add(OptimizerUI.InfoRow("Saved setting", FormatBytes(entry.PendingFromBytes) + " in playable"));
            }
            else
            {
                details.Add(OptimizerUI.InfoRow("Luna output", prediction == null ? "not predicted yet (Predict)" : "prediction failed: " + prediction.error));
            }
            details.Add(OptimizerUI.InfoRow("Last build", entry.HasLastBuild ? FormatBytes(entry.LastBuildBytes) + (entry.LastBuildOutdated ? " (sound changed since)" : string.Empty) : "–"));

            details.Add(OptimizerUI.SectionTitle("Used by"));
            details.Add(UsedBy(entry.AssetPath));

            var actions = new VisualElement();
            actions.AddToClassList("lo-details-actions");
            var ping = new Button(() => OptimizerUI.Ping(entry.AssetPath)) { text = "Show in Project" };
            ping.AddToClassList("lo-details-button");
            actions.Add(ping);
            var exclude = new Button(() => LunaExclusionWindow.AddAndShow(new[] { entry.AssetPath })) { text = "Exclude from build…", tooltip = "Review it in the Luna Exclusions window before anything is written." };
            exclude.AddToClassList("lo-details-button");
            actions.Add(exclude);
            details.Add(actions);
        }

        // Range slider over the source waveform (the parts left out are shaded), exact start / end fields, and
        // shortcuts. Predicted once the handles rest and the drag ended; nothing is written until Save all.
        VisualElement TrimEditor(SoundEntry entry, VisualElement wave)
        {
            var length = entry.LengthSeconds;
            var box = new VisualElement();
            box.AddToClassList("lo-trim-editor");
            var leftShade = TrimShade(true);
            var rightShade = TrimShade(false);
            wave.Add(leftShade);
            wave.Add(rightShade);

            var slider = new MinMaxSlider(SoundTrim.Start(entry), SoundTrim.End(entry), 0f, length);
            slider.AddToClassList("lo-trim-slider");
            box.Add(slider);
            var fields = new VisualElement();
            fields.AddToClassList("lo-row");
            var start = new FloatField("Start") { isDelayed = true, formatString = "0.00", value = SoundTrim.Start(entry) };
            var end = new FloatField("End") { isDelayed = true, formatString = "0.00", value = SoundTrim.End(entry) };
            start.AddToClassList("lo-trim-detail-field");
            end.AddToClassList("lo-trim-detail-field");
            var kept = OptimizerUI.SmallLabel(string.Empty);
            kept.AddToClassList("lo-trim-kept");
            fields.Add(start);
            fields.Add(end);
            fields.Add(kept);
            box.Add(fields);

            Action<Vector2> show = range =>
            {
                leftShade.style.width = Length.Percent(length > 0f ? range.x / length * 100f : 0f);
                rightShade.style.width = Length.Percent(length > 0f ? (length - range.y) / length * 100f : 0f);
                start.SetValueWithoutNotify(range.x);
                end.SetValueWithoutNotify(range.y);
                kept.text = "keeps " + (range.y - range.x).ToString("0.00") + " of " + length.ToString("0.00") + " s";
            };
            show(slider.value);
            slider.RegisterValueChangedCallback(change =>
            {
                var range = change.newValue;
                show(range);
                if (trimCommit != null)
                    trimCommit.Pause();
                trimCommit = slider.schedule.Execute(() => CommitTrim(slider, entry, range));
                trimCommit.ExecuteLater(TRIM_COMMIT_DELAY_MS);
            });
            start.RegisterValueChangedCallback(change =>
            {
                var value = change.newValue;
                EditorApplication.delayCall += () => SetTrim(entry, value, SoundTrim.End(entry));
            });
            end.RegisterValueChangedCallback(change =>
            {
                var value = change.newValue;
                EditorApplication.delayCall += () => SetTrim(entry, SoundTrim.Start(entry), value);
            });

            var buttons = new VisualElement();
            buttons.AddToClassList("lo-row");
            var silence = new Button(() => EditorApplication.delayCall += () => ShowTrimResult(TrimSilence(new List<SoundEntry> { entry })))
            {
                text = "Trim silence",
                tooltip = "Leave out leading and trailing silence (below about -50 dB, with 30 ms kept around the sound).",
            };
            silence.AddToClassList("lo-small-button");
            buttons.Add(silence);
            var whole = new Button(() => EditorApplication.delayCall += () => SetTrim(entry, 0f, length)) { text = "Keep whole clip" };
            whole.AddToClassList("lo-small-button");
            whole.SetEnabled(entry.HasTrim);
            buttons.Add(whole);
            box.Add(buttons);
            return box;
        }

        static VisualElement TrimShade(bool left)
        {
            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            shade.AddToClassList("lo-trim-shade");
            shade.AddToClassList(left ? "lo-trim-shade--left" : "lo-trim-shade--right");
            return shade;
        }

        void CommitTrim(MinMaxSlider slider, SoundEntry entry, Vector2 range)
        {
            // Still dragging: try again shortly (re-predicting rebuilds this panel and would end the drag).
            if (slider.panel != null && slider.panel.GetCapturingElement(PointerId.mousePointerId) != null)
            {
                trimCommit = slider.schedule.Execute(() => CommitTrim(slider, entry, range));
                trimCommit.ExecuteLater(TRIM_COMMIT_DELAY_MS);
                return;
            }
            if (Mathf.Abs(range.x - SoundTrim.Start(entry)) > 0.0005f || Mathf.Abs(range.y - SoundTrim.End(entry)) > 0.0005f)
                SetTrim(entry, range.x, range.y);
        }

        // Label, play button and waveform image of a file Unity can decode (or a hint when there is none yet).
        VisualElement WaveRow(string name, string filePath, Color color, Action play, string missingHint)
        {
            var row = new VisualElement();
            row.AddToClassList("lo-wave-row");
            var head = new VisualElement();
            head.AddToClassList("lo-row");
            var playButton = new Button(play) { text = "▶ " + name };
            playButton.AddToClassList("lo-small-button");
            head.Add(playButton);
            row.Add(head);
            var wave = new VisualElement();
            wave.AddToClassList("lo-wave");
            var texture = filePath != null ? WaveformFor(filePath, color) : null;
            if (texture != null)
                wave.style.backgroundImage = new StyleBackground(texture);
            else
                wave.Add(OptimizerUI.SmallLabel(filePath == null ? missingHint ?? string.Empty : "Unity cannot decode this file for a waveform."));
            row.Add(wave);
            return row;
        }

        Texture2D WaveformFor(string filePath, Color color)
        {
            var key = filePath + "|" + File.GetLastWriteTimeUtc(filePath).Ticks;
            Texture2D texture;
            if (waveforms.TryGetValue(key, out texture) && texture != null)
                return texture;
            var clip = LoadClip(filePath);
            texture = AudioWaveform.Render(clip, WAVE_WIDTH, WAVE_HEIGHT, color);
            if (clip != null)
                UnityEngine.Object.DestroyImmediate(clip);
            waveforms[key] = texture;
            return texture;
        }

        void ClearWaveforms()
        {
            foreach (var texture in waveforms.Values.Where(t => t != null))
                UnityEngine.Object.DestroyImmediate(texture);
            waveforms.Clear();
        }

        // The chain from this sound up to the scene that ships it (BlastRadius), computed once per sound.
        VisualElement UsedBy(string assetPath)
        {
            BlastRadius result;
            if (!usedByCache.TryGetValue(assetPath, out result))
            {
                result = settings != null ? BlastRadius.Analyze(new[] { assetPath }, settings) : null;
                usedByCache[assetPath] = result;
            }
            return OptimizerUI.UsedBy(result);
        }

        void PlaySource(SoundEntry entry)
        {
            StopPreview();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.AssetPath);
            if (clip != null)
                PlayClip(clip);
        }

        void PlayLuna(SoundEntry entry)
        {
            StopPreview();
            var path = LunaSoundPipeline.PreviewPathFor(entry, projectRoot, install);
            if (!File.Exists(path))
            {
                // The preview file may have been cleared: make it again.
                var predictions = LunaSoundPipeline.PredictMany(new[] { entry }, install, projectRoot, null, null);
                if (predictions[0] != null)
                    entry.Prediction = predictions[0];
            }
            if (!File.Exists(path))
            {
                ShowNotification(new GUIContent("Could not make the Luna output" + (entry.Prediction != null ? ": " + entry.Prediction.error : string.Empty)));
                return;
            }
            previewClip = LoadClip(path);
            if (previewClip == null)
            {
                ShowNotification(new GUIContent("Unity could not load the Luna output"));
                return;
            }
            previewClip.name = entry.FileName + " (Luna)";
            PlayClip(previewClip);
            RefreshDetails();
        }

        // Loads a local mp3 or wav file as a fully decoded clip; file requests finish within milliseconds.
        static AudioClip LoadClip(string path)
        {
            var type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV
                : path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS
                : path.EndsWith(".aif", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) ? AudioType.AIFF
                : AudioType.MPEG;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
                var operation = request.SendWebRequest();
                var started = Environment.TickCount;
                while (!operation.isDone && Environment.TickCount - started < LOAD_TIMEOUT_MS)
                    Thread.Sleep(5);
                return request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
            }
        }

        // Unity's internal preview player (the one the Inspector uses); works outside Play Mode.
        static void PlayClip(AudioClip clip)
        {
            var method = AudioUtilMethod("PlayPreviewClip");
            if (method != null)
                method.Invoke(null, new object[] { clip, 0, false });
        }

        void StopPreview()
        {
            var method = AudioUtilMethod("StopAllPreviewClips");
            if (method != null)
                method.Invoke(null, null);
            if (previewClip != null)
            {
                UnityEngine.Object.DestroyImmediate(previewClip);
                previewClip = null;
            }
        }

        static MethodInfo AudioUtilMethod(string name)
        {
            var type = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            return type != null ? type.GetMethod(name, BindingFlags.Static | BindingFlags.Public) : null;
        }

        void ApplySortAndFilter()
        {
            visibleEntries = searchText.Length == 0
                ? new List<SoundEntry>(entries)
                : entries.Where(e => e.AssetPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var sort = table != null ? table.sortedColumns.FirstOrDefault() : null;
            Column column;
            if (sort != null && Enum.TryParse(sort.columnName, out column) && column != Column.Pick && column != Column.Listen)
            {
                var comparison = ComparisonFor(column);
                var ascending = sort.direction == SortDirection.Ascending;
                visibleEntries.Sort((a, b) => ascending ? comparison(a, b) : comparison(b, a));
            }
            else if (table == null)
            {
                var comparison = ComparisonFor(Column.Predicted);
                visibleEntries.Sort((a, b) => comparison(b, a));
            }
            RefreshUI();
        }

        static Comparison<SoundEntry> ComparisonFor(Column column)
        {
            switch (column)
            {
                case Column.Source: return (a, b) => a.FileBytes.CompareTo(b.FileBytes);
                case Column.Format: return (a, b) => a.Format.CompareTo(b.Format);
                case Column.Bitrate: return (a, b) => a.LunaRule.bitrate.CompareTo(b.LunaRule.bitrate);
                case Column.Channels: return (a, b) => a.LunaRule.channels.CompareTo(b.LunaRule.channels);
                case Column.Trim: return (a, b) => SoundTrim.Kept(a).CompareTo(SoundTrim.Kept(b));
                case Column.Predicted: return (a, b) => a.PlayableBytes.CompareTo(b.PlayableBytes);
                case Column.LastBuild: return (a, b) => a.LastBuildBytes.CompareTo(b.LastBuildBytes);
                default: return (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}
