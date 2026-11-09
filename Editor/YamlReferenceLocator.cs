using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Finds where a text-serialized Unity file (scene, prefab, material, ScriptableObject…) references another asset,
    // as readable "GameObject path › Component.field" lines. Reads the YAML text; nothing is loaded or opened.
    public class YamlReferenceLocator
    {
        static readonly Regex HEADER = new Regex(@"^--- !u!(\d+) &(-?\d+)( stripped)?");
        static readonly Regex NAME = new Regex(@"^  m_Name: (.*)$");
        static readonly Regex GAME_OBJECT = new Regex(@"^  m_GameObject: \{fileID: (-?\d+)\}");
        static readonly Regex FATHER = new Regex(@"^  m_Father: \{fileID: (-?\d+)\}");
        static readonly Regex PREFAB_INSTANCE = new Regex(@"^  m_PrefabInstance: \{fileID: (-?\d+)\}");
        static readonly Regex TRANSFORM_PARENT = new Regex(@"^    m_TransformParent: \{fileID: (-?\d+)\}");
        static readonly Regex SOURCE_PREFAB = new Regex(@"^  m_SourcePrefab: \{fileID: -?\d+, guid: (\w+)");
        static readonly Regex SCRIPT = new Regex(@"^  m_Script: \{fileID: -?\d+, guid: (\w+)");
        static readonly Regex PROPERTY_PATH = new Regex(@"^\s+propertyPath: (.*)$");
        static readonly Regex TARGET = new Regex(@"^\s+- target: \{fileID: (-?\d+), guid: (\w+)");
        static readonly Regex NAME_OVERRIDE = new Regex(@"- target: \{fileID: (-?\d+), guid: \w+, type: \d\}\s*\n\s+propertyPath: m_Name\s*\n\s+value: (.*)");
        static readonly Regex KEY = new Regex(@"^([^\s:{][^:]*):");
        const int MAX_DEPTH = 32;

        static readonly Dictionary<int, string> CLASS_NAMES = new Dictionary<int, string>
        {
            { 1, "GameObject" }, { 4, "Transform" }, { 20, "Camera" }, { 21, "Material" }, { 23, "MeshRenderer" },
            { 33, "MeshFilter" }, { 54, "Rigidbody" }, { 64, "MeshCollider" }, { 74, "AnimationClip" }, { 82, "AudioSource" },
            { 91, "AnimatorController" }, { 95, "Animator" }, { 108, "Light" }, { 111, "Animation" }, { 114, "MonoBehaviour" },
            { 137, "SkinnedMeshRenderer" }, { 198, "ParticleSystem" }, { 199, "ParticleSystemRenderer" },
            { 212, "SpriteRenderer" }, { 221, "AnimatorOverrideController" }, { 222, "CanvasRenderer" }, { 224, "RectTransform" },
            { 1001, "PrefabInstance" }, { 1101, "AnimatorStateTransition" }, { 1102, "AnimatorState" }, { 1107, "AnimatorStateMachine" },
        };

        class Doc
        {
            public int ClassId;
            public string FileId;
            public bool Stripped;
            public int Start;
            public int End;
            public string Name;
            public string GameObject;
            public string Father;
            public string PrefabInstance;
            public string TransformParent;
            public string SourcePrefabGuid;
            public string ScriptGuid;
        }

        readonly string[] lines;
        readonly List<Doc> docs = new List<Doc>();
        readonly Dictionary<string, Doc> byFileId = new Dictionary<string, Doc>();
        readonly Dictionary<string, Doc> transformOf = new Dictionary<string, Doc>();   // GameObject fileId -> its Transform
        readonly string text;
        readonly Dictionary<string, YamlReferenceLocator> sources = new Dictionary<string, YamlReferenceLocator>();   // prefab guid -> its file

        public YamlReferenceLocator(string fullPath)
        {
            text = File.ReadAllText(fullPath);
            lines = text.Split('\n');
            Doc current = null;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                lines[i] = line;
                var header = HEADER.Match(line);
                if (header.Success)
                {
                    if (current != null)
                        current.End = i;
                    current = new Doc { ClassId = int.Parse(header.Groups[1].Value), FileId = header.Groups[2].Value, Stripped = header.Groups[3].Success, Start = i };
                    docs.Add(current);
                    byFileId[current.FileId] = current;
                    continue;
                }
                if (current == null)
                    continue;
                Capture(NAME, line, ref current.Name);
                Capture(GAME_OBJECT, line, ref current.GameObject);
                Capture(FATHER, line, ref current.Father);
                Capture(PREFAB_INSTANCE, line, ref current.PrefabInstance);
                Capture(TRANSFORM_PARENT, line, ref current.TransformParent);
                Capture(SOURCE_PREFAB, line, ref current.SourcePrefabGuid);
                Capture(SCRIPT, line, ref current.ScriptGuid);
            }
            if (current != null)
                current.End = lines.Length;
            foreach (var doc in docs.Where(d => (d.ClassId == 4 || d.ClassId == 224) && d.GameObject != null))
                transformOf[doc.GameObject] = doc;
        }

        public static bool CanRead(string assetPath)
        {
            var extension = Path.GetExtension(assetPath).ToLowerInvariant();
            return extension == ".unity" || extension == ".prefab" || extension == ".mat" || extension == ".asset"
                || extension == ".controller" || extension == ".overridecontroller" || extension == ".anim" || extension == ".playable"
                || extension == ".mask" || extension == ".guiskin" || extension == ".flare" || extension == ".spriteatlas";
        }

        // One line per reference to the asset with this guid.
        public List<string> Find(string guid)
        {
            var found = new List<string>();
            var needle = "guid: " + guid;
            foreach (var doc in docs)
            {
                for (int i = doc.Start + 1; i < doc.End; i++)
                {
                    if (lines[i].Contains(needle) && !IsBookkeeping(lines[i]))
                        found.Add(Describe(doc, i));
                }
            }
            return found.Distinct().ToList();
        }

        // Lines that name the source prefab of an override or a stripped object: Unity bookkeeping, not a use.
        static bool IsBookkeeping(string line)
        {
            var trimmed = line.TrimStart(' ');
            return trimmed.StartsWith("- target:") || trimmed.StartsWith("m_CorrespondingSourceObject:") || trimmed.StartsWith("m_PrefabAsset:");
        }

        string Describe(Doc doc, int line)
        {
            if (doc.ClassId == 1001)
            {
                var instance = InstanceName(doc);
                if (SOURCE_PREFAB.IsMatch(lines[line]))
                    return instance + " (prefab instance)";
                for (int i = line; i > doc.Start && i > line - 4; i--)
                {
                    var property = PROPERTY_PATH.Match(lines[i]);
                    if (!property.Success)
                        continue;
                    var target = TARGET.Match(lines[i - 1]);
                    var component = target.Success ? OwnerInSource(target.Groups[2].Value, target.Groups[1].Value, instance) : instance;
                    return component + "." + Pretty(property.Groups[1].Value);
                }
                return instance;
            }

            var field = FieldAt(doc, line);
            if (doc.GameObject != null)
                return GameObjectPath(doc.GameObject, 0) + " › " + ComponentName(doc) + (field.Length > 0 ? "." + field : string.Empty);
            var owner = doc.Name != null && doc.ClassId != 21 ? doc.Name + " (" + ComponentName(doc) + ")" : ComponentName(doc);
            return field.Length > 0 ? owner + " › " + field : owner;
        }

        // Keys from the reference line up to the document's top level, e.g. _decorFlowPrefabs[3] or _BaseMap.
        // Unity writes list items at the same indent as their key, so a parent is a line that is less indented,
        // or equally indented but not a list item when the current line is one.
        string FieldAt(Doc doc, int line)
        {
            var keys = new List<string>();
            int indent;
            bool isItem;
            var key = ParseLine(lines[line], out indent, out isItem);
            int? index = null;
            if (key != null)
                keys.Add(key);
            else if (isItem)
                index = CountItemsBefore(doc, line, indent);

            for (int i = line - 1; i > doc.Start; i--)
            {
                int parentIndent;
                bool parentIsItem;
                var parentKey = ParseLine(lines[i], out parentIndent, out parentIsItem);
                var isParent = parentIndent < indent || (parentIndent == indent && isItem && !parentIsItem);
                if (!isParent || parentKey == null)
                    continue;
                keys.Add(index.HasValue ? parentKey + "[" + index.Value + "]" : parentKey);
                index = null;
                indent = parentIndent;
                isItem = parentIsItem;
                if (indent <= 2 && !isItem)
                    break;
            }
            keys.Reverse();
            var meaningful = keys.Where(k => !k.StartsWith("m_") && k != "data" && k != "Array").ToList();
            return string.Join(".", meaningful.Count > 0 ? meaningful : keys.Take(1));
        }

        // "    - _BaseMap:" -> key _BaseMap, item; "  - {fileID: …}" -> no key, item.
        static string ParseLine(string raw, out int indent, out bool isItem)
        {
            var trimmed = raw.TrimStart(' ');
            indent = raw.Length - trimmed.Length;
            isItem = trimmed.StartsWith("- ");
            var body = isItem ? trimmed.Substring(2) : trimmed;
            var match = KEY.Match(body);
            return match.Success ? match.Groups[1].Value : null;
        }

        int CountItemsBefore(Doc doc, int line, int indent)
        {
            var count = 0;
            for (int i = line - 1; i > doc.Start; i--)
            {
                int currentIndent;
                bool currentIsItem;
                ParseLine(lines[i], out currentIndent, out currentIsItem);
                if (currentIndent < indent || (currentIndent == indent && !currentIsItem))
                    break;
                if (currentIndent == indent)
                    count++;
            }
            return count;
        }

        string GameObjectPath(string gameObjectId, int depth)
        {
            Doc gameObject;
            var name = byFileId.TryGetValue(gameObjectId, out gameObject) && gameObject.Name != null ? gameObject.Name : "?";
            Doc transform;
            if (depth > MAX_DEPTH || !transformOf.TryGetValue(gameObjectId, out transform))
                return name;
            var parent = ParentPath(transform.Father, depth);
            return parent.Length > 0 ? parent + "/" + name : name;
        }

        // A transform's parent: a normal GameObject, or a stripped transform that stands for a prefab instance.
        string ParentPath(string fatherId, int depth)
        {
            Doc father;
            if (fatherId == null || fatherId == "0" || !byFileId.TryGetValue(fatherId, out father) || depth > MAX_DEPTH)
                return string.Empty;
            if (father.Stripped && father.PrefabInstance != null)
            {
                Doc instance;
                return byFileId.TryGetValue(father.PrefabInstance, out instance) ? InstanceName(instance) : "?";
            }
            return father.GameObject != null ? GameObjectPath(father.GameObject, depth + 1) : string.Empty;
        }

        // The instance's own name: an m_Name override on the source prefab's root, else the prefab's file name.
        string InstanceName(Doc instance)
        {
            var name = instance.SourcePrefabGuid != null ? Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(instance.SourcePrefabGuid)) : "?";
            var source = instance.SourcePrefabGuid != null ? Source(instance.SourcePrefabGuid) : null;
            var root = source != null ? source.RootGameObjectId() : null;
            var block = string.Join("\n", lines, instance.Start, instance.End - instance.Start);
            foreach (Match overridden in NAME_OVERRIDE.Matches(block))
            {
                if (overridden.Groups[1].Value == root)
                    name = overridden.Groups[2].Value.Trim();
            }
            var parent = ParentPath(instance.TransformParent, 0);
            return parent.Length > 0 ? parent + "/" + name : name;
        }

        // "Game/Map/DirtyRiver › TrapStackSpawner" for a component inside the instance's source prefab.
        string OwnerInSource(string prefabGuid, string fileId, string instancePath)
        {
            var source = Source(prefabGuid);
            string owner;
            return source != null && source.TryDescribeObject(fileId, instancePath, 0, out owner) ? owner : instancePath;
        }

        // Objects of a nested prefab get the id (instance id XOR source id) & 0x7FFF…, so an id missing from this file
        // is looked up in each nested prefab instance's source.
        bool TryDescribeObject(string fileId, string instancePath, int depth, out string owner)
        {
            owner = null;
            Doc doc;
            if (byFileId.TryGetValue(fileId, out doc) && !doc.Stripped)
            {
                var path = instancePath;
                if (doc.GameObject != null)
                {
                    var inner = GameObjectPath(doc.GameObject, 0);
                    var slash = inner.IndexOf('/');
                    path = slash >= 0 ? instancePath + inner.Substring(slash) : instancePath;
                }
                owner = path + " › " + ComponentName(doc);
                return true;
            }
            long id;
            if (depth > 4 || !long.TryParse(fileId, out id))
                return false;
            foreach (var instance in docs.Where(d => d.ClassId == 1001 && d.SourcePrefabGuid != null))
            {
                long instanceId;
                if (!long.TryParse(instance.FileId, out instanceId))
                    continue;
                var sourceId = ((id ^ instanceId) & long.MaxValue).ToString();
                var nested = Source(instance.SourcePrefabGuid);
                if (nested == null)
                    continue;
                var nestedPath = instancePath + InstanceName(instance).Substring(Math.Max(0, InstancePathPrefixLength(instance)));
                if (nested.TryDescribeObject(sourceId, nestedPath, depth + 1, out owner))
                    return true;
            }
            return false;
        }

        // InstanceName returns "parent/…/name" inside this file; the part after this file's root replaces the root name.
        int InstancePathPrefixLength(Doc instance)
        {
            var full = InstanceName(instance);
            var slash = full.IndexOf('/');
            return slash >= 0 ? slash : full.Length;
        }

        YamlReferenceLocator Source(string prefabGuid)
        {
            YamlReferenceLocator source;
            if (sources.TryGetValue(prefabGuid, out source))
                return source;
            var path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            try
            {
                source = !string.IsNullOrEmpty(path) && File.Exists(path) ? new YamlReferenceLocator(Path.GetFullPath(path)) : null;
            }
            catch (IOException)
            {
                source = null;
            }
            sources[prefabGuid] = source;
            return source;
        }

        // The prefab's root: the GameObject whose transform has no parent.
        string RootGameObjectId()
        {
            var root = docs.FirstOrDefault(d => (d.ClassId == 4 || d.ClassId == 224) && !d.Stripped && (d.Father == null || d.Father == "0") && d.GameObject != null);
            return root != null ? root.GameObject : null;
        }

        static string ComponentName(Doc doc)
        {
            if (doc.ScriptGuid != null)
            {
                var script = AssetDatabase.GUIDToAssetPath(doc.ScriptGuid);
                if (!string.IsNullOrEmpty(script))
                    return Path.GetFileNameWithoutExtension(script);
            }
            string name;
            return CLASS_NAMES.TryGetValue(doc.ClassId, out name) ? name : "class " + doc.ClassId;
        }

        // _decorFlowPrefabs.Array.data[3] -> _decorFlowPrefabs[3]
        static string Pretty(string propertyPath)
        {
            return propertyPath.Replace(".Array.data[", "[");
        }

        static void Capture(Regex regex, string line, ref string value)
        {
            if (value != null)
                return;
            var match = regex.Match(line);
            if (match.Success)
                value = match.Groups[1].Value.Trim();
        }
    }
}
