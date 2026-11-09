using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Size of every non-texture asset category for the scene Luna builds now, without a Luna build:
    // which objects ship comes from the references in the scene and the assets it pulls in; sizes come from the
    // size memory (exact, from any earlier measure), else a mesh's exact raw size times the typical compression,
    // else the category's typical size. Every asset says which of these it is.
    public static class SceneSizeEstimator
    {
        public const string REMEMBERED = "remembered";
        public const string CHANGED = "remembered, file changed";
        public const string ESTIMATED = "estimated";
        const string BUILTIN_GUID = "0000000000000000e000000000000000";
        const string BUILTIN_EXTRA_GUID = "0000000000000000f000000000000000";
        const string BUILTIN_EXTRA_PATH = "Resources/unity_builtin_extra";
        static readonly string[] CONSTANT_CATEGORIES = { "resources", "project-settings" };
        static readonly Regex REFERENCE = new Regex(@"\{fileID: (-?\d+), guid: (\w+), type: \d\}");
        static readonly Dictionary<long, string> BUILTIN_MESHES = new Dictionary<long, string>
        {
            { 10202, "Cube" }, { 10206, "Cylinder" }, { 10207, "Sphere" }, { 10208, "Capsule" }, { 10209, "Plane" }, { 10210, "Quad" },
        };
        static readonly int[] ATTRIBUTE_FLOATS = { 3, 3, 4, 4, 4, 4, 2, 2, 2, 2 };
        static readonly UnityEngine.Rendering.VertexAttribute[] ATTRIBUTES =
        {
            UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttribute.Normal, UnityEngine.Rendering.VertexAttribute.Tangent,
            UnityEngine.Rendering.VertexAttribute.Color, UnityEngine.Rendering.VertexAttribute.BlendIndices, UnityEngine.Rendering.VertexAttribute.BlendWeight,
            UnityEngine.Rendering.VertexAttribute.TexCoord0, UnityEngine.Rendering.VertexAttribute.TexCoord1, UnityEngine.Rendering.VertexAttribute.TexCoord2,
            UnityEngine.Rendering.VertexAttribute.TexCoord3,
        };

        public class Asset
        {
            public string Title;
            public string Path;
            public double Size;
            public string Tag;
        }

        public class Category
        {
            public string Title;
            public List<Asset> Assets = new List<Asset>();
        }

        struct Shipped
        {
            public string Category;
            public string Path;
            public string Title;
            public UnityEngine.Object Object;
            public bool Placed;     // prefab placed in a scene or prefab (Luna lists only some of these, by name)
        }

        // Categories other than textures (the Overview predicts those live). halfPrecisionMeshes: Luna's mesh rule.
        public static List<Category> Estimate(LunaSettings settings, string network, AssetSizeMemory memory, string projectRoot, bool halfPrecisionMeshes)
        {
            var shipped = CollectShipped(settings);
            var meshFactor = MeshCompression(shipped, network, memory, halfPrecisionMeshes);
            var categories = new Dictionary<string, Category>();
            foreach (var item in shipped)
            {
                if (item.Category == "textures")
                    continue;
                var asset = SizeOf(item, network, memory, projectRoot, meshFactor, halfPrecisionMeshes);
                // Placed prefabs Luna never listed are part of the scene data, not separate prefab entries.
                if (item.Placed && asset.Tag == ESTIMATED)
                    continue;
                Category category;
                if (!categories.TryGetValue(item.Category, out category))
                    categories[item.Category] = category = new Category { Title = item.Category };
                category.Assets.Add(asset);
            }
            // A placed model whose renderer uses an overriding mesh of the same name from another model.
            Category meshes;
            if (categories.TryGetValue("meshes", out meshes))
                meshes.Assets.RemoveAll(a => a.Tag == ESTIMATED && meshes.Assets.Any(o => o != a && o.Title == a.Title && o.Path != a.Path && o.Tag != ESTIMATED));
            // Luna's resource index and project settings do not depend on the scene: reuse what was measured.
            foreach (var title in CONSTANT_CATEGORIES)
            {
                var entries = memory.ForCategory(network, title);
                if (entries.Count == 0)
                    continue;
                var category = new Category { Title = title };
                category.Assets.AddRange(entries.Select(e => new Asset { Title = e.title, Path = e.path, Size = e.size, Tag = REMEMBERED }));
                categories[title] = category;
            }
            return categories.Values.OrderByDescending(c => c.Assets.Sum(a => a.Size)).ToList();
        }

        static Asset SizeOf(Shipped item, string network, AssetSizeMemory memory, string projectRoot, double meshFactor, bool halfPrecision)
        {
            var asset = new Asset { Title = item.Title, Path = item.Path };
            var remembered = Remembered(item, network, memory);
            if (remembered != null)
            {
                asset.Size = remembered.size;
                asset.Tag = remembered.fileTicks != 0 && remembered.fileTicks != AssetSizeMemory.FileTicks(projectRoot, item.Path) ? CHANGED : REMEMBERED;
                return asset;
            }
            var mesh = item.Object as Mesh;
            if (mesh != null && meshFactor > 0)
                asset.Size = RawMeshBytes(mesh, halfPrecision) * meshFactor;
            else if (item.Category == "scenes")
                asset.Size = SceneBytes(item.Path, network, memory, projectRoot);
            else
                asset.Size = memory.Median(network, item.Category);
            asset.Tag = ESTIMATED;
            return asset;
        }

        // A scene's data grows with its file: the measured scenes' playable bytes per scene-file byte.
        static double SceneBytes(string path, string network, AssetSizeMemory memory, string projectRoot)
        {
            var ratios = memory.ForCategory(network, "scenes")
                .Select(e => new { e.size, file = Path.Combine(projectRoot, e.path ?? string.Empty) })
                .Where(e => File.Exists(e.file) && new FileInfo(e.file).Length > 0)
                .Select(e => e.size / new FileInfo(e.file).Length).OrderBy(r => r).ToList();
            var full = Path.Combine(projectRoot, path);
            if (ratios.Count == 0 || !File.Exists(full))
                return memory.Median(network, "scenes");
            return new FileInfo(full).Length * ratios[ratios.Count / 2];
        }

        // Luna's stats name some assets by full path (scriptable objects) or by name only (nested prefabs).
        static AssetSizeMemory.Entry Remembered(Shipped item, string network, AssetSizeMemory memory)
        {
            var exact = memory.Find(network, item.Category, item.Path, item.Title);
            if (exact.Count == 1)
                return exact[0];
            var byPath = memory.Find(network, item.Category, item.Path, item.Path);
            if (byPath.Count == 1)
                return byPath[0];
            var byName = memory.Find(network, item.Category, item.Title, item.Title);
            return byName.Count == 1 ? byName[0] : null;
        }

        // Luna's mesh blob: half (or full) precision vertex floats, 16-bit indices, 6 bytes per blend shape delta
        // (verified byte-exact on 73 meshes, 2026-11-06). The playable size then depends on how well it compresses.
        public static double RawMeshBytes(Mesh mesh, bool halfPrecision)
        {
            var floatsPerVertex = 0;
            for (int i = 0; i < ATTRIBUTES.Length; i++)
            {
                if (mesh.HasVertexAttribute(ATTRIBUTES[i]))
                    floatsPerVertex += ATTRIBUTE_FLOATS[i];
            }
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                indices += mesh.GetIndexCount(i);
            long blendDeltas = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                blendDeltas += (long)mesh.GetBlendShapeFrameCount(i) * mesh.vertexCount * 3;
            var floatBytes = halfPrecision ? 2 : 4;
            return (double)mesh.vertexCount * floatsPerVertex * floatBytes + indices * 2 + blendDeltas * 3 * floatBytes;
        }

        // Typical playable size per raw mesh byte on this network, from remembered meshes of this scene.
        static double MeshCompression(List<Shipped> shipped, string network, AssetSizeMemory memory, bool halfPrecision)
        {
            var ratios = new List<double>();
            foreach (var item in shipped.Where(s => s.Object is Mesh))
            {
                var remembered = Remembered(item, network, memory);
                var raw = RawMeshBytes((Mesh)item.Object, halfPrecision);
                if (remembered != null && raw > 0)
                    ratios.Add(remembered.size / raw);
            }
            if (ratios.Count == 0)
                return 0;
            ratios.Sort();
            return ratios[ratios.Count / 2];
        }

        // ---------------------------------------------------------------- what the scene ships

        static List<Shipped> CollectShipped(LunaSettings settings)
        {
            HashSet<string> files;
            BlastRadius.DirectUsers(settings, out files);
            var result = new Dictionary<string, Shipped>();
            var objectsByFile = new Dictionary<string, Dictionary<long, UnityEngine.Object>>();

            foreach (var scene in settings.Scenes.Where(File.Exists))
                Add(result, new Shipped { Category = "scenes", Path = scene, Title = Path.GetFileNameWithoutExtension(scene) });

            // Resources and always-included assets ship whole.
            foreach (var root in BuildTextureScanner.CollectRoots(settings).Where(r => !settings.Scenes.Contains(r)))
            {
                var main = AssetDatabase.LoadMainAssetAtPath(root);
                if (main != null)
                    AddObject(result, main, root, false);
            }

            foreach (var file in files.Where(YamlReferenceLocator.CanRead))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.TrimStart(' ');
                    if (trimmed.StartsWith("- target:") || trimmed.StartsWith("m_CorrespondingSourceObject:") || trimmed.StartsWith("m_PrefabAsset:"))
                        continue;
                    var viaPrefabInstance = trimmed.StartsWith("m_SourcePrefab:");
                    foreach (Match match in REFERENCE.Matches(line))
                    {
                        long fileId;
                        if (!long.TryParse(match.Groups[1].Value, out fileId))
                            continue;
                        var guid = match.Groups[2].Value;
                        if (guid == BUILTIN_GUID)
                        {
                            string name;
                            if (BUILTIN_MESHES.TryGetValue(fileId, out name))
                            {
                                var mesh = Resources.GetBuiltinResource<Mesh>(name + ".fbx");
                                if (mesh != null)
                                    Add(result, new Shipped { Category = "meshes", Path = "Library/unity default resources/" + name, Title = name, Object = mesh });
                            }
                            continue;
                        }
                        if (guid == BUILTIN_EXTRA_GUID)
                        {
                            var builtin = Resolve(objectsByFile, BUILTIN_EXTRA_PATH, fileId);
                            if (builtin != null)
                                AddObject(result, builtin, BUILTIN_EXTRA_PATH + "/" + builtin.name, false);
                            continue;
                        }
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(path) || !files.Contains(path))
                            continue;
                        // m_SourcePrefab names the prefab asset itself (fileID 100100000), not its root GameObject.
                        var target = viaPrefabInstance ? AssetDatabase.LoadMainAssetAtPath(path) : Resolve(objectsByFile, path, fileId);
                        if (target == null)
                            continue;
                        AddObject(result, target, path, viaPrefabInstance);
                        // A placed model brings the meshes of its own renderers (not referenced from the scene file).
                        if (viaPrefabInstance && target is GameObject && AssetImporter.GetAtPath(path) is ModelImporter)
                            AddModelMeshes(result, (GameObject)target, path);
                    }
                }
            }
            return result.Values.ToList();
        }

        static UnityEngine.Object Resolve(Dictionary<string, Dictionary<long, UnityEngine.Object>> cache, string path, long fileId)
        {
            Dictionary<long, UnityEngine.Object> objects;
            if (!cache.TryGetValue(path, out objects))
            {
                objects = new Dictionary<long, UnityEngine.Object>();
                foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    string guid;
                    long localId;
                    if (candidate != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out guid, out localId))
                        objects[localId] = candidate;
                }
                cache[path] = objects;
            }
            UnityEngine.Object found;
            return objects.TryGetValue(fileId, out found) ? found : null;
        }

        static void AddModelMeshes(Dictionary<string, Shipped> result, GameObject model, string path)
        {
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    Add(result, new Shipped { Category = "meshes", Path = path, Title = filter.sharedMesh.name, Object = filter.sharedMesh });
            }
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                    Add(result, new Shipped { Category = "meshes", Path = path, Title = skinned.sharedMesh.name, Object = skinned.sharedMesh });
            }
        }

        // Luna's categories by object type. Prefabs count when referenced as an asset (a script field) and when
        // placed (Luna lists placed ones by name). A font asset brings its own material sub-assets.
        static void AddObject(Dictionary<string, Shipped> result, UnityEngine.Object target, string path, bool viaPrefabInstance)
        {
            string category = null;
            var title = target.name;
            if (target is Mesh)
                category = "meshes";
            else if (target is AnimationClip)
                category = target.name.StartsWith("__preview__", StringComparison.Ordinal) ? null : "animation-clips";
            else if (target is Material)
                category = "materials";
            else if (target is Sprite)
                category = "sprites";
            else if (target is Texture)
                category = "textures";
            else if (target is AudioClip)
                category = "sounds";
            else if (target is Font)
                category = "fonts";
            else if (target is AnimatorOverrideController)
                category = "animator-override-controllers";
            else if (target is RuntimeAnimatorController)
                category = "animator-controllers";
            else if (target is AvatarMask)
                category = "avatar-masks";
            else if (target is TextAsset)
                category = "text-assets";
            else if (target is Shader)
                category = "shaders";
            else if (target is ScriptableObject && !(target is MonoScript))
            {
                category = "scriptable-objects";
                title = path;
                foreach (var material in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Material>())
                    Add(result, new Shipped { Category = "materials", Path = path, Title = material.name, Object = material });
            }
            else if (target is GameObject && path.EndsWith(".prefab", StringComparison.Ordinal) && ((GameObject)target).transform.parent == null)
                category = "prefabs";
            if (category == null)
                return;
            var item = new Shipped { Category = category, Path = path, Title = title, Object = target, Placed = viaPrefabInstance && category == "prefabs" };
            Shipped existing;
            // Referenced as an asset anywhere wins over placed.
            if (result.TryGetValue(category + "|" + path + "|" + title, out existing) && !existing.Placed)
                item.Placed = false;
            Add(result, item);
        }

        static void Add(Dictionary<string, Shipped> result, Shipped item)
        {
            result[item.Category + "|" + item.Path + "|" + item.Title] = item;
        }
    }
}
