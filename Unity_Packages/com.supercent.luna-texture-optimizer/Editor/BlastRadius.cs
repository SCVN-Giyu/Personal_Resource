using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // What breaks when assets are left out of a Luna build: for each excluded asset, the chain of shipped assets and
    // scene objects that use it, up to the scene (or Resources / always-included asset) that pulls it in.
    // Luna drops an excluded asset silently and the users keep a dangling reference (verified 2026-11-06:
    // a material whose texture was excluded renders white).
    public class BlastRadius
    {
        const int MAX_DEPTH = 10;

        public class Link
        {
            public string AssetPath;
            public List<string> Where = new List<string>();   // how this asset uses the one below it
            public List<Link> Users = new List<Link>();
            public string RootKind;                           // why Luna ships it on its own: scene, Resources, always included
            public bool ShownElsewhere;                       // users already listed under another branch
        }

        public class Target
        {
            public string AssetPath;
            public string Consequence;
            public List<Link> Users = new List<Link>();
        }

        public List<Target> Targets = new List<Target>();
        public List<string> NotShipped = new List<string>();   // selected files Luna does not ship anyway
        public List<string> Blockers = new List<string>();     // exclusions Luna would refuse (its build scene)

        // selection: asset files and/or folders. Folders cover every shipped asset under them.
        // asIfShipped: excluded entries to treat as shipped, to show what an existing exclusion leaves broken.
        public static BlastRadius Analyze(IList<string> selection, LunaSettings settings, IList<string> asIfShipped = null)
        {
            var graph = new Graph(settings, asIfShipped ?? new string[0]);
            var result = new BlastRadius();

            var targets = new List<string>();
            foreach (var path in selection)
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    var prefix = path.TrimEnd('/') + "/";
                    targets.AddRange(graph.Shipped.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)));
                }
                else if (graph.Shipped.Contains(path))
                {
                    targets.Add(path);
                }
                else
                {
                    result.NotShipped.Add(path);
                }
            }
            targets = targets.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

            foreach (var scene in settings.Scenes.Where(targets.Contains))
                result.Blockers.Add(scene + " is a Luna build scene: Luna refuses to build without it.");

            var targetSet = new HashSet<string>(targets);
            var expanded = new HashSet<string>();
            var locators = new Dictionary<string, YamlReferenceLocator>();
            foreach (var path in targets)
            {
                var target = new Target { AssetPath = path, Consequence = ConsequenceFor(path) };
                target.Users = graph.UsersOf(path, targetSet, expanded, locators, 0);
                result.Targets.Add(target);
            }
            return result;
        }

        // Shipped assets and the shipped assets that reference each of them directly (one graph pass).
        public static Dictionary<string, List<string>> DirectUsers(LunaSettings settings, out HashSet<string> shipped)
        {
            var graph = new Graph(settings, new string[0]);
            shipped = graph.Shipped;
            return graph.Users;
        }

        // Only textures are verified in a build; the rest is what a null reference usually does in Unity.
        public static string ConsequenceFor(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (type == null)
                return "References to it become empty.";
            if (typeof(Texture).IsAssignableFrom(type) || type == typeof(Sprite))
                return "Materials and images that use it render plain white (verified in a Luna build).";
            if (type == typeof(AudioClip))
                return "Sounds that use it play nothing (not tested in a build).";
            if (type == typeof(Material))
                return "Renderers that use it lose their material (not tested in a build).";
            if (type == typeof(Mesh) || extension == ".fbx" || extension == ".obj")
                return "Objects using this mesh/model show nothing (not tested in a build).";
            if (type == typeof(GameObject))
                return "Fields that hold this prefab become null; spawning it can throw and stop gameplay (not tested in a build).";
            if (type == typeof(AnimationClip) || typeof(RuntimeAnimatorController).IsAssignableFrom(type))
                return "Animations from it do not play (not tested in a build).";
            if (type == typeof(Font) || type.Name == "TMP_FontAsset")
                return "Text that uses this font may not render (not tested in a build).";
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return "Script fields that hold it become null; code reading them can throw (not tested in a build).";
            if (type == typeof(SceneAsset))
                return "The scene is not exported.";
            return "References to it become empty (not tested in a build).";
        }

        // Shipped assets and who uses whom, following references the way Luna's export does: from the build roots,
        // not into assets that are already excluded.
        class Graph
        {
            public readonly HashSet<string> Shipped = new HashSet<string>();
            readonly Dictionary<string, List<string>> users = new Dictionary<string, List<string>>();

            public Dictionary<string, List<string>> Users
            {
                get { return users; }
            }
            readonly Dictionary<string, string> rootKinds = new Dictionary<string, string>();

            readonly LunaSettings settings;
            readonly IList<string> asIfShipped;

            public Graph(LunaSettings settings, IList<string> asIfShipped)
            {
                this.settings = settings;
                this.asIfShipped = asIfShipped;
                foreach (var scene in settings.Scenes)
                    rootKinds[scene] = "build scene";
                foreach (var include in settings.Includes)
                    rootKinds[include] = "always included (luna.json)";
                var roots = BuildTextureScanner.CollectRoots(settings);
                foreach (var root in roots.Where(r => !rootKinds.ContainsKey(r)))
                    rootKinds[root] = "Resources (loadable by name)";

                var pending = new Stack<string>(roots.Where(r => !IsExcluded(r)));
                while (pending.Count > 0)
                {
                    var path = pending.Pop();
                    if (!Shipped.Add(path) || BuildTextureScanner.IsScript(path))
                        continue;
                    foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                    {
                        if (dependency == path || IsExcluded(dependency) || BuildTextureScanner.IsScript(dependency))
                            continue;
                        List<string> list;
                        if (!users.TryGetValue(dependency, out list))
                            users[dependency] = list = new List<string>();
                        if (!list.Contains(path))
                            list.Add(path);
                        if (!Shipped.Contains(dependency))
                            pending.Push(dependency);
                    }
                }
            }

            bool IsExcluded(string path)
            {
                return settings.IsExcluded(path)
                    && !asIfShipped.Any(e => path == e.TrimEnd('/') || path.StartsWith(e.TrimEnd('/') + "/", StringComparison.Ordinal));
            }

            public List<Link> UsersOf(string path, HashSet<string> targets, HashSet<string> expanded,
                Dictionary<string, YamlReferenceLocator> locators, int depth)
            {
                var links = new List<Link>();
                List<string> list;
                if (!users.TryGetValue(path, out list))
                    return links;
                var guid = AssetDatabase.AssetPathToGUID(path);
                foreach (var user in list.Where(u => !targets.Contains(u)).OrderBy(u => u, StringComparer.Ordinal))
                {
                    var link = new Link { AssetPath = user, Where = Locate(user, guid, locators) };
                    rootKinds.TryGetValue(user, out link.RootKind);
                    if (!expanded.Add(user))
                        link.ShownElsewhere = users.ContainsKey(user);
                    else if (depth < MAX_DEPTH)
                        link.Users = UsersOf(user, targets, expanded, locators, depth + 1);
                    links.Add(link);
                }
                return links;
            }

            static List<string> Locate(string user, string guid, Dictionary<string, YamlReferenceLocator> locators)
            {
                var extension = Path.GetExtension(user).ToLowerInvariant();
                if (extension == ".fbx" || extension == ".obj" || extension == ".blend")
                    return new List<string> { "model import settings (material remap)" };
                if (!YamlReferenceLocator.CanRead(user))
                    return new List<string>();
                YamlReferenceLocator locator;
                if (!locators.TryGetValue(user, out locator))
                {
                    try
                    {
                        locator = new YamlReferenceLocator(Path.GetFullPath(user));
                    }
                    catch (Exception)
                    {
                        locator = null;
                    }
                    locators[user] = locator;
                }
                return locator != null ? locator.Find(guid) : new List<string>();
            }
        }
    }
}
