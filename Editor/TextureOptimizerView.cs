using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Textures tab of the Luna Optimizer (UI Toolkit): every texture Luna ships in a sortable table, and a details
    // panel for the selected one (Luna input/output preview, format and size, what uses it). Changes stay in memory
    // and are predicted; the hub's Save all writes them as Luna per-texture overrides.
    [Serializable]
    public class TextureOptimizerView : OptimizerView
    {
        const int MIN_MAX_SIZE = 32;
        const int FULL_SIZE = 0;    // bulk max size: each texture's own largest side
        static readonly int[] BULK_MAX_SIZES = { FULL_SIZE, 2048, 1024, 512, 256, 128, 64, 32 };
        const float ROW_HEIGHT = 24f;
        const float DETAILS_WIDTH = 420f;
        const float PREVIEW_HEIGHT = 300f;
        const string KEEP_FORMAT = "Format: keep";
        const string KEEP_SIZE = "Max size: keep";
        const int DEFAULT_LOSSY_QUALITY = 80;
        const int MIN_QUALITY = 1;
        const int MAX_QUALITY = 100;
        const string WEBP_QUALITY_PREF = "LunaOptimizer.WebpQuality";
        const string JPEG_QUALITY_PREF = "LunaOptimizer.JpegQuality";
        const long QUALITY_COMMIT_DELAY_MS = 350;   // slider: predict once the value rests (and the drag ended)

        enum Column
        {
            Pick,
            Name,
            FileSize,
            UnityCompression,
            LunaRule,
            MaxSize,
            Predicted,
            LastBuild,
        }

        // Formats offered per texture: Luna's best-quality PNG preset, lossy WebP, and JPEG for images without alpha.
        class FormatOption
        {
            public string Name;           // label without the quality
            public string Label;
            public string Format;
            public string Compression;    // null keeps the rule's compression
            public int Quality;           // -1 keeps the rule's quality
            public bool NeedsOpaque;

            public void ApplyTo(LunaTextureRule rule)
            {
                rule.format = Format;
                if (Compression != null)
                    rule.compression = Compression;
                if (Quality >= 0)
                    rule.quality = Quality;
            }

            public static FormatOption Lossy(string name, string format, int quality, bool needsOpaque)
            {
                return new FormatOption { Name = name, Label = name + " q" + quality, Format = format, Quality = quality, NeedsOpaque = needsOpaque };
            }

            public FormatOption WithQuality(int quality)
            {
                return Lossy(Name, Format, quality, NeedsOpaque);
            }

            public bool Matches(LunaTextureRule rule)
            {
                return rule.format == Format && (Compression == null || rule.compression == Compression) && (Quality < 0 || rule.quality == Quality);
            }

            // Luna still writes JPEG for textures Unity imported without alpha, whatever the PNG setting says.
            public string LabelFor(TextureEntry entry)
            {
                var rule = entry.LunaRule.Clone();
                ApplyTo(rule);
                var output = LunaPipeline.OutputExtension(rule, entry.ImportedFormat, entry.AlphaSource).TrimStart('.');
                return output == Format || (Format == PNG_FORMAT && output == "png") ? Label : Label + " (ships as " + output + ")";
            }
        }

        const string PNG_FORMAT = "png32";
        static readonly FormatOption PNG_OPTION =
            new FormatOption { Name = "PNG · best quality", Label = "PNG · best quality", Format = PNG_FORMAT, Compression = "quantQuality", Quality = -1 };

        // Lossy quality chosen in the toolbar, used by Pick smallest, bulk edit and switching a texture to WebP/JPEG.
        int webpQuality = DEFAULT_LOSSY_QUALITY;
        int jpegQuality = DEFAULT_LOSSY_QUALITY;

        List<TextureEntry> entries = new List<TextureEntry>();
        List<TextureEntry> visibleEntries = new List<TextureEntry>();
        LunaSettings settings;
        LunaBuildStats stats;
        LunaInstall install;
        PredictionCache cache;
        string projectRoot;
        string searchText = string.Empty;
        HashSet<string> ticked = new HashSet<string>();   // asset paths ticked for bulk edit
        [SerializeField] string selectedPath;             // texture shown in the details panel
        TexturePreviewPanel preview;
        readonly Dictionary<string, BlastRadius> usedByCache = new Dictionary<string, BlastRadius>();

        // UI Toolkit elements, rebuilt by CreateUI after every reload.
        MultiColumnListView table;
        VisualElement statStrip;
        VisualElement bulkBar;
        VisualElement bulkEdit;
        Label bulkHint;
        SearchBar searchBar;
        double largestPredicted;
        Label summaryLabel;
        Label warningLabel;
        Toggle allShownToggle;
        Label tickedLabel;
        DropdownField bulkFormatField;
        IVisualElementScheduledItem qualityCommit;
        DropdownField bulkSizeField;
        Button bulkApplyButton;
        Button untickButton;
        ToolbarButton predictButton;
        ToolbarButton pickSmallestButton;
        ToolbarMenu sourcesMenu;
        IntegerField webpField;
        IntegerField jpegField;
        VisualElement details;
        IMGUIContainer previewContainer;

        public override string Title
        {
            get { return "Textures"; }
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
            webpQuality = Mathf.Clamp(EditorPrefs.GetInt(WEBP_QUALITY_PREF, DEFAULT_LOSSY_QUALITY), MIN_QUALITY, MAX_QUALITY);
            jpegQuality = Mathf.Clamp(EditorPrefs.GetInt(JPEG_QUALITY_PREF, DEFAULT_LOSSY_QUALITY), MIN_QUALITY, MAX_QUALITY);
            preview = new TexturePreviewPanel(() =>
            {
                if (previewContainer != null)
                    previewContainer.MarkDirtyRepaint();
            });
            Refresh();
        }

        public override void OnDisable()
        {
            ExclusionQueue.Changed -= OnExclusionQueueChanged;
            if (preview != null)
                preview.Dispose();
        }

        void Refresh()
        {
            projectRoot = Path.GetDirectoryName(Application.dataPath);
            settings = LunaSettings.Load(projectRoot);
            stats = LunaBuildStats.Load(projectRoot);
            install = LunaInstall.Find();
            cache = new PredictionCache(projectRoot);
            entries = settings != null ? BuildTextureScanner.Scan(projectRoot, settings, stats) : new List<TextureEntry>();
            foreach (var entry in entries)
                entry.Prediction = cache.Get(PredictionCache.KeyFor(entry, projectRoot, install));
            ticked.IntersectWith(entries.Select(e => e.AssetPath));
            usedByCache.Clear();
            ApplySortAndFilter();
        }

        // Runs Luna's pipeline locally for every texture without an up-to-date prediction.
        void PredictMissing()
        {
            var pending = entries.Where(e => e.Prediction == null).ToList();
            var predictions = LunaPipeline.PredictMany(pending, install, projectRoot, cache, "Predicting Luna output");
            for (int i = 0; i < pending.Count; i++)
            {
                if (predictions[i] == null)
                    continue;
                pending[i].Prediction = predictions[i];
                cache.Set(PredictionCache.KeyFor(pending[i], projectRoot, install), predictions[i]);
            }
            cache.Save();
            ApplySortAndFilter();
        }

        // Evaluates every listed PNG first, then asks before changing any asset.
        void OptimizeSources()
        {
            var targets = visibleEntries.Where(SourceOptimizer.CanOptimize).ToList();
            if (!EditorUtility.DisplayDialog("Optimize sources",
                    "Test compressed versions of " + targets.Count + " PNG source(s) against Luna's predicted output.\n\n"
                    + "Nothing is changed until you confirm the results.", "Evaluate", "Cancel"))
                return;

            var results = SourceOptimizer.EvaluateMany(targets, install, projectRoot, settings, cache);
            cache.Save();
            if (results == null)
                return;

            var improving = results.Where(r => r.Best != null && r.Best.ChangesSomething).ToList();
            var failed = results.Count(r => r.Error != null);
            if (improving.Count == 0)
            {
                EditorUtility.DisplayDialog("Optimize sources", "No source change makes Luna's output smaller." + FailedNote(failed), "OK");
                return;
            }

            var before = improving.Sum(r => r.Candidates[0].Prediction.PlayableBytes);
            var after = improving.Sum(r => r.Best.Prediction.PlayableBytes);
            var details = string.Join("\n", improving.Take(15).Select(r => r.Entry.FileName + ": "
                + FormatBytes(r.Candidates[0].Prediction.PlayableBytes) + " → " + FormatBytes(r.Best.Prediction.PlayableBytes) + "  (" + r.Best.Label + ")"));
            if (improving.Count > 15)
                details += "\n… and " + (improving.Count - 15) + " more";
            if (!EditorUtility.DisplayDialog("Optimize sources",
                    improving.Count + " of " + results.Count + " textures get smaller in the playable: "
                    + FormatBytes(before) + " → " + FormatBytes(after) + "\n\n" + details + FailedNote(failed)
                    + "\n\nApply? Originals are backed up and can be restored.", "Apply", "Cancel"))
                return;

            foreach (var result in improving)
            {
                SourceOptimizer.Apply(result, projectRoot);
                cache.Set(PredictionCache.KeyFor(result.Entry, projectRoot, install), result.Best.Prediction);
            }
            cache.Save();
            Refresh();
        }

        static string FailedNote(int failed)
        {
            return failed > 0 ? "\n\n" + failed + " texture(s) could not be evaluated (see Predicted column)." : string.Empty;
        }

        void RestoreOriginals()
        {
            if (!EditorUtility.DisplayDialog("Restore originals",
                    "Put back every source file and import setting the optimizer changed?", "Restore", "Cancel"))
                return;
            var restored = SourceOptimizer.RestoreAll(projectRoot);
            Refresh();
            EditorUtility.DisplayDialog("Restore originals", restored + " texture(s) restored.", "OK");
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
            warningLabel = new Label();
            warningLabel.AddToClassList("lo-warning");
            warningLabel.AddToClassList("lo-summary");
            root.Add(warningLabel);
            root.Add(CreateBulkBar());
            searchBar.RegisterShortcut(root);

            var split = new TwoPaneSplitView(1, DETAILS_WIDTH, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "lo-textures-split" };
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

        VisualElement CreateToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.AddToClassList("lo-tab-toolbar");
            toolbar.Add(OptimizerUI.IconButton("d_Refresh", "Refresh", "Scan again: textures, Luna settings and the last build.", Refresh));
            predictButton = new ToolbarButton(PredictMissing) { text = "Predict", tooltip = "Run Luna's texture pipeline locally for textures without an up-to-date prediction." };
            toolbar.Add(predictButton);
            toolbar.Add(new ToolbarSpacer());
            pickSmallestButton = new ToolbarButton(() => PickSmallestFormats(visibleEntries)) { text = "Pick smallest format", tooltip = "Predict PNG, WebP and (without alpha) JPEG at the WebP/JPEG quality set next to it for the listed textures and select the smallest. Nothing is saved." };
            toolbar.Add(pickSmallestButton);
            webpField = QualityField("WebP q", webpQuality, WEBP_QUALITY_PREF, q => webpQuality = q);
            toolbar.Add(webpField);
            jpegField = QualityField("JPEG q", jpegQuality, JPEG_QUALITY_PREF, q => jpegQuality = q);
            toolbar.Add(jpegField);
            toolbar.Add(new ToolbarSpacer());
            sourcesMenu = new ToolbarMenu { text = "Source files", tooltip = "Change the PNG files themselves (backed up first)." };
            sourcesMenu.menu.AppendAction("Optimize sources…", _ => EditorApplication.delayCall += OptimizeSources, _ => CanOptimizeSources
                ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            sourcesMenu.menu.AppendAction("Restore originals…", _ => EditorApplication.delayCall += RestoreOriginals,
                _ => projectRoot != null && SourceOptimizer.HasBackups(projectRoot) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            toolbar.Add(sourcesMenu);
            return toolbar;
        }

        bool CanOptimizeSources
        {
            get { return install != null && PendingCount == 0 && visibleEntries.Any(SourceOptimizer.CanOptimize); }
        }

        // Quality (1-100) used when a texture is switched to WebP or JPEG here; remembered across sessions.
        IntegerField QualityField(string label, int value, string pref, Action<int> set)
        {
            var field = new IntegerField(label)
            {
                value = value,
                isDelayed = true,
                tooltip = "Quality (" + MIN_QUALITY + "-" + MAX_QUALITY + ") used by Pick smallest format, bulk edit and when a texture is switched to this format. Textures already in it keep their own quality.",
            };
            field.AddToClassList("lo-quality-field");
            field.RegisterValueChangedCallback(change =>
            {
                var quality = Mathf.Clamp(change.newValue, MIN_QUALITY, MAX_QUALITY);
                field.SetValueWithoutNotify(quality);
                set(quality);
                EditorPrefs.SetInt(pref, quality);
                var index = bulkFormatField.index;
                bulkFormatField.choices = BulkFormatChoices();
                bulkFormatField.index = index;
                RefreshUI();
            });
            return field;
        }

        List<string> BulkFormatChoices()
        {
            return new[] { KEEP_FORMAT }.Concat(FormatOptions().Select(o => o.Label)).ToList();
        }

        VisualElement CreateBulkBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("lo-bulkbar");
            bulkBar = bar;
            searchBar = new SearchBar(searchText, "Search textures by name or path   (Ctrl/Cmd+F)", value =>
            {
                searchText = value;
                ApplySortAndFilter();
            });
            bar.Add(searchBar);
            var gap = new VisualElement();
            gap.AddToClassList("lo-spacer");
            bar.Add(gap);
            // Lives in the tick column's header (see CreateTable).
            allShownToggle = new Toggle { tooltip = "Tick or untick every texture the search shows." };
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
            bulkHint = new Label("Tick textures to change their format or size together.");
            bulkHint.AddToClassList("lo-muted");
            bulkHint.AddToClassList("lo-bulk-hint");
            bar.Add(bulkHint);
            bulkEdit = new VisualElement();
            bulkEdit.AddToClassList("lo-bulk-edit");
            bar.Add(bulkEdit);
            bulkFormatField = new DropdownField(BulkFormatChoices(), 0);
            bulkFormatField.AddToClassList("lo-bulk-field");
            bulkFormatField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bulkEdit.Add(bulkFormatField);
            bulkSizeField = new DropdownField(new[] { KEEP_SIZE }.Concat(BULK_MAX_SIZES.Select(m => m == FULL_SIZE ? "Full size" : "Max " + m)).ToList(), 0);
            bulkSizeField.AddToClassList("lo-bulk-field");
            bulkSizeField.RegisterValueChangedCallback(_ => RefreshBulkBar());
            bulkEdit.Add(bulkSizeField);
            bulkApplyButton = new Button(() =>
            {
                var targets = TickedVisible();
                var formatIndex = bulkFormatField.choices.IndexOf(bulkFormatField.value) - 1;
                var sizeIndex = bulkSizeField.choices.IndexOf(bulkSizeField.value) - 1;
                var option = formatIndex >= 0 ? FormatOptions()[formatIndex] : null;
                var maxSize = sizeIndex >= 0 ? BULK_MAX_SIZES[sizeIndex] : -1;
                EditorApplication.delayCall += () => ApplyBulk(targets, option, maxSize);
            }) { tooltip = "Change the ticked textures; nothing is written until Save all." };
            bulkApplyButton.AddToClassList("lo-primary");
            bulkEdit.Add(bulkApplyButton);
            untickButton = new Button(() =>
            {
                ticked.Clear();
                RefreshUI();
            }) { text = "Clear" };
            bulkEdit.Add(untickButton);
            return bar;
        }

        MultiColumnListView CreateTable()
        {
            table = new MultiColumnListView
            {
                fixedItemHeight = ROW_HEIGHT,
                selectionType = SelectionType.Single,
                sortingEnabled = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                viewDataKey = "lo-textures-table",
            };
            table.AddToClassList("lo-table");
            AddColumn(Column.Pick, string.Empty, 26f, false, () => new Toggle(), (element, entry) =>
            {
                var toggle = (Toggle)element;
                toggle.SetValueWithoutNotify(ticked.Contains(entry.AssetPath));
            }, element => ((Toggle)element).RegisterValueChangedCallback(change =>
            {
                var entry = EntryOf(element);
                if (entry == null)
                    return;
                if (change.newValue) ticked.Add(entry.AssetPath); else ticked.Remove(entry.AssetPath);
                RefreshBulkBar();
            }));
            AddColumn(Column.Name, "Texture", 220f, false, () => new Label(), (element, e) =>
            {
                var label = (Label)element;
                var queued = ExclusionQueue.IsQueued(e.AssetPath);
                label.text = e.FileName + (queued ? "   ⦸ excluding" : string.Empty);
                label.tooltip = e.AssetPath + (queued ? "\nWaiting in the Luna Exclusions window (Pending)." : string.Empty);
                label.EnableInClassList("lo-excluding", queued);
            }, null);
            AddLabelColumn(Column.FileSize, "File", 70f, true, e => FormatBytes(e.FileBytes), null);
            AddLabelColumn(Column.UnityCompression, "Unity compression", 120f, false,
                e => e.UnityCompression + (e.HasPlatformOverride ? " (platform)" : string.Empty),
                e => e.UnityCompressionAffectsLuna ? "Luna re-encodes this texture from Unity's compressed import." : null,
                e => e.UnityCompressionAffectsLuna);
            AddColumn(Column.LunaRule, "Luna format", 170f, true, () => new DropdownField(), (element, entry) => BindFormatField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnFormatPicked((DropdownField)element, change.newValue)));
            AddColumn(Column.MaxSize, "Max size", 140f, true, () => new DropdownField(), (element, entry) => BindSizeField((DropdownField)element, entry),
                element => ((DropdownField)element).RegisterValueChangedCallback(change => OnSizePicked((DropdownField)element, change.newValue)));
            AddColumn(Column.Predicted, "Predicted", 130f, true, OptimizerUI.SizeCell, (element, entry) =>
            {
                BindPrediction((Label)element[1], entry);
                OptimizerUI.SetHeat(element, PredictedBytes(entry), largestPredicted);
            }, null);
            AddLabelColumn(Column.LastBuild, "Last build", 90f, true,
                e => e.HasLastBuild ? FormatBytes(e.LastBuildBytes) + (e.LastBuildOutdated ? " (old)" : string.Empty) : "–",
                e => e.LastBuildOutdated ? "The texture changed after the last Luna build." : null);

            table.sortColumnDescriptions.Add(new SortColumnDescription(Column.LastBuild.ToString(), SortDirection.Descending));
            table.columnSortingChanged += ApplySortAndFilter;
            table.selectionChanged += selection =>
            {
                var entry = selection.OfType<TextureEntry>().FirstOrDefault();
                selectedPath = entry != null ? entry.AssetPath : null;
                RefreshDetails();
            };
            table.itemsChosen += chosen =>
            {
                var entry = chosen.OfType<TextureEntry>().FirstOrDefault();
                if (entry != null)
                    OptimizerUI.Ping(entry.AssetPath);
            };
            return table;
        }

        // Every cell sits in a container that remembers its row's entry, for callbacks and the right-click menu.
        void AddColumn(Column column, string title, float width, bool rightAligned, Func<VisualElement> make,
            Action<VisualElement, TextureEntry> bind, Action<VisualElement> registerOnce)
        {
            table.columns.Add(new UnityEngine.UIElements.Column
            {
                name = column.ToString(),
                makeHeader = column == Column.Pick ? () => allShownToggle : (Func<VisualElement>)null,
                title = title,
                width = width,
                minWidth = Mathf.Min(width, 40f),
                sortable = column != Column.Pick,
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

        void AddLabelColumn(Column column, string title, float width, bool rightAligned, Func<TextureEntry, string> text,
            Func<TextureEntry, string> tooltip, Func<TextureEntry, bool> warn = null)
        {
            AddColumn(column, title, width, rightAligned, () => new Label(), (element, entry) =>
            {
                var label = (Label)element;
                label.text = text(entry);
                label.tooltip = tooltip != null ? tooltip(entry) : null;
                label.EnableInClassList("lo-warning", warn != null && warn(entry));
            }, null);
        }

        static TextureEntry EntryOf(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                var entry = current.userData as TextureEntry;
                if (entry != null)
                    return entry;
            }
            return null;
        }

        void BuildRowMenu(ContextualMenuPopulateEvent evt, TextureEntry entry)
        {
            if (entry == null)
                return;
            var path = entry.AssetPath;
            evt.menu.AppendAction("Add to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(new[] { path }));
            var tickedPaths = TickedVisible().Select(e => e.AssetPath).ToList();
            if (tickedPaths.Count > 1)
                evt.menu.AppendAction("Add " + tickedPaths.Count + " ticked textures to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(tickedPaths));
            evt.menu.AppendAction("Show in Project", _ => OptimizerUI.Ping(path));
        }

        // Formats this tool recommends; a rule set some other way stays shown as is.
        void BindFormatField(DropdownField field, TextureEntry entry)
        {
            var options = ShownOptionsFor(entry);
            var labels = options.Select(o => o.LabelFor(entry)).ToList();
            var selected = options.FindIndex(o => o.Matches(entry.LunaRule));
            if (selected < 0)
            {
                labels.Add(DescribeRule(entry));
                selected = labels.Count - 1;
            }
            field.choices = labels;
            field.SetValueWithoutNotify(labels[selected]);
            field.tooltip = entry.IsLunaOverride ? "Per-texture override in Luna" : "Luna's default rule";
        }

        void OnFormatPicked(DropdownField field, string label)
        {
            var entry = EntryOf(field);
            if (entry == null)
                return;
            var options = ShownOptionsFor(entry);
            var option = options.FirstOrDefault(o => o.LabelFor(entry) == label);
            if (option != null && !option.Matches(entry.LunaRule))
                EditorApplication.delayCall += () => SetFormat(entry, option);
        }

        // Box sizes Luna can shrink to; each choice shows the pixel size Luna will output.
        List<KeyValuePair<int, string>> SizeChoices(TextureEntry entry, out int selected)
        {
            var input = entry.LunaInputSize;
            var largestSide = Mathf.Max(input.x, input.y);
            var sizes = new List<int> { largestSide };
            for (var size = Mathf.NextPowerOfTwo(largestSide) / 2; size >= MIN_MAX_SIZE; size /= 2)
            {
                if (size < largestSide)
                    sizes.Add(size);
            }
            var rule = entry.LunaRule;
            var current = entry.OutputSizeFor(rule.maxWidth, rule.maxHeight);
            var choices = sizes.Select((s, i) => new KeyValuePair<int, string>(s, (i == 0 ? "Full " : s + " → ") + FormatPixels(entry.OutputSizeFor(s, s)))).ToList();
            selected = sizes.FindIndex(s => entry.OutputSizeFor(s, s) == current);
            if (selected < 0)
            {
                // A non-square or unusual box set in the Luna window: keep it shown as is.
                choices.Add(new KeyValuePair<int, string>(-1, rule.maxWidth + "×" + rule.maxHeight + " → " + FormatPixels(current)));
                selected = choices.Count - 1;
            }
            return choices;
        }

        void BindSizeField(DropdownField field, TextureEntry entry)
        {
            int selected;
            var choices = SizeChoices(entry, out selected);
            field.choices = choices.Select(c => c.Value).ToList();
            field.SetValueWithoutNotify(choices[selected].Value);
        }

        void OnSizePicked(DropdownField field, string label)
        {
            var entry = EntryOf(field);
            if (entry == null)
                return;
            int selected;
            var choice = SizeChoices(entry, out selected).FirstOrDefault(c => c.Value == label);
            if (choice.Value != null && choice.Key > 0 && choice.Value != SizeChoices(entry, out selected)[selected].Value)
            {
                var size = choice.Key;
                EditorApplication.delayCall += () => SetMaxSize(entry, size);
            }
        }

        void BindPrediction(Label label, TextureEntry entry)
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
            var tooltip = "Luna output: " + prediction.outputExtension + ", " + FormatBytes(prediction.outputBytes) + " file\n"
                + "Luna input: " + (prediction.usedSourceFile ? "your source file" : "Unity's imported texture") + ", " + FormatBytes(prediction.inputBytes);
            label.text = FormatBytes(prediction.PlayableBytes);
            if (entry.HasPendingChange && entry.PendingFromBytes >= 0)
                label.text += "  " + FormatDelta(prediction.PlayableBytes - entry.PendingFromBytes);
            if (entry.HasPendingChange)
            {
                tooltip = "Unsaved change" + (entry.PendingFromBytes >= 0 ? ", was " + FormatBytes(entry.PendingFromBytes) : string.Empty) + "\n" + tooltip;
                label.AddToClassList("lo-pending-cell");
            }
            label.tooltip = tooltip;
        }

        List<TextureEntry> TickedVisible()
        {
            return visibleEntries.Where(e => ticked.Contains(e.AssetPath)).ToList();
        }

        // Everything that follows the data: table rows, summary, bulk bar, toolbar states, details panel.
        void RefreshUI()
        {
            if (table == null)
                return;
            largestPredicted = visibleEntries.Count > 0 ? visibleEntries.Max(e => PredictedBytes(e)) : 0;
            table.itemsSource = visibleEntries;
            table.RefreshItems();
            var index = selectedPath != null ? visibleEntries.FindIndex(e => e.AssetPath == selectedPath) : -1;
            if (index >= 0)
                table.SetSelectionWithoutNotify(new[] { index });
            else
                table.ClearSelection();

            RefreshStats();
            var affected = entries.Count(e => e.UnityCompressionAffectsLuna);
            warningLabel.text = "⚠  " + affected + " textures reach Luna through Unity's compressed import (orange). Setting their Unity Compression to None usually makes Luna's output smaller.";
            warningLabel.style.display = affected > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            predictButton.SetEnabled(install != null && entries.Any(e => e.Prediction == null));
            pickSmallestButton.SetEnabled(install != null && visibleEntries.Count > 0);
            RefreshBulkBar();
            RefreshDetails();
        }

        void RefreshStats()
        {
            statStrip.Clear();
            string problem = null;
            if (settings == null)
                problem = LunaSettings.FILE_NAME + " not found in the project root. Open the Luna window once to create it.";
            else if (install == null)
                problem = "No Luna install found, so sizes cannot be predicted.";
            summaryLabel.text = problem ?? string.Empty;
            summaryLabel.style.display = problem != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (settings == null)
                return;

            OptimizerUI.AddStat(statStrip, entries.Count.ToString(), "textures");
            OptimizerUI.AddStat(statStrip, FormatBytes(entries.Sum(e => e.FileBytes)), "source files");
            var predicted = entries.Where(e => e.HasPrediction).ToList();
            if (predicted.Count > 0)
                OptimizerUI.AddStat(statStrip, FormatBytes(predicted.Sum(e => e.Prediction.PlayableBytes)),
                    "predicted in playable" + (predicted.Count < entries.Count ? " (" + predicted.Count + "/" + entries.Count + ")" : string.Empty),
                    "What Luna would ship for these textures with the current (unsaved included) settings.", "lo-stat--key");
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
            tickedLabel.tooltip = hidden > 0 ? "Ticked textures the search hides are not changed." : null;
            var anyChange = bulkFormatField.value != KEEP_FORMAT || bulkSizeField.value != KEEP_SIZE;
            bulkApplyButton.text = "Apply to " + targets.Count;
            bulkApplyButton.SetEnabled(targets.Count > 0 && anyChange);
            untickButton.SetEnabled(ticked.Count > 0);
        }

        // ---------------------------------------------------------------- details panel

        TextureEntry SelectedEntry
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
                var empty = new Label("Select a texture to see Luna's output, change its format and size, and see what uses it.");
                empty.AddToClassList("lo-empty");
                details.Add(empty);
                return;
            }

            var badges = new List<Label>
            {
                entry.IsLunaOverride ? OptimizerUI.Badge("Override", null, "Per-texture override in Luna") : OptimizerUI.Badge("Default rule", null, "Uses Luna's default texture rule"),
                OptimizerUI.Badge(entry.LunaOutputFormat.ToUpperInvariant(), null, "Format Luna ships"),
            };
            if (entry.HasPendingChange)
                badges.Add(OptimizerUI.Badge("Unsaved", "accent"));
            if (entry.UnityCompressionAffectsLuna)
                badges.Add(OptimizerUI.Badge("Compressed import", "warn", "Luna re-encodes this texture from Unity's compressed import."));
            if (ExclusionQueue.IsQueued(entry.AssetPath))
                badges.Add(OptimizerUI.Badge("Excluding", "warn", "Waiting in the Luna Exclusions window (Pending)."));
            details.Add(OptimizerUI.DetailsHeader(entry.FileName, entry.AssetPath, badges));

            previewContainer = new IMGUIContainer(() =>
            {
                var rect = previewContainer.contentRect;
                preview.Draw(new Rect(0f, 0f, rect.width, rect.height), SelectedEntry, install, projectRoot);
            });
            previewContainer.AddToClassList("lo-preview");
            previewContainer.style.height = PREVIEW_HEIGHT;
            details.Add(previewContainer);

            var shown = entry.Prediction;
            if (shown != null && shown.success)
                details.Add(OptimizerUI.Metric(FormatBytes(shown.PlayableBytes), "in playable",
                    entry.HasPendingChange && entry.PendingFromBytes >= 0 ? FormatDelta(shown.PlayableBytes - entry.PendingFromBytes) + " unsaved" : null,
                    shown.PlayableBytes <= entry.PendingFromBytes));

            details.Add(OptimizerUI.SectionTitle("Luna settings"));
            var format = new DropdownField("Format");
            BindFormatField(format, entry);
            format.userData = entry;
            format.RegisterValueChangedCallback(change => OnFormatPicked(format, change.newValue));
            details.Add(format);
            var size = new DropdownField("Max size");
            BindSizeField(size, entry);
            size.userData = entry;
            size.RegisterValueChangedCallback(change => OnSizePicked(size, change.newValue));
            details.Add(size);
            if (entry.LunaOutputFormat != "png")
                details.Add(QualitySlider(entry));

            details.Add(OptimizerUI.SectionTitle("Size"));
            details.Add(OptimizerUI.InfoRow("Source", entry.Width + "×" + entry.Height + " · " + FormatBytes(entry.FileBytes)));
            var import = OptimizerUI.InfoRow("Unity import", entry.UnityCompression + (entry.HasPlatformOverride ? " (platform)" : string.Empty));
            import.EnableInClassList("lo-warning", entry.UnityCompressionAffectsLuna);
            import.tooltip = entry.UnityCompressionAffectsLuna ? "Luna re-encodes this texture from Unity's compressed import; Compression None usually makes Luna's output smaller." : null;
            details.Add(import);
            var prediction = entry.Prediction;
            if (prediction != null && prediction.success)
            {
                var output = entry.OutputSizeFor(entry.LunaRule.maxWidth, entry.LunaRule.maxHeight);
                details.Add(OptimizerUI.InfoRow("Luna output", prediction.outputExtension.TrimStart('.') + " · " + FormatPixels(output) + " · " + FormatBytes(prediction.outputBytes) + " file"));
                if (entry.HasPendingChange && entry.PendingFromBytes >= 0)
                    details.Add(OptimizerUI.InfoRow("Saved setting", FormatBytes(entry.PendingFromBytes) + " in playable"));
            }
            else
            {
                details.Add(OptimizerUI.InfoRow("Luna output", prediction == null ? "not predicted yet (Predict)" : "prediction failed: " + prediction.error));
            }
            details.Add(OptimizerUI.InfoRow("Last build", entry.HasLastBuild ? FormatBytes(entry.LastBuildBytes) + (entry.LastBuildOutdated ? " (texture changed since)" : string.Empty) : "–"));

            details.Add(OptimizerUI.SectionTitle("Used by"));
            details.Add(UsedBy(entry));

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

        // WebP/JPEG quality of the selected texture. Predicted (and the preview re-rendered) once the value rests
        // and the drag has ended, so dragging stays smooth; nothing is written until Save all.
        SliderInt QualitySlider(TextureEntry entry)
        {
            var slider = new SliderInt("Quality", MIN_QUALITY, MAX_QUALITY)
            {
                value = entry.LunaRule.quality,
                showInputField = true,
                tooltip = "Luna's " + entry.LunaOutputFormat.ToUpperInvariant() + " quality for this texture. Lower is smaller, with more visible artifacts.",
            };
            slider.RegisterValueChangedCallback(change =>
            {
                var quality = change.newValue;
                if (qualityCommit != null)
                    qualityCommit.Pause();
                qualityCommit = slider.schedule.Execute(() => CommitQuality(slider, entry, quality));
                qualityCommit.ExecuteLater(QUALITY_COMMIT_DELAY_MS);
            });
            return slider;
        }

        void CommitQuality(SliderInt slider, TextureEntry entry, int quality)
        {
            // Still dragging: try again shortly (re-predicting rebuilds this panel and would end the drag).
            if (slider.panel != null && slider.panel.GetCapturingElement(PointerId.mousePointerId) != null)
            {
                qualityCommit = slider.schedule.Execute(() => CommitQuality(slider, entry, quality));
                qualityCommit.ExecuteLater(QUALITY_COMMIT_DELAY_MS);
                return;
            }
            if (entry.LunaRule.quality != quality)
                ChangeRule(entry, rule => rule.quality = quality);
        }

        // The chain from this texture up to the scene that ships it (BlastRadius), computed once per texture.
        VisualElement UsedBy(TextureEntry entry)
        {
            BlastRadius result;
            if (!usedByCache.TryGetValue(entry.AssetPath, out result))
            {
                result = settings != null ? BlastRadius.Analyze(new[] { entry.AssetPath }, settings) : null;
                usedByCache[entry.AssetPath] = result;
            }
            return OptimizerUI.UsedBy(result);
        }

        void SetMaxSize(TextureEntry entry, int size)
        {
            ChangeRule(entry, rule =>
            {
                rule.maxWidth = size;
                rule.maxHeight = size;
            });
        }

        void SetFormat(TextureEntry entry, FormatOption option)
        {
            ChangeRule(entry, option.ApplyTo);
        }

        // Keeps the change in memory and predicts it; nothing is written until Save to Luna.
        void ChangeRule(TextureEntry entry, Action<LunaTextureRule> change)
        {
            ChangeRules(new List<TextureEntry> { entry }, (e, rule) => change(rule));
        }

        void ChangeRules(IList<TextureEntry> targets, Action<TextureEntry, LunaTextureRule> change)
        {
            foreach (var entry in targets)
            {
                var saved = entry.SavedRule ?? entry.LunaRule;
                var rule = entry.LunaRule.Clone();
                change(entry, rule);
                if (SameResult(entry, rule, saved))
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
            if (missing.Count > 0 && install != null)
            {
                var predictions = LunaPipeline.PredictMany(missing, install, projectRoot, cache, missing.Count > 1 ? "Predicting Luna output" : null);
                for (int i = 0; i < missing.Count; i++)
                {
                    missing[i].Prediction = predictions[i];
                    if (predictions[i] != null)
                        cache.Set(PredictionCache.KeyFor(missing[i], projectRoot, install), predictions[i]);
                }
            }
            foreach (var entry in targets)
                entry.PendingFromBytes = entry.HasPendingChange ? PredictedBytesFor(entry, entry.SavedRule) : -1;
            cache.Save();
            ApplySortAndFilter();
        }

        static bool SameResult(TextureEntry entry, LunaTextureRule a, LunaTextureRule b)
        {
            return entry.OutputSizeFor(a.maxWidth, a.maxHeight) == entry.OutputSizeFor(b.maxWidth, b.maxHeight)
                && a.format == b.format && a.compression == b.compression && a.quality == b.quality;
        }

        // maxSize: -1 keeps, FULL_SIZE uses each texture's largest side. JPEG skips textures with alpha.
        void ApplyBulk(List<TextureEntry> targets, FormatOption option, int maxSize)
        {
            var note = ApplyBulkCore(targets, option, maxSize);
            if (note != null)
                EditorUtility.DisplayDialog("Apply to ticked textures", note, "OK");
        }

        // Returns what could not be applied (null when everything was).
        string ApplyBulkCore(List<TextureEntry> targets, FormatOption option, int maxSize)
        {
            var formatKept = new List<string>();
            ChangeRules(targets, (entry, rule) =>
            {
                if (option != null)
                {
                    if (!option.NeedsOpaque || !entry.HasAlpha)
                        option.ApplyTo(rule);
                    else
                        formatKept.Add(entry.FileName);
                }
                if (maxSize >= 0)
                {
                    var input = entry.LunaInputSize;
                    var size = maxSize == FULL_SIZE ? Mathf.Max(input.x, input.y) : maxSize;
                    rule.maxWidth = size;
                    rule.maxHeight = size;
                }
            });
            return formatKept.Count > 0
                ? option.Label + " needs a texture without alpha; format kept for: " + LunaSaveHistory.Names(formatKept)
                : null;
        }

        // Predicts every allowed format for the targets in one parallel pass and selects the smallest;
        // still only a preview. Returns how many textures changed format.
        int PickSmallestFormats(List<TextureEntry> targets)
        {
            if (install == null)
                return 0;
            targets = targets.ToList();
            var variants = new List<KeyValuePair<TextureEntry, FormatOption>>();
            var toPredict = new List<TextureEntry>();
            foreach (var entry in targets)
            {
                foreach (var option in FormatOptionsFor(entry))
                {
                    var variant = entry.Clone();
                    variant.LunaRule = entry.LunaRule.Clone();
                    option.ApplyTo(variant.LunaRule);
                    var cached = cache.Get(PredictionCache.KeyFor(variant, projectRoot, install));
                    variant.Prediction = cached;
                    if (cached == null)
                        toPredict.Add(variant);
                    variants.Add(new KeyValuePair<TextureEntry, FormatOption>(variant, option));
                }
            }

            var predictions = LunaPipeline.PredictMany(toPredict, install, projectRoot, cache, "Comparing formats");
            for (int i = 0; i < toPredict.Count; i++)
            {
                toPredict[i].Prediction = predictions[i];
                if (predictions[i] != null)
                    cache.Set(PredictionCache.KeyFor(toPredict[i], projectRoot, install), predictions[i]);
            }

            var changed = 0;
            foreach (var entry in targets)
            {
                var best = variants.Where(v => v.Key.AssetPath == entry.AssetPath && v.Key.HasPrediction)
                    .OrderBy(v => v.Key.Prediction.PlayableBytes).Select(v => v.Value).FirstOrDefault();
                if (best != null && !best.Matches(entry.LunaRule))
                {
                    SetFormat(entry, best);
                    changed++;
                }
            }
            cache.Save();
            ApplySortAndFilter();
            ShowNotification(new GUIContent(changed == 0 ? "Every listed texture already uses its smallest format" : changed + " format change(s) to review"));
            return changed;
        }

        // ---------------------------------------------------------------- for the browser page (LunaOptimizerWebApi)

        public LunaSettings Settings
        {
            get { return settings; }
        }

        public LunaInstall Install
        {
            get { return install; }
        }

        public bool HasBuildStats
        {
            get { return stats != null; }
        }

        public TextureEntry Find(string assetPath)
        {
            return entries.FirstOrDefault(e => e.AssetPath == assetPath);
        }

        // The format choices the row's dropdown shows, and which one is selected.
        public List<string> FormatChoices(TextureEntry entry, out int selected)
        {
            var options = ShownOptionsFor(entry);
            var labels = options.Select(o => o.LabelFor(entry)).ToList();
            selected = options.FindIndex(o => o.Matches(entry.LunaRule));
            if (selected < 0)
            {
                labels.Add(DescribeRule(entry));
                selected = labels.Count - 1;
            }
            return labels;
        }

        public bool PickFormat(TextureEntry entry, string label)
        {
            var option = ShownOptionsFor(entry).FirstOrDefault(o => o.LabelFor(entry) == label);
            if (option == null || option.Matches(entry.LunaRule))
                return false;
            SetFormat(entry, option);
            return true;
        }

        public List<string> SizeLabels(TextureEntry entry, out int selected)
        {
            return SizeChoices(entry, out selected).Select(c => c.Value).ToList();
        }

        public bool PickSize(TextureEntry entry, string label)
        {
            int selected;
            var choices = SizeChoices(entry, out selected);
            var choice = choices.FirstOrDefault(c => c.Value == label);
            if (choice.Value == null || choice.Key <= 0 || label == choices[selected].Value)
                return false;
            SetMaxSize(entry, choice.Key);
            return true;
        }

        public void SetQuality(TextureEntry entry, int quality)
        {
            quality = Mathf.Clamp(quality, MIN_QUALITY, MAX_QUALITY);
            if (entry.LunaOutputFormat != "png" && entry.LunaRule.quality != quality)
                ChangeRule(entry, rule => rule.quality = quality);
        }

        public List<string> BulkFormatLabels()
        {
            return BulkFormatChoices();
        }

        public List<string> BulkSizeLabels()
        {
            return new[] { KEEP_SIZE }.Concat(BULK_MAX_SIZES.Select(m => m == FULL_SIZE ? "Full size" : "Max " + m)).ToList();
        }

        // Indices into BulkFormatLabels / BulkSizeLabels (0 = keep). Returns a note when something was kept.
        public string ApplyBulk(IEnumerable<string> paths, int formatIndex, int sizeIndex)
        {
            var targets = paths.Select(Find).Where(e => e != null).ToList();
            var option = formatIndex > 0 ? FormatOptions()[formatIndex - 1] : null;
            var maxSize = sizeIndex > 0 ? BULK_MAX_SIZES[sizeIndex - 1] : -1;
            if (targets.Count == 0 || (option == null && maxSize < 0))
                return null;
            return ApplyBulkCore(targets, option, maxSize);
        }

        public int PickSmallest(IEnumerable<string> paths)
        {
            return PickSmallestFormats(paths.Select(Find).Where(e => e != null).ToList());
        }

        // Luna's input and output for the entry's current rule as PNG files (rendered once, then cached on disk).
        public bool RenderPreview(TextureEntry entry, out string inputPng, out string outputPng, out string error)
        {
            inputPng = outputPng = null;
            if (install == null)
            {
                error = "No Luna install found, so Luna's output cannot be rendered.";
                return false;
            }
            return LunaPipeline.RenderPreview(entry, install, projectRoot, PredictionCache.KeyFor(entry, projectRoot, install), out inputPng, out outputPng, out error);
        }

        public bool CanOptimizeSourcesNow
        {
            get { return install != null && PendingCount == 0 && entries.Any(SourceOptimizer.CanOptimize); }
        }

        public bool HasSourceBackups
        {
            get { return projectRoot != null && SourceOptimizer.HasBackups(projectRoot); }
        }

        // Both ask for confirmation in Unity (they change the source files).
        public void OptimizeSourcesInUnity()
        {
            var shown = visibleEntries;
            visibleEntries = entries;
            try
            {
                OptimizeSources();
            }
            finally
            {
                visibleEntries = shown;
                ApplySortAndFilter();
            }
        }

        public void RestoreOriginalsInUnity()
        {
            RestoreOriginals();
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
            get { return entries.Where(e => e.HasPrediction).Sum(e => e.Prediction.PlayableBytes); }
        }

        // Predicted bytes the unsaved changes save (negative when they add).
        public double PendingSavingBytes
        {
            get { return entries.Where(e => e.HasPendingChange && e.HasPrediction && e.PendingFromBytes >= 0).Sum(e => e.PendingFromBytes - e.Prediction.PlayableBytes); }
        }

        public IReadOnlyList<TextureEntry> Entries
        {
            get { return entries; }
        }

        public int UnityCompressedCount
        {
            get { return entries.Count(e => e.UnityCompressionAffectsLuna); }
        }

        public bool CanPredictSizes
        {
            get { return install != null && entries.Any(e => e.Prediction == null); }
        }

        public bool HasLunaInstall
        {
            get { return install != null; }
        }

        public void PredictAll()
        {
            if (install != null)
                PredictMissing();
        }

        // Every texture at these WebP / JPEG qualities (remembered as the toolbar's qualities).
        public int PickSmallestAll(int webp, int jpeg)
        {
            SetLossyQualities(webp, jpeg);
            return PickSmallestFormats(entries);
        }

        public int WebpQuality
        {
            get { return webpQuality; }
        }

        public int JpegQuality
        {
            get { return jpegQuality; }
        }

        public void SetLossyQualities(int webp, int jpeg)
        {
            webpQuality = Mathf.Clamp(webp, MIN_QUALITY, MAX_QUALITY);
            jpegQuality = Mathf.Clamp(jpeg, MIN_QUALITY, MAX_QUALITY);
            EditorPrefs.SetInt(WEBP_QUALITY_PREF, webpQuality);
            EditorPrefs.SetInt(JPEG_QUALITY_PREF, jpegQuality);
            if (webpField != null)
                webpField.SetValueWithoutNotify(webpQuality);
            if (jpegField != null)
                jpegField.SetValueWithoutNotify(jpegQuality);
            if (bulkFormatField != null)
            {
                var index = bulkFormatField.index;
                bulkFormatField.choices = BulkFormatChoices();
                bulkFormatField.index = index;
            }
        }

        double PredictedBytesFor(TextureEntry entry, LunaTextureRule rule)
        {
            var current = entry.LunaRule;
            entry.LunaRule = rule;
            var prediction = cache.Get(PredictionCache.KeyFor(entry, projectRoot, install));
            entry.LunaRule = current;
            return prediction != null && prediction.success ? prediction.PlayableBytes : -1;
        }

        void SaveRules()
        {
            var pending = entries.Where(e => e.HasPendingChange).ToList();
            string error;
            if (!LunaSettingsWriter.SaveRules(pending, projectRoot, out error))
            {
                EditorUtility.DisplayDialog("Save to Luna", "Nothing was saved.\n\n" + error, "OK");
                return;
            }
            Refresh();
        }

        List<FormatOption> FormatOptions()
        {
            return new List<FormatOption>
            {
                PNG_OPTION,
                FormatOption.Lossy("WebP", "webp", webpQuality, false),
                FormatOption.Lossy("JPEG", "jpeg", jpegQuality, true),
            };
        }

        List<FormatOption> FormatOptionsFor(TextureEntry entry)
        {
            return FormatOptions().Where(o => !o.NeedsOpaque || !entry.HasAlpha).ToList();
        }

        // A texture already in WebP/JPEG shows its own quality; picking another format uses the toolbar's quality.
        List<FormatOption> ShownOptionsFor(TextureEntry entry)
        {
            var rule = entry.LunaRule;
            return FormatOptionsFor(entry)
                .Select(o => o.Quality >= 0 && o.Format == rule.format && o.Quality != rule.quality ? o.WithQuality(rule.quality) : o)
                .ToList();
        }

        static string FormatPixels(Vector2Int size)
        {
            return size.x + "×" + size.y;
        }

        static string DescribeRule(TextureEntry entry)
        {
            var rule = entry.LunaRule;
            var output = entry.LunaOutputFormat;
            var text = output == "png" ? rule.format + " · " + rule.compression : output + " q" + rule.quality;
            if (output == "jpeg" && rule.format != "jpeg")
                text += " (no alpha)";
            return (entry.IsLunaOverride ? string.Empty : "default: ") + text;
        }

        void ApplySortAndFilter()
        {
            visibleEntries = searchText.Length == 0
                ? new List<TextureEntry>(entries)
                : entries.Where(e => e.AssetPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var sort = table != null ? table.sortedColumns.FirstOrDefault() : null;
            Column column;
            if (sort != null && Enum.TryParse(sort.columnName, out column) && column != Column.Pick)
            {
                var comparison = ComparisonFor(column);
                var ascending = sort.direction == SortDirection.Ascending;
                visibleEntries.Sort((a, b) => ascending ? comparison(a, b) : comparison(b, a));
            }
            else if (table == null)
            {
                var comparison = ComparisonFor(Column.LastBuild);
                visibleEntries.Sort((a, b) => comparison(b, a));
            }
            RefreshUI();
        }

        static Comparison<TextureEntry> ComparisonFor(Column column)
        {
            switch (column)
            {
                case Column.FileSize: return (a, b) => a.FileBytes.CompareTo(b.FileBytes);
                case Column.UnityCompression: return (a, b) => a.UnityCompression.CompareTo(b.UnityCompression);
                case Column.LunaRule: return (a, b) => string.CompareOrdinal(DescribeRule(a), DescribeRule(b));
                case Column.MaxSize: return (a, b) => OutputPixels(a).CompareTo(OutputPixels(b));
                case Column.Predicted: return (a, b) => PredictedBytes(a).CompareTo(PredictedBytes(b));
                case Column.LastBuild: return (a, b) => a.LastBuildBytes.CompareTo(b.LastBuildBytes);
                default: return (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        static long OutputPixels(TextureEntry entry)
        {
            var size = entry.OutputSizeFor(entry.LunaRule.maxWidth, entry.LunaRule.maxHeight);
            return (long)size.x * size.y;
        }

        static double PredictedBytes(TextureEntry entry)
        {
            return entry.HasPrediction ? entry.Prediction.PlayableBytes : -1;
        }

        // "−1.2 KB" / "+0.4 KB": change against the saved rule.
        static string FormatDelta(double bytes)
        {
            return (bytes <= 0 ? "−" : "+") + FormatBytes(Math.Abs(bytes));
        }

        static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}

