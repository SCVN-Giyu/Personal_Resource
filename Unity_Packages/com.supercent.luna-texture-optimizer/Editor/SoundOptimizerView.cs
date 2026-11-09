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
    // for the selected one (Source / Luna waveforms and playback, format, bitrate, channels, what uses it).
    // Changes stay in memory and are predicted; the hub's Save all writes them as Luna per-sound overrides.
    [Serializable]
    public class SoundOptimizerView : OptimizerView
    {
        const float ROW_HEIGHT = 22f;
        const int LOAD_TIMEOUT_MS = 5000;
        const float DETAILS_WIDTH = 400f;
        const int WAVE_WIDTH = 512;
        const int WAVE_HEIGHT = 64;
        const string KEEP_FORMAT = "Format: keep";
        const string KEEP_BITRATE = "Bitrate: keep";
        const string KEEP_CHANNELS = "Channels: keep";
        static readonly Color SOURCE_WAVE = new Color(0.55f, 0.75f, 0.95f);
        static readonly Color LUNA_WAVE = new Color(0.45f, 0.85f, 0.5f);

        enum Column
        {
            Pick,
            Name,
            Source,
            Length,
            SourceChannels,
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
        ToolbarButton predictButton;
        ScrollView details;

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
            var moves = new Dictionary<string, string>();
            var removals = new List<string>();
            foreach (var name in staleOverrides)
            {
                var fileName = Path.GetFileName(name);
                var matches = entries.Where(e => e.FileName == fileName).ToList();
                if (matches.Count == 1 && !settings.SoundOverrides.Any(r => r.name == matches[0].AssetPath))
                    moves[name] = matches[0].AssetPath;
                else
                    removals.Add(name);
            }

            var lines = moves.Select(m => "Move  " + m.Key + "\n   → " + m.Value)
                .Concat(removals.Select(r => "Remove  " + r)).ToList();
            if (!EditorUtility.DisplayDialog("Fix stale sound overrides",
                    string.Join("\n", lines) + "\n\nMoved overrides keep their bitrate and channels. Write these changes to "
                    + LunaSettings.FILE_NAME + "?", "Fix", "Cancel"))
                return;

            string error;
            if (!LunaSettingsWriter.RepairSoundOverrides(moves, removals, projectRoot, out error))
            {
                EditorUtility.DisplayDialog("Fix stale sound overrides", "Nothing was changed.\n\n" + error, "OK");
                return;
            }
            Refresh();
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
                    && (rule.script ?? string.Empty) == (saved.script ?? string.Empty) && (rule.ext ?? string.Empty) == (saved.ext ?? string.Empty))
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
            var custom = targets.Where(e => e.Format == SoundFormat.Custom).ToList();
            targets = targets.Except(custom).ToList();
            string ffmpeg = null;
            if (format.HasValue && targets.Any(e => e.Format != format.Value)
                && !ConfirmFormat(format.Value, targets.Count + " sound(s)", out ffmpeg))
                return;

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
            if (notes.Count > 0)
                EditorUtility.DisplayDialog("Apply to ticked sounds", string.Join("\n\n", notes), "OK");
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
            summaryLabel = new Label();
            summaryLabel.AddToClassList("lo-summary");
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

            var split = new TwoPaneSplitView(1, DETAILS_WIDTH, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "lo-sounds-split" };
            split.AddToClassList("lo-split");
            split.Add(CreateTable());
            details = new ScrollView();
            details.AddToClassList("lo-details");
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
            toolbar.Add(new ToolbarButton(Refresh) { text = "Refresh", tooltip = "Scan again: sounds, Luna settings and the last build." });
            predictButton = new ToolbarButton(PredictMissing) { text = "Predict", tooltip = "Run Luna's sound step locally for sounds without an up-to-date prediction." };
            toolbar.Add(predictButton);
            toolbar.Add(new ToolbarButton(StopPreview) { text = "Stop", tooltip = "Stop the sound being played." });
            var spacer = new VisualElement();
            spacer.AddToClassList("lo-spacer");
            toolbar.Add(spacer);
            var search = new ToolbarSearchField { value = searchText };
            search.AddToClassList("lo-search");
            search.RegisterValueChangedCallback(change =>
            {
                searchText = change.newValue ?? string.Empty;
                ApplySortAndFilter();
            });
            toolbar.Add(search);
            return toolbar;
        }

        VisualElement CreateBulkBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("lo-bulkbar");
            allShownToggle = new Toggle("All shown") { tooltip = "Tick or untick every sound the search shows." };
            allShownToggle.AddToClassList("lo-bulk-all");
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
            bar.Add(allShownToggle);
            tickedLabel = new Label();
            tickedLabel.AddToClassList("lo-bulk-count");
            bar.Add(tickedLabel);
            bulkFormatField = new DropdownField(new[] { KEEP_FORMAT }.Concat(FORMATS.Select(LunaSoundFormat.Label)).ToList(), 0);
            bulkFormatField.AddToClassList("lo-bulk-field");
            bulkFormatField.RegisterValueChangedCallback(change =>
            {
                var index = bulkFormatField.choices.IndexOf(change.newValue) - 1;
                bulkFormat = index >= 0 ? FORMATS[index] : (SoundFormat?)null;
                RefreshBulkBar();
            });
            bar.Add(bulkFormatField);
            bulkBitrateField = new DropdownField(new List<string> { KEEP_BITRATE }, 0);
            bulkBitrateField.AddToClassList("lo-bulk-field");
            bulkBitrateField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bar.Add(bulkBitrateField);
            bulkChannelsField = new DropdownField(new[] { KEEP_CHANNELS }.Concat(CHANNEL_LABELS).ToList(), 0);
            bulkChannelsField.AddToClassList("lo-bulk-field");
            bulkChannelsField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bar.Add(bulkChannelsField);
            bulkApplyButton = new Button(() =>
            {
                var targets = TickedVisible();
                var format = bulkFormat;
                var bitrate = ParseBitrate(bulkBitrateField.value);
                var channelIndex = bulkChannelsField.choices.IndexOf(bulkChannelsField.value) - 1;
                var channels = channelIndex >= 0 ? CHANNEL_VALUES[channelIndex] : (int?)null;
                EditorApplication.delayCall += () => ApplyBulk(targets, format, bitrate, channels);
            }) { tooltip = "Change the ticked sounds; nothing is written until Save all." };
            bar.Add(bulkApplyButton);
            untickButton = new Button(() =>
            {
                ticked.Clear();
                RefreshUI();
            }) { text = "Untick all" };
            bar.Add(untickButton);
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
            AddLabelColumn(Column.Source, "Source", 80f, true, e => e.Extension.TrimStart('.') + " · " + FormatBytes(e.FileBytes), null);
            AddLabelColumn(Column.Length, "Length", 56f, true, e => e.LengthSeconds.ToString("0.0") + " s", null);
            AddLabelColumn(Column.SourceChannels, "Exported", 110f, false, ExportedText, ExportedTooltip, e => !e.UsesSourceFile);
            AddColumn(Column.Format, "Format", 100f, false, () => new DropdownField(), (element, entry) => BindFormatField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnFormatPicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Bitrate, "Bitrate", 90f, false, () => new DropdownField(), (element, entry) => BindBitrateField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnBitratePicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Channels, "Channels", 100f, false, () => new DropdownField(), (element, entry) => BindChannelsField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnChannelsPicked((DropdownField)element, change.newValue)));
            AddLabelColumn(Column.Trim, "Trim", 90f, false, e => e.HasTrim ? e.LunaRule.startTime + "–" + e.LunaRule.endTime : "–",
                e => e.HasTrim ? "Trim set in the Luna window" : null);
            AddColumn(Column.Predicted, "Predicted", 86f, true, () => new Label(), (element, entry) => BindPrediction((Label)element, entry), null);
            AddLabelColumn(Column.LastBuild, "Last build", 86f, true,
                e => e.HasLastBuild ? FormatBytes(e.LastBuildBytes) + (e.LastBuildOutdated ? " (old)" : string.Empty) : "–",
                e => e.LastBuildOutdated ? "The sound changed after the last Luna build." : null);
            AddColumn(Column.Listen, "Listen", 104f, false, () =>
            {
                var buttons = new VisualElement();
                buttons.AddToClassList("lo-row");
                buttons.Add(new Button { text = "Source", tooltip = "Play the clip as Unity imported it." });
                buttons.Add(new Button { text = "Luna", tooltip = "Play what Luna would ship with the current settings." });
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
            table.itemsSource = visibleEntries;
            table.RefreshItems();
            var index = selectedPath != null ? visibleEntries.FindIndex(e => e.AssetPath == selectedPath) : -1;
            if (index >= 0)
                table.SetSelectionWithoutNotify(new[] { index });
            else
                table.ClearSelection();

            var summary = entries.Count + " sounds  ·  source files " + FormatBytes(entries.Sum(e => e.FileBytes));
            var predicted = entries.Where(e => e.HasPrediction).ToList();
            if (predicted.Count > 0)
                summary += "  ·  predicted " + FormatBytes(predicted.Sum(e => e.PlayableBytes)) + " in playable (base64)"
                    + (predicted.Count < entries.Count ? " (" + predicted.Count + "/" + entries.Count + ")" : string.Empty);
            if (stats != null)
                summary += "  ·  last build " + FormatBytes(entries.Where(e => e.HasLastBuild).Sum(e => e.LastBuildBytes))
                    + " (" + stats.BuiltAt.ToString("yyyy-MM-dd HH:mm") + ")";
            if (settings == null)
                summary = LunaSettings.FILE_NAME + " not found in the project root. Open the Luna window once to create it.";
            else if (!CanPredict)
                summary += "  ·  no Luna install with ffmpeg found, so sizes cannot be predicted";
            summaryLabel.text = summary + OptimizerUI.ExcludedNote(settings);

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

        void RefreshBulkBar()
        {
            if (tickedLabel == null)
                return;
            var targets = TickedVisible();
            var hidden = ticked.Count - targets.Count;
            allShownToggle.SetValueWithoutNotify(visibleEntries.Count > 0 && targets.Count == visibleEntries.Count);
            tickedLabel.text = targets.Count + " ticked" + (hidden > 0 ? " (+" + hidden + " hidden)" : string.Empty);
            tickedLabel.tooltip = hidden > 0 ? "Ticked sounds the search hides are not changed." : null;

            var bitrates = new[] { KEEP_BITRATE }.Concat(BulkBitrates(targets).Select(b => b + " kbps")).ToList();
            var currentBitrate = bulkBitrateField.value;
            bulkBitrateField.choices = bitrates;
            bulkBitrateField.SetValueWithoutNotify(bitrates.Contains(currentBitrate) ? currentBitrate : KEEP_BITRATE);

            var anyChange = bulkFormat.HasValue || bulkBitrateField.value != KEEP_BITRATE || bulkChannelsField.value != KEEP_CHANNELS;
            bulkApplyButton.text = "Apply to " + targets.Count;
            bulkApplyButton.SetEnabled(targets.Count > 0 && anyChange);
            untickButton.SetEnabled(ticked.Count > 0);
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
                var empty = new Label("Select a sound to hear and see Luna's output, change its format, bitrate and channels, and see what uses it.");
                empty.AddToClassList("lo-empty");
                details.Add(empty);
                return;
            }

            var title = new Label(entry.FileName);
            title.AddToClassList("lo-details-title");
            details.Add(title);
            details.Add(OptimizerUI.LinkLabel(entry.AssetPath, entry.AssetPath, "lo-small"));

            details.Add(OptimizerUI.SectionTitle("Listen"));
            details.Add(WaveRow("Source", Path.GetFullPath(entry.AssetPath), SOURCE_WAVE, () => PlaySource(entry), null));
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
            details.Add(OptimizerUI.InfoRow("Source", entry.Extension.TrimStart('.') + " · " + FormatBytes(entry.FileBytes) + " · " + entry.LengthSeconds.ToString("0.0") + " s"));
            var exported = OptimizerUI.InfoRow("Exported", ExportedText(entry));
            exported.tooltip = ExportedTooltip(entry);
            exported.EnableInClassList("lo-warning", !entry.UsesSourceFile);
            details.Add(exported);
            if (entry.HasTrim)
                details.Add(OptimizerUI.InfoRow("Trim", entry.LunaRule.startTime + "–" + entry.LunaRule.endTime));
            var prediction = entry.Prediction;
            if (prediction != null && prediction.success)
            {
                details.Add(OptimizerUI.InfoRow("Luna output", (prediction.outputExtension ?? ".mp3").TrimStart('.') + " · " + FormatBytes(prediction.outputBytes) + " file"));
                var playable = OptimizerUI.InfoRow("In playable", FormatBytes(entry.PlayableBytes)
                    + (entry.HasPendingChange && entry.PendingFromBytes >= 0 ? "   (was " + FormatBytes(entry.PendingFromBytes) + ", unsaved)" : string.Empty));
                playable.EnableInClassList("lo-pending-cell", entry.HasPendingChange);
                details.Add(playable);
            }
            else
            {
                details.Add(OptimizerUI.InfoRow("Luna output", prediction == null ? "not predicted yet (Predict)" : "prediction failed: " + prediction.error));
            }
            details.Add(OptimizerUI.InfoRow("Last build", entry.HasLastBuild ? FormatBytes(entry.LastBuildBytes) + (entry.LastBuildOutdated ? " (sound changed since)" : string.Empty) : "–"));

            details.Add(OptimizerUI.SectionTitle("Used by"));
            details.Add(UsedBy(entry.AssetPath));

            var exclude = new Button(() => LunaExclusionWindow.AddAndShow(new[] { entry.AssetPath })) { text = "Add to Luna Exclusions…", tooltip = "Review it in the Luna Exclusions window before anything is written." };
            exclude.AddToClassList("lo-details-button");
            details.Add(exclude);
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
                case Column.Length: return (a, b) => a.LengthSeconds.CompareTo(b.LengthSeconds);
                case Column.SourceChannels: return (a, b) => a.Channels.CompareTo(b.Channels);
                case Column.Format: return (a, b) => a.Format.CompareTo(b.Format);
                case Column.Bitrate: return (a, b) => a.LunaRule.bitrate.CompareTo(b.LunaRule.bitrate);
                case Column.Channels: return (a, b) => a.LunaRule.channels.CompareTo(b.LunaRule.channels);
                case Column.Trim: return (a, b) => a.HasTrim.CompareTo(b.HasTrim);
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
