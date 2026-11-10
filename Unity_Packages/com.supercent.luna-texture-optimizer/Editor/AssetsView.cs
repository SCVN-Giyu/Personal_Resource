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
    // "Models & Other" tab of the Luna Optimizer: every shipped asset that is not a texture, sound, scene or script
    // (models, prefabs, materials, animations, fonts, data…) with Unity's own preview, its size in the playable
    // and what uses it. Read-only apart from adding assets to the Luna Exclusions list.
    [Serializable]
    public class AssetsView : OptimizerView
    {
        const float ROW_HEIGHT = 24f;
        const float DETAILS_WIDTH = 400f;
        const float PREVIEW_HEIGHT = 300f;
        const string ALL_TYPES = "All types";

        enum Column
        {
            Pick,
            Name,
            Kind,
            Folder,
            File,
            Playable,
            Users,
        }

        class AssetRow
        {
            public string Path;
            public string Name;
            public string Kind;
            public string Folder;
            public long FileBytes;
            public int UserCount;
        }

        List<AssetRow> rows = new List<AssetRow>();
        List<AssetRow> visibleRows = new List<AssetRow>();
        LunaSettings settings;
        string searchText = string.Empty;
        string kindFilter = ALL_TYPES;
        HashSet<string> ticked = new HashSet<string>();
        [SerializeField] string selectedPath;
        readonly Dictionary<string, BlastRadius> usedByCache = new Dictionary<string, BlastRadius>();
        UnityEditor.Editor previewEditor;    // not for models/prefabs: Unity's GameObject preview stayed blank for FBX models in the hub

        MultiColumnListView table;
        Label summaryLabel;
        VisualElement statStrip;
        SearchBar searchBar;
        DropdownField kindField;
        Button excludeTickedButton;
        ScrollView details;

        public override string Title
        {
            get { return "Models & Other"; }
        }

        public override void OnEnable()
        {
            ExclusionQueue.Changed += OnExclusionQueueChanged;
            Refresh();
        }

        public override void OnDisable()
        {
            ExclusionQueue.Changed -= OnExclusionQueueChanged;
            DestroyPreviewEditor();
        }

        void Refresh()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            settings = LunaSettings.Load(projectRoot);
            rows = new List<AssetRow>();
            usedByCache.Clear();
            if (settings != null)
            {
                HashSet<string> shipped;
                var users = BlastRadius.DirectUsers(settings, out shipped);
                foreach (var path in shipped)
                {
                    var kind = KindOf(path);
                    if (kind == null)
                        continue;
                    List<string> list;
                    var fullPath = Path.Combine(projectRoot, path);
                    rows.Add(new AssetRow
                    {
                        Path = path,
                        Name = Path.GetFileName(path),
                        Kind = kind,
                        Folder = Path.GetDirectoryName(path).Replace('\\', '/'),
                        FileBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
                        UserCount = users.TryGetValue(path, out list) ? list.Count : 0,
                    });
                }
            }
            ticked.IntersectWith(rows.Select(r => r.Path));
            ApplySortAndFilter();
        }

        // ---------------------------------------------------------------- for the browser page (LunaOptimizerWebApi)

        public List<Dictionary<string, object>> BrowserRows()
        {
            return rows.Select(r => new Dictionary<string, object>
            {
                { "name", r.Name }, { "path", r.Path }, { "kind", r.Kind }, { "folder", r.Folder },
                { "file", r.FileBytes }, { "users", r.UserCount }, { "playable", LunaOptimizerWindow.SizeOf(r.Path) },
                { "excluding", ExclusionQueue.IsQueued(r.Path) },
            }).ToList();
        }

        public LunaSettings Settings
        {
            get { return settings; }
        }

        public void Rescan()
        {
            Refresh();
        }

        // Null for what other tabs cover (textures, sounds) and what is not an asset of its own (scenes, scripts, folders).
        static string KindOf(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                return null;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || AssetDatabase.IsValidFolder(path))
                return null;
            if (typeof(Texture).IsAssignableFrom(type) || type == typeof(AudioClip) || type == typeof(SceneAsset) || type == typeof(MonoScript)
                || type == typeof(DefaultAsset))
                return null;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (type == typeof(GameObject))
                return extension == ".prefab" ? "Prefab" : "Model";
            if (type == typeof(Mesh))
                return "Mesh";
            if (type == typeof(Material))
                return "Material";
            if (type == typeof(AnimationClip))
                return "Animation";
            if (typeof(RuntimeAnimatorController).IsAssignableFrom(type) || type == typeof(AvatarMask))
                return "Animator";
            if (type == typeof(Font) || type.Name == "TMP_FontAsset")
                return "Font";
            if (type == typeof(Shader) || type.Name == "ShaderVariantCollection")
                return "Shader";
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return "Data (" + type.Name + ")";
            return type.Name;
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

            var toolbar = new Toolbar();
            toolbar.AddToClassList("lo-tab-toolbar");
            toolbar.Add(OptimizerUI.IconButton("d_Refresh", "Refresh", "Scan again: what Luna ships and who uses what.", Refresh));
            kindField = new DropdownField(new List<string> { ALL_TYPES }, 0);
            kindField.AddToClassList("lo-toolbar-field");
            kindField.RegisterValueChangedCallback(change =>
            {
                kindFilter = change.newValue;
                ApplySortAndFilter();
            });
            toolbar.Add(kindField);
            excludeTickedButton = new ToolbarButton(() => LunaExclusionWindow.AddAndShow(TickedVisible().Select(r => r.Path).ToList()))
            {
                tooltip = "Add the ticked assets to the Luna Exclusions window for review.",
            };
            toolbar.Add(excludeTickedButton);
            root.Add(toolbar);

            statStrip = OptimizerUI.StatStrip();
            root.Add(statStrip);
            summaryLabel = new Label();
            summaryLabel.AddToClassList("lo-summary");
            summaryLabel.AddToClassList("lo-muted");
            root.Add(summaryLabel);
            var bar = new VisualElement();
            bar.AddToClassList("lo-bulkbar");
            searchBar = new SearchBar(searchText, "Search assets by name or path   (Ctrl/Cmd+F)", value =>
            {
                searchText = value;
                ApplySortAndFilter();
            });
            bar.Add(searchBar);
            root.Add(bar);
            searchBar.RegisterShortcut(root);

            var split = new TwoPaneSplitView(1, DETAILS_WIDTH, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "lo-assets-split" };
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

        MultiColumnListView CreateTable()
        {
            table = new MultiColumnListView
            {
                fixedItemHeight = ROW_HEIGHT,
                selectionType = SelectionType.Single,
                sortingEnabled = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                viewDataKey = "lo-assets-table",
            };
            table.AddToClassList("lo-table");
            AddColumn(Column.Pick, string.Empty, 26f, false, () => new Toggle(), (element, row) =>
                ((Toggle)element).SetValueWithoutNotify(ticked.Contains(row.Path)),
                element => ((Toggle)element).RegisterValueChangedCallback(change =>
                {
                    var row = RowOf(element);
                    if (row == null)
                        return;
                    if (change.newValue) ticked.Add(row.Path); else ticked.Remove(row.Path);
                    RefreshToolbar();
                }));
            AddColumn(Column.Name, "Asset", 220f, false, () => new Label(), (element, r) =>
            {
                var label = (Label)element;
                var queued = ExclusionQueue.IsQueued(r.Path);
                label.text = r.Name + (queued ? "   ⦸ excluding" : string.Empty);
                label.tooltip = r.Path + (queued ? "\nWaiting in the Luna Exclusions window (Pending)." : string.Empty);
                label.EnableInClassList("lo-excluding", queued);
            }, null);
            AddLabelColumn(Column.Kind, "Type", 110f, false, r => r.Kind, null);
            AddLabelColumn(Column.Folder, "Folder", 220f, false, r => r.Folder, r => r.Folder);
            AddLabelColumn(Column.File, "File", 80f, true, r => FormatBytes(r.FileBytes), null);
            AddLabelColumn(Column.Playable, "In playable", 90f, true, r => PlayableText(r.Path),
                r => "From the Overview tab's last Measure. Luna lists some assets only by name, and nested parts (meshes inside a model) under their own entries.");
            AddLabelColumn(Column.Users, "Used by", 70f, true, r => r.UserCount.ToString(), r => r.UserCount + " shipped asset(s) or scene(s) reference it directly");

            table.sortColumnDescriptions.Add(new SortColumnDescription(Column.Playable.ToString(), SortDirection.Descending));
            table.columnSortingChanged += ApplySortAndFilter;
            table.selectionChanged += selection =>
            {
                var row = selection.OfType<AssetRow>().FirstOrDefault();
                selectedPath = row != null ? row.Path : null;
                RefreshDetails();
            };
            table.itemsChosen += chosen =>
            {
                var row = chosen.OfType<AssetRow>().FirstOrDefault();
                if (row != null)
                    OptimizerUI.Ping(row.Path);
            };
            return table;
        }

        void AddColumn(Column column, string title, float width, bool rightAligned, Func<VisualElement> make,
            Action<VisualElement, AssetRow> bind, Action<VisualElement> registerOnce)
        {
            table.columns.Add(new UnityEngine.UIElements.Column
            {
                name = column.ToString(),
                title = title,
                width = width,
                minWidth = Mathf.Min(width, 40f),
                sortable = column != Column.Pick,
                stretchable = column == Column.Name || column == Column.Folder,
                makeCell = () =>
                {
                    var cell = new VisualElement();
                    cell.AddToClassList("lo-cell");
                    if (rightAligned)
                        cell.AddToClassList("lo-right");
                    var control = make();
                    cell.Add(control);
                    cell.AddManipulator(new ContextualMenuManipulator(evt => BuildRowMenu(evt, RowOf(cell))));
                    if (registerOnce != null)
                        registerOnce(control);
                    return cell;
                },
                bindCell = (cell, index) =>
                {
                    var row = index >= 0 && index < visibleRows.Count ? visibleRows[index] : null;
                    cell.userData = row;
                    if (row != null)
                        bind(cell[0], row);
                },
            });
        }

        void AddLabelColumn(Column column, string title, float width, bool rightAligned, Func<AssetRow, string> text, Func<AssetRow, string> tooltip)
        {
            AddColumn(column, title, width, rightAligned, () => new Label(), (element, row) =>
            {
                var label = (Label)element;
                label.text = text(row);
                label.tooltip = tooltip != null ? tooltip(row) : null;
            }, null);
        }

        static AssetRow RowOf(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                var row = current.userData as AssetRow;
                if (row != null)
                    return row;
            }
            return null;
        }

        void BuildRowMenu(ContextualMenuPopulateEvent evt, AssetRow row)
        {
            if (row == null)
                return;
            var path = row.Path;
            evt.menu.AppendAction("Add to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(new[] { path }));
            var tickedPaths = TickedVisible().Select(r => r.Path).ToList();
            if (tickedPaths.Count > 1)
                evt.menu.AppendAction("Add " + tickedPaths.Count + " ticked assets to Luna Exclusions", _ => LunaExclusionWindow.AddAndShow(tickedPaths));
            evt.menu.AppendAction("Show in Project", _ => OptimizerUI.Ping(path));
        }

        List<AssetRow> TickedVisible()
        {
            return visibleRows.Where(r => ticked.Contains(r.Path)).ToList();
        }

        static string PlayableText(string path)
        {
            var bytes = LunaOptimizerWindow.SizeOf(path);
            return bytes > 0 ? FormatBytes(bytes) : "–";
        }

        void ApplySortAndFilter()
        {
            visibleRows = rows.Where(r => (kindFilter == ALL_TYPES || r.Kind == kindFilter)
                && (searchText.Length == 0 || r.Path.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            var sort = table != null ? table.sortedColumns.FirstOrDefault() : null;
            Column column;
            var sortColumn = sort != null && Enum.TryParse(sort.columnName, out column) ? column : Column.Playable;
            var ascending = sort != null && sort.direction == SortDirection.Ascending;
            var comparison = ComparisonFor(sortColumn);
            visibleRows.Sort((a, b) => ascending ? comparison(a, b) : comparison(b, a));
            RefreshUI();
        }

        static Comparison<AssetRow> ComparisonFor(Column column)
        {
            switch (column)
            {
                case Column.Kind: return (a, b) => string.CompareOrdinal(a.Kind, b.Kind);
                case Column.Folder: return (a, b) => string.CompareOrdinal(a.Folder, b.Folder);
                case Column.File: return (a, b) => a.FileBytes.CompareTo(b.FileBytes);
                case Column.Playable: return (a, b) => LunaOptimizerWindow.SizeOf(a.Path).CompareTo(LunaOptimizerWindow.SizeOf(b.Path));
                case Column.Users: return (a, b) => a.UserCount.CompareTo(b.UserCount);
                default: return (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        void RefreshUI()
        {
            if (table == null)
                return;
            searchBar.SetCount(visibleRows.Count, rows.Count);
            table.itemsSource = visibleRows;
            table.RefreshItems();
            var index = selectedPath != null ? visibleRows.FindIndex(r => r.Path == selectedPath) : -1;
            if (index >= 0)
                table.SetSelectionWithoutNotify(new[] { index });
            else
                table.ClearSelection();

            var kinds = new[] { ALL_TYPES }.Concat(rows.Select(r => r.Kind).Distinct().OrderBy(k => k)).ToList();
            kindField.choices = kinds;
            kindField.SetValueWithoutNotify(kinds.Contains(kindFilter) ? kindFilter : ALL_TYPES);

            statStrip.Clear();
            if (settings == null)
            {
                summaryLabel.text = LunaSettings.FILE_NAME + " not found in the project root.";
            }
            else
            {
                OptimizerUI.AddStat(statStrip, rows.Count.ToString(), "other shipped assets", null, "lo-stat--key");
                foreach (var group in rows.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()).Take(6))
                    OptimizerUI.AddStat(statStrip, group.Count().ToString(), group.Key.ToLowerInvariant());
                var excluded = OptimizerUI.ExcludedCount(settings);
                if (excluded > 0)
                    OptimizerUI.AddStat(statStrip, excluded.ToString(), "excluded", "Excluded assets/folders are not listed: see Exclusions.");
                summaryLabel.text = "Sizes in the playable come from the Overview's last Measure.";
            }
            RefreshToolbar();
            RefreshDetails();
        }

        void RefreshToolbar()
        {
            if (excludeTickedButton == null)
                return;
            var count = TickedVisible().Count;
            excludeTickedButton.text = count > 0 ? "Exclude " + count + " ticked…" : "Exclude ticked…";
            excludeTickedButton.SetEnabled(count > 0);
        }

        // ---------------------------------------------------------------- details panel

        void RefreshDetails()
        {
            if (details == null)
                return;
            details.Clear();
            DestroyPreviewEditor();
            var row = selectedPath != null ? rows.FirstOrDefault(r => r.Path == selectedPath) : null;
            if (row == null)
            {
                var empty = new Label("Select an asset to preview it, see its size in the playable and what uses it.");
                empty.AddToClassList("lo-empty");
                details.Add(empty);
                return;
            }

            var badges = new List<Label> { OptimizerUI.Badge(row.Kind, null) };
            if (ExclusionQueue.IsQueued(row.Path))
                badges.Add(OptimizerUI.Badge("Excluding", "warn", "Waiting in the Luna Exclusions window (Pending)."));
            details.Add(OptimizerUI.DetailsHeader(row.Name, row.Path, badges));

            var asset = AssetDatabase.LoadMainAssetAtPath(row.Path);
            if (asset != null)
            {
                if (!(asset is GameObject))
                    previewEditor = UnityEditor.Editor.CreateEditor(asset);
                var preview = new IMGUIContainer();
                preview.onGUIHandler = () => DrawPreview(preview, asset);
                preview.AddToClassList("lo-preview");
                preview.style.height = PREVIEW_HEIGHT;
                details.Add(preview);
                if (HasInteractivePreview())
                    details.Add(OptimizerUI.SmallLabel("Drag to rotate, scroll to zoom (Unity's preview)."));
            }

            details.Add(OptimizerUI.Metric(PlayableText(row.Path), "in playable", null, false));
            details.Add(OptimizerUI.SectionTitle("Size"));
            details.Add(OptimizerUI.InfoRow("File", FormatBytes(row.FileBytes)));
            var dependencies = AssetDatabase.GetDependencies(row.Path, false).Where(d => d != row.Path && !d.EndsWith(".cs", StringComparison.Ordinal)).ToList();
            details.Add(OptimizerUI.InfoRow("Uses", dependencies.Count == 0 ? "nothing" : dependencies.Count + " asset(s)"));
            foreach (var dependency in dependencies.Take(12))
            {
                var link = OptimizerUI.LinkLabel(Path.GetFileName(dependency), dependency, "lo-small");
                link.style.marginLeft = 90;
                details.Add(link);
            }
            if (dependencies.Count > 12)
                details.Add(OptimizerUI.SmallLabel("… +" + (dependencies.Count - 12) + " more"));

            details.Add(OptimizerUI.SectionTitle("Used by"));
            BlastRadius result;
            if (!usedByCache.TryGetValue(row.Path, out result))
            {
                result = settings != null ? BlastRadius.Analyze(new[] { row.Path }, settings) : null;
                usedByCache[row.Path] = result;
            }
            details.Add(OptimizerUI.UsedBy(result));

            var actions = new VisualElement();
            actions.AddToClassList("lo-details-actions");
            var ping = new Button(() => OptimizerUI.Ping(row.Path)) { text = "Show in Project" };
            ping.AddToClassList("lo-details-button");
            actions.Add(ping);
            var exclude = new Button(() => LunaExclusionWindow.AddAndShow(new[] { row.Path })) { text = "Exclude from build…", tooltip = "Review it in the Luna Exclusions window before anything is written." };
            exclude.AddToClassList("lo-details-button");
            actions.Add(exclude);
            details.Add(actions);
        }

        // Some of Unity's editors (AnimationClipEditor right after a domain reload) throw from HasPreviewGUI: no preview then.
        bool HasInteractivePreview()
        {
            if (previewEditor == null)
                return false;
            try
            {
                return previewEditor.HasPreviewGUI();
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Models and prefabs: Unity's thumbnail. Others: Unity's interactive preview when there is one (materials, meshes, animations), else the thumbnail.
        void DrawPreview(IMGUIContainer container, UnityEngine.Object asset)
        {
            var rect = new Rect(0f, 0f, container.contentRect.width, container.contentRect.height);
            if (rect.width < 2f || rect.height < 2f)
                return;
            if (HasInteractivePreview())
            {
                previewEditor.OnInteractivePreviewGUI(rect, EditorStyles.helpBox);
                return;
            }
            var thumbnail = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail != null)
                GUI.DrawTexture(rect, thumbnail, ScaleMode.ScaleToFit);
            if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                container.MarkDirtyRepaint();
        }

        void DestroyPreviewEditor()
        {
            if (previewEditor != null)
                UnityEngine.Object.DestroyImmediate(previewEditor);
            previewEditor = null;
        }

        static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }
    }
}
