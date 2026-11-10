using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // The hub's Simple mode: the playable size against its cap, what ships per asset type, and one-click actions
    // (predict, shrink texture formats, trim silence, mono sounds). Every action stays unsaved until Save all, like
    // in Manual mode, whose tabs it drives. Labels follow the tabs' data through Refresh (called by the hub).
    public class SimpleModeView
    {
        const string PRESET_PREF = "LunaOptimizer.SimplePreset";
        const float WARN_RATIO = 0.85f;

        // Lossy qualities Shrink formats tries for WebP and (textures without alpha) JPEG.
        class Preset
        {
            public string Name;
            public int Webp;
            public int Jpeg;
            public string Hint;
        }

        static readonly Preset[] PRESETS =
        {
            new Preset { Name = "Balanced", Webp = 80, Jpeg = 80, Hint = "WebP / JPEG at quality 80: hard to tell from the source." },
            new Preset { Name = "Smaller", Webp = 70, Jpeg = 65, Hint = "WebP 70 / JPEG 65: small artifacts on gradients and edges." },
            new Preset { Name = "Smallest", Webp = 55, Jpeg = 50, Hint = "WebP 55 / JPEG 50: visible artifacts; check the textures that matter." },
        };

        // For the browser page: the presets Shrink texture formats offers, and the one chosen.
        public static string[] PresetNames
        {
            get { return PRESETS.Select(p => p.Name).ToArray(); }
        }

        public static int ChosenPreset
        {
            get { return Mathf.Clamp(EditorPrefs.GetInt(PRESET_PREF, 0), 0, PRESETS.Length - 1); }
            set { EditorPrefs.SetInt(PRESET_PREF, Mathf.Clamp(value, 0, PRESETS.Length - 1)); }
        }

        public static void PresetQualities(int index, out int webp, out int jpeg, out string hint)
        {
            var preset = PRESETS[Mathf.Clamp(index, 0, PRESETS.Length - 1)];
            webp = preset.Webp;
            jpeg = preset.Jpeg;
            hint = preset.Hint;
        }

        readonly SizeDashboardView overview;
        readonly TextureOptimizerView textures;
        readonly SoundOptimizerView sounds;
        readonly Action<LunaOptimizerWindow.Tab> openTab;
        readonly Action saveAll;
        readonly Action discardAll;
        readonly Action<string> notify;

        Label totalLabel;
        Label limitLabel;
        VisualElement meter;
        VisualElement meterFill;
        Label meterLabel;
        Label freshnessLabel;
        Label problemLabel;
        Button measureButton;
        VisualElement categoryList;
        string categoryKey;
        Label textureStats;
        Label textureNote;
        DropdownField presetField;
        Label presetHint;
        Button shrinkButton;
        Button texturePredictButton;
        Label soundStats;
        Button trimButton;
        Button monoButton;
        Button soundPredictButton;
        VisualElement pendingCard;
        Label pendingLabel;

        public SimpleModeView(SizeDashboardView overview, TextureOptimizerView textures, SoundOptimizerView sounds,
            Action<LunaOptimizerWindow.Tab> openTab, Action saveAll, Action discardAll, Action<string> notify)
        {
            this.overview = overview;
            this.textures = textures;
            this.sounds = sounds;
            this.openTab = openTab;
            this.saveAll = saveAll;
            this.discardAll = discardAll;
            this.notify = notify;
        }

        public VisualElement CreateUI()
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("lo-simple");
            scroll.contentContainer.AddToClassList("lo-simple-content");
            scroll.Add(CreatePendingCard());
            scroll.Add(CreateSizeCard());
            var actions = new VisualElement();
            actions.AddToClassList("lo-card-row");
            actions.Add(CreateTextureCard());
            actions.Add(CreateSoundCard());
            scroll.Add(actions);
            scroll.Add(CreateCategoryCard());
            categoryKey = null;
            Refresh();
            return scroll;
        }

        static VisualElement Card(string title, Texture icon)
        {
            var card = new VisualElement();
            card.AddToClassList("lo-card");
            var head = new VisualElement();
            head.AddToClassList("lo-card-head");
            if (icon != null)
            {
                var image = new Image { image = icon };
                image.AddToClassList("lo-card-icon");
                head.Add(image);
            }
            var label = new Label(title);
            label.AddToClassList("lo-card-title");
            head.Add(label);
            card.Add(head);
            return card;
        }

        static Button ActionButton(string text, string tooltip, Action action, bool primary = false)
        {
            // Deferred: the actions open progress bars and dialogs.
            var button = new Button(() => EditorApplication.delayCall += () => action()) { text = text, tooltip = tooltip };
            button.AddToClassList("lo-action");
            if (primary)
                button.AddToClassList("lo-primary");
            return button;
        }

        static Button LinkButton(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("lo-link-button");
            return button;
        }

        VisualElement CreateSizeCard()
        {
            var card = Card("Playable size", OptimizerUI.Icon("d_Profiler.Memory"));
            card.AddToClassList("lo-size-card");
            var line = new VisualElement();
            line.AddToClassList("lo-size-line");
            totalLabel = new Label();
            totalLabel.AddToClassList("lo-size-total");
            line.Add(totalLabel);
            limitLabel = new Label();
            limitLabel.AddToClassList("lo-size-limit");
            line.Add(limitLabel);
            var spacer = new VisualElement();
            spacer.AddToClassList("lo-spacer");
            line.Add(spacer);
            measureButton = new Button(() =>
            {
                if (overview.IsMeasuring)
                    overview.CancelMeasure();
                else
                    EditorApplication.delayCall += overview.MeasureNow;
                Refresh();
            });
            measureButton.AddToClassList("lo-action");
            measureButton.AddToClassList("lo-primary");
            line.Add(measureButton);
            card.Add(line);

            meter = new VisualElement();
            meter.AddToClassList("lo-meter");
            meterFill = new VisualElement();
            meterFill.AddToClassList("lo-meter-fill");
            meter.Add(meterFill);
            card.Add(meter);
            meterLabel = new Label();
            meterLabel.AddToClassList("lo-muted");
            card.Add(meterLabel);
            freshnessLabel = new Label();
            freshnessLabel.AddToClassList("lo-muted");
            card.Add(freshnessLabel);
            problemLabel = new Label();
            problemLabel.AddToClassList("lo-warning");
            problemLabel.AddToClassList("lo-wrap");
            card.Add(problemLabel);
            return card;
        }

        VisualElement CreateCategoryCard()
        {
            var card = Card("What ships", OptimizerUI.TypeIcon(typeof(DefaultAsset)));
            var hint = new Label("Largest first. Click a row to work on it in Manual mode.");
            hint.AddToClassList("lo-muted");
            card.Add(hint);
            categoryList = new VisualElement();
            categoryList.AddToClassList("lo-category-list");
            card.Add(categoryList);
            return card;
        }

        VisualElement CreateTextureCard()
        {
            var card = Card("Textures", OptimizerUI.TypeIcon(typeof(Texture2D)));
            card.AddToClassList("lo-action-card");
            textureStats = new Label();
            textureStats.AddToClassList("lo-card-stat");
            card.Add(textureStats);

            var index = Mathf.Clamp(EditorPrefs.GetInt(PRESET_PREF, 0), 0, PRESETS.Length - 1);
            presetField = new DropdownField("Compression", PRESETS.Select(p => p.Name).ToList(), index);
            presetField.AddToClassList("lo-preset-field");
            presetField.RegisterValueChangedCallback(_ =>
            {
                EditorPrefs.SetInt(PRESET_PREF, presetField.index);
                Refresh();
            });
            card.Add(presetField);
            presetHint = new Label();
            presetHint.AddToClassList("lo-muted");
            presetHint.AddToClassList("lo-wrap");
            card.Add(presetHint);

            shrinkButton = ActionButton("Shrink texture formats", "Predict PNG, WebP and (without alpha) JPEG at the chosen compression for every texture and pick the smallest. Nothing is saved until Save all.", () =>
            {
                var preset = PRESETS[presetField.index];
                var changed = textures.PickSmallestAll(preset.Webp, preset.Jpeg);
                notify(changed == 0 ? "Every texture already uses its smallest format" : changed + " texture(s) changed: review, then Save all");
            }, true);
            card.Add(shrinkButton);
            texturePredictButton = ActionButton("Predict texture sizes", "Run Luna's texture pipeline locally for textures without an up-to-date prediction.", textures.PredictAll);
            card.Add(texturePredictButton);
            textureNote = new Label();
            textureNote.AddToClassList("lo-warning");
            textureNote.AddToClassList("lo-wrap");
            card.Add(textureNote);
            card.Add(LinkButton("Open Textures tab →", () => openTab(LunaOptimizerWindow.Tab.Textures)));
            return card;
        }

        VisualElement CreateSoundCard()
        {
            var card = Card("Sounds", OptimizerUI.TypeIcon(typeof(AudioClip)));
            card.AddToClassList("lo-action-card");
            soundStats = new Label();
            soundStats.AddToClassList("lo-card-stat");
            card.Add(soundStats);
            trimButton = ActionButton("Trim silence", "Leave out leading and trailing silence of every sound (within any trim already set). Nothing is saved until Save all.", () =>
            {
                var changed = sounds.TrimSilenceAll();
                notify(changed == 0 ? "No silence to trim" : changed + " sound(s) trimmed: review, then Save all");
            }, true);
            card.Add(trimButton);
            monoButton = ActionButton("Make all mono", "Luna mixes every sound down to one channel: about half the size for stereo sounds. Nothing is saved until Save all.", () =>
            {
                var changed = sounds.MonoAll();
                notify(changed == 0 ? "Every sound is already mono" : changed + " sound(s) set to mono: review, then Save all");
            });
            card.Add(monoButton);
            soundPredictButton = ActionButton("Predict sound sizes", "Run Luna's sound step locally for sounds without an up-to-date prediction.", sounds.PredictAll);
            card.Add(soundPredictButton);
            card.Add(LinkButton("Open Sounds tab →", () => openTab(LunaOptimizerWindow.Tab.Sounds)));
            return card;
        }

        VisualElement CreatePendingCard()
        {
            pendingCard = new VisualElement();
            pendingCard.AddToClassList("lo-pending-card");
            pendingLabel = new Label();
            pendingLabel.AddToClassList("lo-pending-text");
            pendingCard.Add(pendingLabel);
            var spacer = new VisualElement();
            spacer.AddToClassList("lo-spacer");
            pendingCard.Add(spacer);
            var review = new Button(() => openTab(textures.PendingCount > 0 ? LunaOptimizerWindow.Tab.Textures : LunaOptimizerWindow.Tab.Sounds)) { text = "Review" };
            review.AddToClassList("lo-action");
            pendingCard.Add(review);
            var discard = new Button(() => EditorApplication.delayCall += () => discardAll()) { text = "Discard" };
            discard.AddToClassList("lo-action");
            pendingCard.Add(discard);
            var save = new Button(() => EditorApplication.delayCall += () => saveAll()) { text = "Save all to Luna" };
            save.AddToClassList("lo-action");
            save.AddToClassList("lo-primary");
            pendingCard.Add(save);
            return pendingCard;
        }

        public void Refresh()
        {
            if (totalLabel == null)
                return;
            RefreshSize();
            RefreshCategories();
            RefreshTextures();
            RefreshSounds();
            RefreshPending();
        }

        void RefreshSize()
        {
            var measured = overview.HasMeasurement && overview.TotalBytes > 0;
            totalLabel.text = measured ? FormatBytes(overview.TotalBytes) : "Not measured";
            var limit = overview.Limit;
            limitLabel.text = measured && limit > 0 ? "of " + FormatBytes(limit) : string.Empty;
            var ratio = measured && limit > 0 ? (float)(overview.TotalBytes / limit) : 0f;
            meter.style.display = measured && limit > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            meterFill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);
            meterFill.EnableInClassList("lo-meter-fill--warn", ratio > WARN_RATIO && ratio <= 1f);
            meterFill.EnableInClassList("lo-meter-fill--over", ratio > 1f);
            meterLabel.text = measured && limit > 0
                ? (ratio * 100f).ToString("0") + "%  ·  " + (limit >= overview.TotalBytes ? FormatBytes(limit - overview.TotalBytes) + " left" : FormatBytes(overview.TotalBytes - limit) + " over the cap")
                : string.Empty;
            meterLabel.style.display = meterLabel.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (!overview.HasLunaInstall)
                freshnessLabel.text = "No Luna install found among this project's packages.";
            else if (overview.IsMeasuring)
                freshnessLabel.text = "Measuring… the first run compiles your scripts (~30-40 s), later ones are quick.";
            else if (measured)
                freshnessLabel.text = "Measured " + overview.MeasuredAtText + ". Scripts and textures are measured live; unsaved changes count once saved.";
            else
                freshnessLabel.text = "Quick predict runs Luna's own size statistics on your project, without a full build.";
            measureButton.text = overview.IsMeasuring ? "Cancel" : "Quick predict";
            measureButton.SetEnabled(overview.HasLunaInstall);
            measureButton.EnableInClassList("lo-primary", !overview.IsMeasuring);
            var problem = overview.LastMessage;
            problemLabel.text = problem ?? string.Empty;
            problemLabel.style.display = string.IsNullOrEmpty(problem) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Rebuilt only when the numbers change, so hover and clicks are not interrupted.
        void RefreshCategories()
        {
            var categories = overview.HasMeasurement ? overview.Categories() : new List<SizeDashboardView.CategorySummary>();
            var key = string.Join("|", categories.Select(c => c.Name + ":" + c.Bytes.ToString("0", CultureInfo.InvariantCulture) + ":" + c.Count + ":" + c.Estimated));
            if (key == categoryKey)
                return;
            categoryKey = key;
            categoryList.Clear();
            if (categories.Count == 0)
            {
                var empty = new Label("Press Quick predict to see what each asset type costs.");
                empty.AddToClassList("lo-muted");
                categoryList.Add(empty);
                return;
            }
            var largest = categories.Max(c => c.Bytes);
            var total = categories.Sum(c => c.Bytes);
            foreach (var category in categories)
                categoryList.Add(CategoryRow(category, largest, total));
        }

        VisualElement CategoryRow(SizeDashboardView.CategorySummary category, double largest, double total)
        {
            var tab = TabFor(category.Name);
            var row = new VisualElement { tooltip = (tab == LunaOptimizerWindow.Tab.Overview ? "Open the Overview tab" : "Open the " + tab + " tab")
                + (category.Estimated ? "\nPart of this size is estimated (scene not built yet, or scripts changed since the last measure)." : string.Empty) };
            row.AddToClassList("lo-category-row");
            row.AddManipulator(new Clickable(() => openTab(tab)));
            var name = new Label(Pretty(category.Name) + (category.Estimated ? " ≈" : string.Empty));
            name.AddToClassList("lo-category-name");
            row.Add(name);
            var count = new Label(category.Count > 0 ? category.Count.ToString() : string.Empty);
            count.AddToClassList("lo-category-count");
            row.Add(count);
            var bar = new VisualElement();
            bar.AddToClassList("lo-category-bar");
            var fill = new VisualElement();
            fill.AddToClassList("lo-category-fill");
            fill.style.width = Length.Percent(largest > 0 ? (float)(category.Bytes / largest * 100.0) : 0f);
            bar.Add(fill);
            row.Add(bar);
            var size = new Label(FormatBytes(category.Bytes));
            size.AddToClassList("lo-category-size");
            row.Add(size);
            var share = new Label(total > 0 ? (category.Bytes / total * 100.0).ToString("0") + "%" : string.Empty);
            share.AddToClassList("lo-category-share");
            row.Add(share);
            return row;
        }

        static LunaOptimizerWindow.Tab TabFor(string category)
        {
            var name = category.ToLowerInvariant();
            if (name.Contains("texture") || name.Contains("sprite"))
                return LunaOptimizerWindow.Tab.Textures;
            if (name.Contains("sound") || name.Contains("audio"))
                return LunaOptimizerWindow.Tab.Sounds;
            return LunaOptimizerWindow.Tab.Overview;
        }

        // "animation-clips" -> "Animation clips"
        static string Pretty(string category)
        {
            var text = category.Replace('-', ' ');
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
        }

        void RefreshTextures()
        {
            var count = textures.Count;
            var predicted = textures.PredictedCount;
            textureStats.text = count + " texture" + (count == 1 ? string.Empty : "s")
                + (predicted > 0 ? "  ·  " + FormatBytes(textures.PredictedTotalBytes) + " predicted" + (predicted < count ? " (" + predicted + "/" + count + ")" : string.Empty) : string.Empty);
            var preset = PRESETS[Mathf.Clamp(presetField.index, 0, PRESETS.Length - 1)];
            presetHint.text = preset.Hint;
            shrinkButton.SetEnabled(textures.HasLunaInstall && count > 0);
            texturePredictButton.SetEnabled(textures.CanPredictSizes);
            texturePredictButton.style.display = textures.CanPredictSizes ? DisplayStyle.Flex : DisplayStyle.None;
            var compressed = textures.UnityCompressedCount;
            textureNote.text = compressed > 0 ? compressed + " texture(s) reach Luna through Unity's compressed import, which usually makes them bigger. See the Textures tab (orange)." : string.Empty;
            textureNote.style.display = compressed > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshSounds()
        {
            var count = sounds.Count;
            var predicted = sounds.PredictedCount;
            soundStats.text = count + " sound" + (count == 1 ? string.Empty : "s")
                + (predicted > 0 ? "  ·  " + FormatBytes(sounds.PredictedTotalBytes) + " predicted" + (predicted < count ? " (" + predicted + "/" + count + ")" : string.Empty) : string.Empty);
            trimButton.SetEnabled(count > 0);
            monoButton.SetEnabled(count > 0);
            soundPredictButton.SetEnabled(sounds.CanPredictSizes);
            soundPredictButton.style.display = sounds.CanPredictSizes ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshPending()
        {
            var pending = textures.PendingCount + sounds.PendingCount;
            pendingCard.style.display = pending > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (pending == 0)
                return;
            var saving = textures.PendingSavingBytes + sounds.PendingSavingBytes;
            pendingLabel.text = pending + " unsaved change" + (pending == 1 ? string.Empty : "s")
                + (Math.Abs(saving) >= 1 ? (saving > 0 ? "  ·  saves ~" + FormatBytes(saving) : "  ·  adds ~" + FormatBytes(-saving)) : string.Empty);
        }

        static string FormatBytes(double bytes)
        {
            if (Math.Abs(bytes) >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}
