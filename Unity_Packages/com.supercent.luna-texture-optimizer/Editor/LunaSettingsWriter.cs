using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Writes per-texture and per-sound overrides through Luna's own in-memory settings, the same way the Luna window saves.
    // Editing luna.json directly does not work: Luna overwrites the file with its in-memory copy on its next save.
    public static class LunaSettingsWriter
    {
        const string EDITOR_ASSEMBLY = "EditorScripts";
        const string LUNA_DATA_TYPE = "Luna.Unity.EditorScripts.Interface.LunaData";
        const string TEXTURES_TYPE = "Textures";
        const string TEXTURE_OVERRIDE_TYPE = "TextureOverride";
        const string SOUNDS_TYPE = "Sounds";
        const string SOUND_OVERRIDE_TYPE = "SoundOverride";
        const string NOT_FOUND = "Luna's editor settings were not found. Open the Luna window once and try again.";
        const BindingFlags STATIC_FIELDS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags INSTANCE_FIELDS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Writes size, format, compression and quality from each entry's LunaRule. Entries without their own
        // override get one, copied from the rule that matches them today so only the edited values change.
        public static bool SaveRules(IList<TextureEntry> entries, string projectRoot, out string error)
        {
            var label = "Save to Luna: " + entries.Count + " texture(s)";
            var detail = LunaSaveHistory.Names(entries.Select(e => Path.GetFileName(e.AssetPath)));
            return Edit(LunaSettingsSection.Textures, label, detail, projectRoot, out error, luna =>
            {
                var textures = luna.Textures();
                var overrides = (IDictionary)textures.GetType().GetField("overridesDictionary").GetValue(textures);
                var overrideType = textures.GetType().Assembly.GetTypes().First(t => t.Name == TEXTURE_OVERRIDE_TYPE);
                var defaultRule = textures.GetType().GetField("default").GetValue(textures);
                var formats = (Array)overrideType.GetField("textureFormats", STATIC_FIELDS | BindingFlags.FlattenHierarchy).GetValue(null);
                var compressions = (Array)overrideType.GetField("textureCompressions", STATIC_FIELDS | BindingFlags.FlattenHierarchy).GetValue(null);

                foreach (var entry in entries)
                {
                    var target = overrides.Contains(entry.AssetPath) ? overrides[entry.AssetPath] : null;
                    if (target == null)
                    {
                        var savedRule = entry.SavedRule ?? entry.LunaRule;
                        var template = entry.IsLunaOverride && savedRule.name != null && overrides.Contains(savedRule.name)
                            ? overrides[savedRule.name]
                            : defaultRule;
                        target = Activator.CreateInstance(overrideType, template);
                        overrides[entry.AssetPath] = target;
                    }
                    var rule = entry.LunaRule;
                    overrideType.GetField("maxWidth").SetValue(target, rule.maxWidth);
                    overrideType.GetField("maxHeight").SetValue(target, rule.maxHeight);
                    overrideType.GetField("quality").SetValue(target, rule.quality);
                    overrideType.GetField("currentFormat").SetValue(target, IndexOf(formats, rule.format));
                    overrideType.GetField("currentCompression").SetValue(target, IndexOf(compressions, rule.compression));
                }
            });
        }

        // Writes bitrate, channels, script, ext (the format) and trim from each entry's LunaRule. Entries without their own override get one,
        // copied from the rule that matches them today so only the edited values change.
        public static bool SaveSoundRules(IList<SoundEntry> entries, string projectRoot, out string error)
        {
            var label = "Save to Luna: " + entries.Count + " sound(s)";
            var detail = LunaSaveHistory.Names(entries.Select(e => e.FileName + " " + LunaSoundFormat.Label(e.Format) + " " + e.LunaRule.bitrate + "k"));
            return EditSounds(label, detail, projectRoot, out error, (overrides, overrideType, defaultRule) =>
            {
                foreach (var entry in entries)
                {
                    var target = overrides.Contains(entry.AssetPath) ? overrides[entry.AssetPath] : null;
                    if (target == null)
                    {
                        var savedRule = entry.SavedRule ?? entry.LunaRule;
                        var template = entry.IsLunaOverride && savedRule.name != null && overrides.Contains(savedRule.name)
                            ? overrides[savedRule.name]
                            : defaultRule;
                        target = Activator.CreateInstance(overrideType, template);
                        overrideType.GetField("name").SetValue(target, entry.AssetPath);
                        overrides[entry.AssetPath] = target;
                    }
                    overrideType.GetField("bitrate").SetValue(target, entry.LunaRule.bitrate);
                    overrideType.GetField("channels").SetValue(target, entry.LunaRule.channels);
                    overrideType.GetField("script").SetValue(target, entry.LunaRule.script ?? string.Empty);
                    overrideType.GetField("ext").SetValue(target, entry.LunaRule.ext ?? string.Empty);
                    SetConverted(overrideType, target, "startTime", entry.LunaRule.startTime ?? LunaSoundPipeline.NO_TIME);
                    SetConverted(overrideType, target, "endTime", entry.LunaRule.endTime ?? LunaSoundPipeline.NO_TIME);
                    SetConverted(overrideType, target, "lengthInSeconds", entry.LunaRule.lengthInSeconds);
                }
            });
        }

        // Luna's override fields by name, converted to the field's own type (a missing field is left alone).
        static void SetConverted(Type type, object target, string name, object value)
        {
            var field = type.GetField(name);
            if (field != null)
                field.SetValue(target, Convert.ChangeType(value, field.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }

        // Stale overrides name files that no longer exist. moves: old name -> asset path the override should follow;
        // removals: names to delete. A move onto a path that already has its own override is skipped.
        public static bool RepairSoundOverrides(IDictionary<string, string> moves, IList<string> removals, string projectRoot, out string error)
        {
            var label = "Fix stale overrides: " + moves.Count + " moved, " + removals.Count + " removed";
            var detail = LunaSaveHistory.Names(moves.Keys.Concat(removals).Select(Path.GetFileName));
            return EditSounds(label, detail, projectRoot, out error, (overrides, overrideType, defaultRule) =>
            {
                foreach (var move in moves)
                {
                    if (!overrides.Contains(move.Key) || overrides.Contains(move.Value))
                        continue;
                    var target = overrides[move.Key];
                    overrideType.GetField("name").SetValue(target, move.Value);
                    overrides.Remove(move.Key);
                    overrides[move.Value] = target;
                }
                foreach (var name in removals)
                    overrides.Remove(name);
            });
        }

        static bool EditSounds(string label, string detail, string projectRoot, out string error, Action<IDictionary, Type, object> edit)
        {
            return Edit(LunaSettingsSection.Sounds, label, detail, projectRoot, out error, luna =>
            {
                var sounds = luna.Sounds();
                if (sounds == null)
                    throw new InvalidOperationException(NOT_FOUND);
                var overrides = (IDictionary)sounds.GetType().GetField("overridesDictionary").GetValue(sounds);
                var overrideType = sounds.GetType().Assembly.GetTypes().First(t => t.Name == SOUND_OVERRIDE_TYPE);
                edit(overrides, overrideType, sounds.GetType().GetField("default").GetValue(sounds));
            });
        }

        // Engine modules: included = Luna ships the module; excluded = Luna ships its stub.
        // Mecanim WASM has its own switch (enableMecanimWASM) besides its module entry.
        public static bool SaveModules(bool enableMecanimWasm, IDictionary<string, bool> moduleIncluded, string projectRoot, out string error)
        {
            var excluded = moduleIncluded.Where(m => !m.Value).Select(m => m.Key).OrderBy(n => n).ToList();
            var detail = "Mecanim WASM " + (enableMecanimWasm ? "on" : "off") + "; excluded: " + (excluded.Count > 0 ? LunaSaveHistory.Names(excluded) : "none");
            return Edit(LunaSettingsSection.Modules, "Apply to Luna: engine modules", detail, projectRoot, out error, luna =>
            {
                var unity = luna.Unity();
                unity.GetType().GetField("enableMecanimWASM", INSTANCE_FIELDS).SetValue(unity, enableMecanimWasm);
                var modules = (IList)unity.GetType().GetField("unusedModules", INSTANCE_FIELDS).GetValue(unity);
                foreach (var module in modules)
                {
                    var type = module.GetType();
                    bool included;
                    if (!moduleIncluded.TryGetValue((string)type.GetField("name").GetValue(module), out included))
                        continue;
                    type.GetField("excluded").SetValue(module, !included);
                    type.GetField("forceIncluded").SetValue(module, included);
                }
            });
        }

        // Asset paths or folders ("Assets/X/") Luna leaves out of the build: Luna drops every asset whose path starts
        // with an entry of unity.assets.excludes, unless an entry of unity.assets.includes covers it.
        public static bool SaveAssetExcludes(IList<string> add, IList<string> remove, string label, string projectRoot, out string error)
        {
            var detail = LunaSaveHistory.Names(add.Select(p => "+" + p).Concat(remove.Select(p => "-" + p)));
            return Edit(LunaSettingsSection.AssetExcludes, label, detail, projectRoot, out error, luna =>
            {
                var assets = luna.UnityAssets();
                var excludes = (List<string>)assets.GetType().GetField("excludes").GetValue(assets);
                foreach (var path in add)
                {
                    if (!excludes.Contains(path))
                        excludes.Add(path);
                }
                foreach (var path in remove)
                    excludes.Remove(path);
            });
        }

        // Puts one section back to a JSON snapshot from LunaSaveHistory. Recorded like a save, so it can be undone too.
        public static bool RestoreSection(LunaSettingsSection section, string json, string label, string projectRoot, out string error)
        {
            return Edit(section, label, null, projectRoot, out error, luna => luna.RestoreSection(section, json));
        }

        public static bool TryReadSection(LunaSettingsSection section, string projectRoot, out string json, out string error)
        {
            json = null;
            try
            {
                var luna = LunaState.Find();
                if (luna == null)
                {
                    error = NOT_FOUND;
                    return false;
                }
                json = luna.SectionJson(section);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        // Module and scene snapshots hold Luna's whole "unity" block, but only their own fields are compared and restored.
        public static bool SameSection(LunaSettingsSection section, string a, string b)
        {
            if (a == b)
                return true;
            if (!UNITY_SECTION_FIELDS.ContainsKey(section) || a == null || b == null)
                return false;
            var luna = LunaState.Find();
            return luna != null && luna.UnityFieldsKey(section, a) == luna.UnityFieldsKey(section, b);
        }

        // The scene Luna builds: it becomes the only (startup) scene; every other known scene is listed as disabled,
        // as Luna's window does. A scene missing from Build Settings is added there, because Luna lists scenes from it.
        public static bool SaveBuildScene(string scenePath, string projectRoot, out string error)
        {
            if (!EditorBuildSettings.scenes.Any(s => s.path == scenePath))
            {
                var list = EditorBuildSettings.scenes.ToList();
                list.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            return Edit(LunaSettingsSection.Scenes, "Build scene: " + Path.GetFileNameWithoutExtension(scenePath), scenePath, projectRoot, out error, luna =>
            {
                var unity = luna.Unity();
                var type = unity.GetType();
                var known = new List<string>();
                foreach (var name in new[] { "scenes", "disabledScenes" })
                {
                    var current = type.GetField(name, INSTANCE_FIELDS).GetValue(unity) as IEnumerable;
                    if (current != null)
                        known.AddRange(current.Cast<string>());
                }
                known.AddRange(EditorBuildSettings.scenes.Select(s => s.path));
                SetStrings(unity, "scenes", new List<string> { scenePath });
                SetStrings(unity, "disabledScenes", known.Where(p => p != scenePath).Distinct().ToList());
                type.GetField("startupScene", INSTANCE_FIELDS).SetValue(unity, 0);
            });
        }

        static void SetStrings(object target, string fieldName, List<string> values)
        {
            var field = target.GetType().GetField(fieldName, INSTANCE_FIELDS);
            field.SetValue(target, field.FieldType == typeof(string[]) ? (object)values.ToArray() : values);
        }

        static readonly Dictionary<LunaSettingsSection, string[]> UNITY_SECTION_FIELDS = new Dictionary<LunaSettingsSection, string[]>
        {
            { LunaSettingsSection.Modules, new[] { "enableMecanimWASM", "unusedModules" } },
            { LunaSettingsSection.Scenes, new[] { "scenes", "disabledScenes", "startupScene" } },
        };

        // Every write: refuse when luna.json differs from Luna's memory, edit Luna's memory, save it the way Luna does
        // and record the section before and after for undo.
        static bool Edit(LunaSettingsSection section, string label, string detail, string projectRoot, out string error, Action<LunaState> edit)
        {
            try
            {
                var luna = LunaState.Find();
                if (luna == null)
                {
                    error = NOT_FOUND;
                    return false;
                }
                var path = Path.Combine(projectRoot, LunaSettings.FILE_NAME);
                if (luna.Serialize() != File.ReadAllText(path))
                {
                    error = LunaSettings.FILE_NAME + " differs from Luna's loaded settings (edited outside Luna?). "
                        + "Reopen the Luna window so it reloads the file, then try again.";
                    return false;
                }

                var before = luna.SectionJson(section);
                edit(luna);
                var after = luna.SectionJson(section);
                luna.Save(path);
                if (before != after)
                    LunaSaveHistory.Add(projectRoot, section, label, detail, before, after);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        // Luna stores format and compression as indexes into tables of (display name, value) pairs.
        static int IndexOf(Array table, string value)
        {
            for (int i = 0; i < table.Length; i++)
            {
                var option = table.GetValue(i);
                var fields = option.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fields.Any(f => f.FieldType == typeof(string) && (string)f.GetValue(option) == value))
                    return i;
            }
            throw new ArgumentException("Luna has no texture option \"" + value + "\"");
        }

        // Luna keeps its settings in a static holder: the live LunaData, the last text it wrote and a change event.
        class LunaState
        {
            FieldInfo dataField;
            FieldInfo savedTextField;
            FieldInfo changedField;
            MethodInfo texturesGetter;
            MethodInfo soundsGetter;

            public static LunaState Find()
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == EDITOR_ASSEMBLY);
                var dataType = assembly != null ? assembly.GetType(LUNA_DATA_TYPE) : null;
                if (dataType == null)
                    return null;

                var holder = assembly.GetTypes().FirstOrDefault(t => t.IsAbstract && t.IsSealed
                    && t.GetFields(STATIC_FIELDS).Any(f => f.FieldType == dataType));
                if (holder == null)
                    return null;

                var fields = holder.GetFields(STATIC_FIELDS);
                var state = new LunaState
                {
                    dataField = fields.First(f => f.FieldType == dataType),
                    savedTextField = fields.FirstOrDefault(f => f.FieldType == typeof(string)),
                    changedField = fields.FirstOrDefault(f => f.FieldType == typeof(Action)),
                    texturesGetter = holder.GetMethods(STATIC_FIELDS)
                        .FirstOrDefault(m => m.ReturnType.Name == TEXTURES_TYPE && m.GetParameters().Length == 0),
                    soundsGetter = holder.GetMethods(STATIC_FIELDS)
                        .FirstOrDefault(m => m.ReturnType.Name == SOUNDS_TYPE && m.GetParameters().Length == 0),
                };
                if (state.savedTextField == null || state.changedField == null || state.texturesGetter == null)
                    return null;
                // The getter loads luna.json into memory when Luna has not done so yet.
                return state.Textures() != null && state.dataField.GetValue(null) != null ? state : null;
            }

            public object Data()
            {
                return dataField.GetValue(null);
            }

            public object Textures()
            {
                return texturesGetter.Invoke(null, null);
            }

            public object Sounds()
            {
                return soundsGetter != null ? soundsGetter.Invoke(null, null) : null;
            }

            public object Unity()
            {
                return Data().GetType().GetField("unity", INSTANCE_FIELDS).GetValue(Data());
            }

            public object UnityAssets()
            {
                var unity = Unity();
                return unity.GetType().GetField("assets", INSTANCE_FIELDS).GetValue(unity);
            }

            object Section(LunaSettingsSection section)
            {
                switch (section)
                {
                    case LunaSettingsSection.Textures: return Textures();
                    case LunaSettingsSection.Sounds: return Sounds();
                    case LunaSettingsSection.AssetExcludes: return UnityAssets();
                    default: return Unity();
                }
            }

            public string SectionJson(LunaSettingsSection section)
            {
                var value = Section(section);
                if (value == null)
                    throw new InvalidOperationException(NOT_FOUND);
                return JsonUtility.ToJson(value);
            }

            // Textures and Sounds rebuild their override dictionary from the JSON list (ISerializationCallbackReceiver).
            public void RestoreSection(LunaSettingsSection section, string json)
            {
                var live = Section(section);
                string[] fields;
                if (!UNITY_SECTION_FIELDS.TryGetValue(section, out fields))
                {
                    JsonUtility.FromJsonOverwrite(json, live);
                    return;
                }
                var snapshot = UnitySnapshot(json);
                foreach (var name in fields)
                {
                    var field = live.GetType().GetField(name, INSTANCE_FIELDS);
                    field.SetValue(live, field.GetValue(snapshot));
                }
            }

            public string UnityFieldsKey(LunaSettingsSection section, string unityJson)
            {
                var snapshot = UnitySnapshot(unityJson);
                var type = snapshot.GetType();
                var parts = new List<string>();
                foreach (var name in UNITY_SECTION_FIELDS[section])
                {
                    var value = type.GetField(name, INSTANCE_FIELDS).GetValue(snapshot);
                    var list = value as IList;
                    parts.Add(list != null
                        ? string.Join("|", list.Cast<object>().Select(item => item is string ? (string)item : JsonUtility.ToJson(item)))
                        : Convert.ToString(value));
                }
                return string.Join("#", parts);
            }

            object UnitySnapshot(string json)
            {
                var snapshot = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Unity().GetType());
                JsonUtility.FromJsonOverwrite(json, snapshot);
                return snapshot;
            }

            public string Serialize()
            {
                return JsonUtility.ToJson(dataField.GetValue(null), true);
            }

            public void Save(string path)
            {
                var text = Serialize();
                if (text == (string)savedTextField.GetValue(null))
                    return;
                File.WriteAllText(path, text);
                savedTextField.SetValue(null, text);
                var changed = (Action)changedField.GetValue(null);
                if (changed != null)
                    changed();
            }
        }
    }
}
