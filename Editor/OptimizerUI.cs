using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Small UI Toolkit building blocks shared by the Luna Optimizer tabs' details panels.
    public static class OptimizerUI
    {
        // Excluded assets are not shipped, so the tabs do not list them; point to where they are.
        public static string ExcludedNote(LunaSettings settings)
        {
            var count = ExcludedCount(settings);
            return count > 0 ? "  ·  " + count + " excluded asset/folder entr" + (count == 1 ? "y" : "ies") + " not listed (see Exclusions)" : string.Empty;
        }

        // Plain asset/folder entries in Luna's excludes (patterns and Luna's own defaults are not counted).
        public static int ExcludedCount(LunaSettings settings)
        {
            if (settings == null)
                return 0;
            var count = 0;
            foreach (var entry in settings.Excludes)
            {
                if (entry.StartsWith("Assets/", System.StringComparison.Ordinal) && !entry.Contains("*"))
                    count++;
            }
            return count;
        }

        public static Label SectionTitle(string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.AddToClassList("lo-section-title");
            return label;
        }

        // A built-in editor icon, or null when this editor version does not have it (no console error).
        public static Texture Icon(string name)
        {
            return EditorGUIUtility.FindTexture(name);
        }

        // The small icon Unity shows for assets of this type (always present, unlike icon names).
        public static Texture TypeIcon(System.Type type)
        {
            return EditorGUIUtility.ObjectContent(null, type).image;
        }

        // Toolbar button showing a built-in icon (text when the icon is missing).
        public static UnityEditor.UIElements.ToolbarButton IconButton(string icon, string fallbackText, string tooltip, System.Action action)
        {
            var button = new UnityEditor.UIElements.ToolbarButton(action) { tooltip = tooltip };
            var texture = Icon(icon);
            if (texture == null)
            {
                button.text = fallbackText;
                return button;
            }
            button.AddToClassList("lo-icon-button");
            var image = new Image { image = texture };
            button.Add(image);
            return button;
        }

        // Row of "value caption" chips above a table (replaces a long summary sentence).
        public static VisualElement StatStrip()
        {
            var strip = new VisualElement();
            strip.AddToClassList("lo-stats");
            return strip;
        }

        public static void AddStat(VisualElement strip, string value, string caption, string tooltip = null, string extraClass = null)
        {
            var chip = new VisualElement { tooltip = tooltip };
            chip.AddToClassList("lo-stat");
            if (extraClass != null)
                chip.AddToClassList(extraClass);
            var number = new Label(value);
            number.AddToClassList("lo-stat-value");
            chip.Add(number);
            var text = new Label(caption);
            text.AddToClassList("lo-stat-caption");
            chip.Add(text);
            strip.Add(chip);
        }

        // Small rounded tag: "Override", "Unsaved", "Excluding"…
        public static Label Badge(string text, string kind, string tooltip = null)
        {
            var badge = new Label(text) { tooltip = tooltip };
            badge.AddToClassList("lo-badge");
            if (kind != null)
                badge.AddToClassList("lo-badge--" + kind);
            return badge;
        }

        // Title, clickable path and badges at the top of a details panel.
        public static VisualElement DetailsHeader(string title, string path, IEnumerable<Label> badges)
        {
            var header = new VisualElement();
            header.AddToClassList("lo-details-header");
            var name = new Label(title);
            name.AddToClassList("lo-details-title");
            header.Add(name);
            header.Add(LinkLabel(path, path, "lo-details-path"));
            var row = new VisualElement();
            row.AddToClassList("lo-badges");
            foreach (var badge in badges)
                row.Add(badge);
            if (row.childCount > 0)
                header.Add(row);
            return header;
        }

        // The one number that matters ("10.9 KB in playable") with the unsaved change next to it.
        public static VisualElement Metric(string value, string caption, string delta, bool isSaving)
        {
            var metric = new VisualElement();
            metric.AddToClassList("lo-metric");
            var number = new Label(value);
            number.AddToClassList("lo-metric-value");
            metric.Add(number);
            var text = new Label(caption);
            text.AddToClassList("lo-metric-caption");
            metric.Add(text);
            if (!string.IsNullOrEmpty(delta))
            {
                var change = new Label(delta);
                change.AddToClassList("lo-metric-delta");
                change.AddToClassList(isSaving ? "lo-metric-delta--down" : "lo-metric-delta--up");
                metric.Add(change);
            }
            return metric;
        }

        // A size cell: a small meter (size against the largest one listed) next to the text.
        public static VisualElement SizeCell()
        {
            var cell = new VisualElement();
            cell.AddToClassList("lo-size-cell");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("lo-size-track");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("lo-size-heat");
            track.Add(fill);
            cell.Add(track);
            cell.Add(new Label());
            return cell;
        }

        public static void SetHeat(VisualElement sizeCell, double bytes, double largest)
        {
            var fill = sizeCell[0][0];
            // Square root: small items stay visible next to one large outlier.
            var ratio = largest > 0 && bytes > 0 ? (float)System.Math.Sqrt(System.Math.Min(1.0, bytes / largest)) : 0f;
            fill.style.width = Length.Percent(ratio * 100f);
        }

        // "−1.2 KB" / "+0.4 KB"
        public static string Delta(double bytes)
        {
            return (bytes <= 0 ? "−" : "+") + Bytes(System.Math.Abs(bytes));
        }

        public static string Bytes(double bytes)
        {
            if (System.Math.Abs(bytes) >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            return (bytes / 1024).ToString("0.0") + " KB";
        }

        public static VisualElement InfoRow(string name, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("lo-info-row");
            var key = new Label(name);
            key.AddToClassList("lo-info-key");
            row.Add(key);
            var text = new Label(value);
            text.AddToClassList("lo-info-value");
            row.Add(text);
            return row;
        }

        public static Label SmallLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("lo-small");
            return label;
        }

        public static Label LinkLabel(string text, string path, string extraClass)
        {
            var label = new Label(text) { tooltip = path + "\nClick to show in the Project window" };
            label.AddToClassList("lo-link");
            if (extraClass != null)
                label.AddToClassList(extraClass);
            label.AddManipulator(new Clickable(() => Ping(path)));
            return label;
        }

        public static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                return;
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        // What happens if the asset is excluded, what makes Luna ship it, then the chain of shipped assets and
        // scene objects that use it, as a tree: each step names the asset (icon, name, folder) and how it uses the one above.
        public static VisualElement UsedBy(BlastRadius result)
        {
            var box = new VisualElement();
            box.AddToClassList("lo-usedby");
            var target = result != null && result.Targets.Count > 0 ? result.Targets[0] : null;
            if (target == null)
            {
                box.Add(Callout("Not found in the build graph.", null));
                return box;
            }
            if (target.Users.Count == 0)
            {
                box.Add(Callout("Nothing in the build references it directly: Luna ships it on its own (Resources or always included).", null));
                return box;
            }

            box.Add(Callout("If excluded: " + target.Consequence, "warn"));
            var roots = new List<BlastRadius.Link>();
            CollectRoots(target.Users, roots);
            if (roots.Count > 0)
            {
                var row = new VisualElement();
                row.AddToClassList("lo-usedby-roots");
                var caption = new Label("Ships because of");
                caption.AddToClassList("lo-usedby-caption");
                row.Add(caption);
                foreach (var root in roots.GroupBy(r => r.AssetPath).Select(g => g.First()))
                    row.Add(RootChip(root));
                box.Add(row);
            }
            var tree = new VisualElement();
            tree.AddToClassList("lo-tree");
            foreach (var link in target.Users)
                tree.Add(TreeNode(link));
            box.Add(tree);
            return box;
        }

        static void CollectRoots(IEnumerable<BlastRadius.Link> links, List<BlastRadius.Link> roots)
        {
            foreach (var link in links)
            {
                if (link.RootKind != null)
                    roots.Add(link);
                CollectRoots(link.Users, roots);
            }
        }

        static VisualElement Callout(string text, string kind)
        {
            var callout = new VisualElement();
            callout.AddToClassList("lo-callout");
            if (kind != null)
                callout.AddToClassList("lo-callout--" + kind);
            var label = new Label(text);
            label.AddToClassList("lo-callout-text");
            callout.Add(label);
            return callout;
        }

        static VisualElement RootChip(BlastRadius.Link root)
        {
            var chip = new VisualElement { tooltip = root.AssetPath + "\nClick to show in the Project window" };
            chip.AddToClassList("lo-root-chip");
            var icon = AssetDatabase.GetCachedIcon(root.AssetPath);
            if (icon != null)
            {
                var image = new Image { image = icon };
                image.AddToClassList("lo-tree-icon");
                chip.Add(image);
            }
            chip.Add(new Label(Path.GetFileNameWithoutExtension(root.AssetPath)));
            var kind = new Label(root.RootKind);
            kind.AddToClassList("lo-root-kind");
            chip.Add(kind);
            var path = root.AssetPath;
            chip.AddManipulator(new Clickable(() => Ping(path)));
            return chip;
        }

        static VisualElement TreeNode(BlastRadius.Link link)
        {
            var node = new VisualElement();
            node.AddToClassList("lo-tree-node");

            var head = new VisualElement();
            head.AddToClassList("lo-tree-head");
            var icon = AssetDatabase.GetCachedIcon(link.AssetPath);
            if (icon != null)
            {
                var image = new Image { image = icon };
                image.AddToClassList("lo-tree-icon");
                head.Add(image);
            }
            head.Add(LinkLabel(Path.GetFileName(link.AssetPath), link.AssetPath, "lo-tree-name"));
            var folder = new Label(ShortFolder(link.AssetPath)) { tooltip = link.AssetPath };
            folder.AddToClassList("lo-tree-folder");
            head.Add(folder);
            if (link.RootKind != null)
                head.Add(Badge(link.RootKind, "accent", "Luna ships this on its own, which is why everything below it ships"));
            if (link.ShownElsewhere)
                head.Add(Badge("see above", null, "Its users are already listed in another branch"));
            node.Add(head);

            foreach (var where in link.Where)
            {
                var line = new VisualElement { tooltip = where };
                line.AddToClassList("lo-tree-where");
                var arrow = new Label("uses it in");
                arrow.AddToClassList("lo-tree-where-caption");
                line.Add(arrow);
                var text = new Label(where);
                text.AddToClassList("lo-tree-where-text");
                line.Add(text);
                node.Add(line);
            }

            if (link.Users.Count > 0)
            {
                var children = new VisualElement();
                children.AddToClassList("lo-tree-children");
                foreach (var user in link.Users)
                    children.Add(TreeNode(user));
                node.Add(children);
            }
            return node;
        }

        // ".../Prefabs/Props" — the last two folders, enough to tell same-named files apart.
        static string ShortFolder(string assetPath)
        {
            var parts = (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/').Split('/');
            return parts.Length <= 2 ? string.Join("/", parts) : "…/" + parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
        }
    }
}
