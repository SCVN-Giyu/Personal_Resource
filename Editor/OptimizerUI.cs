using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Small UI Toolkit building blocks shared by the Luna Optimizer tabs' details panels.
    public static class OptimizerUI
    {
        // Excluded assets are not shipped, so the tabs do not list them; point to where they are.
        public static string ExcludedNote(LunaSettings settings)
        {
            if (settings == null)
                return string.Empty;
            var count = 0;
            foreach (var entry in settings.Excludes)
            {
                if (entry.StartsWith("Assets/", System.StringComparison.Ordinal) && !entry.Contains("*"))
                    count++;
            }
            return count > 0 ? "  ·  " + count + " excluded asset/folder entr" + (count == 1 ? "y" : "ies") + " not listed (see Exclusions)" : string.Empty;
        }

        public static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("lo-section-title");
            return label;
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

        // What happens if the asset is excluded, then the chain of shipped assets and scene objects that use it.
        public static VisualElement UsedBy(BlastRadius result)
        {
            var box = new VisualElement();
            var target = result != null && result.Targets.Count > 0 ? result.Targets[0] : null;
            if (target == null)
            {
                box.Add(SmallLabel("Not found in the build graph."));
                return box;
            }
            if (target.Users.Count == 0)
            {
                box.Add(SmallLabel("Nothing in the build references it directly (Resources or always included)."));
                return box;
            }
            box.Add(SmallLabel("If excluded: " + target.Consequence));
            foreach (var link in target.Users)
                box.Add(LinkElement(link, 0));
            return box;
        }

        static VisualElement LinkElement(BlastRadius.Link link, int depth)
        {
            var element = new VisualElement();
            element.style.marginLeft = depth == 0 ? 0 : 14;
            var row = new VisualElement();
            row.AddToClassList("lo-row");
            row.Add(SmallLabel("←"));
            row.Add(LinkLabel(Path.GetFileName(link.AssetPath), link.AssetPath, null));
            if (link.RootKind != null)
            {
                var root = SmallLabel("[" + link.RootKind + "]");
                root.AddToClassList("lo-bold");
                row.Add(root);
            }
            if (link.ShownElsewhere)
                row.Add(SmallLabel("(users listed above)"));
            element.Add(row);
            foreach (var where in link.Where)
            {
                var line = SmallLabel("@ " + where);
                line.tooltip = where;
                line.style.marginLeft = 20;
                element.Add(line);
            }
            foreach (var user in link.Users)
                element.Add(LinkElement(user, depth + 1));
            return element;
        }
    }
}
