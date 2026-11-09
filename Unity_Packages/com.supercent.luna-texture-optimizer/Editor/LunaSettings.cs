using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    [Serializable]
    public class LunaTextureRule
    {
        public int maxWidth;
        public int maxHeight;
        public string format;
        public string compression;
        public int quality;
        public string script;
        public string ext;
        public string name;

        public LunaTextureRule Clone()
        {
            return (LunaTextureRule)MemberwiseClone();
        }
    }

    [Serializable]
    public class LunaSoundRule
    {
        public int channels;
        public string startTime;
        public string endTime;
        public float lengthInSeconds;
        public int bitrate;
        public string script;
        public string ext;
        public string name;

        public LunaSoundRule Clone()
        {
            return (LunaSoundRule)MemberwiseClone();
        }
    }

    // Read-only view of the project's luna.json. Luna keeps all of its settings in this file
    // (the Luna window saves its in-memory copy back to it whenever something changes).
    public class LunaSettings
    {
        public const string FILE_NAME = "luna.json";
        const string RESOURCES_EXPORT = "resources";
        const string ASSETS_PREFIX = "Assets/";

        [Serializable]
        class Root
        {
            public UnitySection unity;
            public AssetsSection assets;
        }

        [Serializable]
        class UnitySection
        {
            public string[] scenes;
            public UnityAssetsSection assets;
            public bool enableLunaCompilerV2;
        }

        [Serializable]
        class UnityAssetsSection
        {
            public string[] export;
            public string[] excludes;
            public string[] includes;
        }

        [Serializable]
        class AssetsSection
        {
            public RulesSection rules;
        }

        [Serializable]
        class RulesSection
        {
            public TextureRulesSection texture;
            public SoundRulesSection sound;
        }

        [Serializable]
        class TextureRulesSection
        {
            public LunaTextureRule @default;
            public LunaTextureRule[] overrides;
        }

        [Serializable]
        class SoundRulesSection
        {
            public LunaSoundRule @default;
            public LunaSoundRule[] overrides;
        }

        public string FilePath { get; private set; }
        public IReadOnlyList<string> Scenes { get; private set; }
        public IReadOnlyList<string> Includes { get; private set; }
        public IReadOnlyList<string> Excludes { get; private set; }
        public bool ExportsResources { get; private set; }
        // Luna Compiler V2: scripts are compiled by Unity's C# compiler and decompiled before Bridge compiles them.
        public bool UsesCompilerV2 { get; private set; }
        public LunaTextureRule DefaultRule { get; private set; }
        public LunaSoundRule DefaultSoundRule { get; private set; }

        readonly List<LunaTextureRule> sortedOverrides = new List<LunaTextureRule>();
        readonly List<LunaSoundRule> sortedSoundOverrides = new List<LunaSoundRule>();
        readonly List<Regex> excludePatterns = new List<Regex>();

        // The same settings with other build scenes (to estimate a scene without switching Luna to it).
        public LunaSettings ForScenes(IReadOnlyList<string> scenes)
        {
            var copy = (LunaSettings)MemberwiseClone();
            copy.Scenes = scenes;
            return copy;
        }

        public static LunaSettings Load(string projectRoot)
        {
            var path = Path.Combine(projectRoot, FILE_NAME);
            if (!File.Exists(path))
                return null;

            var root = JsonUtility.FromJson<Root>(File.ReadAllText(path));
            var settings = new LunaSettings { FilePath = path };
            var unityAssets = root.unity != null ? root.unity.assets : null;

            settings.Scenes = root.unity != null && root.unity.scenes != null ? root.unity.scenes : new string[0];
            settings.Includes = unityAssets != null && unityAssets.includes != null ? unityAssets.includes : new string[0];
            settings.Excludes = unityAssets != null && unityAssets.excludes != null ? unityAssets.excludes : new string[0];
            settings.ExportsResources = unityAssets != null && unityAssets.export != null
                && Array.IndexOf(unityAssets.export, RESOURCES_EXPORT) >= 0;
            settings.UsesCompilerV2 = root.unity != null && root.unity.enableLunaCompilerV2;

            if (unityAssets != null && unityAssets.excludes != null)
            {
                foreach (var glob in unityAssets.excludes)
                    settings.excludePatterns.Add(GlobToRegex(glob));
            }

            var textureRules = root.assets != null && root.assets.rules != null ? root.assets.rules.texture : null;
            settings.DefaultRule = textureRules != null && textureRules.@default != null ? textureRules.@default : new LunaTextureRule();
            if (textureRules != null && textureRules.overrides != null)
            {
                settings.sortedOverrides.AddRange(textureRules.overrides);
                settings.sortedOverrides.Sort((a, b) => CompareRuleNames(a.name, b.name));
            }

            var soundRules = root.assets != null && root.assets.rules != null ? root.assets.rules.sound : null;
            settings.DefaultSoundRule = soundRules != null && soundRules.@default != null ? soundRules.@default : new LunaSoundRule();
            if (soundRules != null && soundRules.overrides != null)
            {
                settings.sortedSoundOverrides.AddRange(soundRules.overrides);
                settings.sortedSoundOverrides.Sort((a, b) => CompareRuleNames(a.name, b.name));
            }
            return settings;
        }

        public bool IsExcluded(string assetPath)
        {
            foreach (var pattern in excludePatterns)
            {
                if (pattern.IsMatch(assetPath))
                    return true;
            }
            return false;
        }

        // Same lookup as Luna's pipeline: the first matching override in sorted order, else the default rule.
        public LunaTextureRule RuleFor(string assetPath, out bool isOverride)
        {
            foreach (var rule in sortedOverrides)
            {
                if (RuleNameMatches(assetPath, rule.name))
                {
                    isOverride = true;
                    return rule;
                }
            }
            isOverride = false;
            return DefaultRule;
        }

        // Luna looks sound rules up the same way as texture rules.
        public LunaSoundRule SoundRuleFor(string assetPath, out bool isOverride)
        {
            foreach (var rule in sortedSoundOverrides)
            {
                if (RuleNameMatches(assetPath, rule.name))
                {
                    isOverride = true;
                    return rule;
                }
            }
            isOverride = false;
            return DefaultSoundRule;
        }

        public IReadOnlyList<LunaSoundRule> SoundOverrides
        {
            get { return sortedSoundOverrides; }
        }

        // Overrides naming an asset file that no longer exists (moved, renamed or deleted): Luna silently ignores them.
        // Built-in names such as "Resources/unity_builtin_extra/..." are not files and never stale.
        public static bool IsStaleOverride(string ruleName, string projectRoot)
        {
            return ruleName != null && ruleName.StartsWith(ASSETS_PREFIX) && !File.Exists(Path.Combine(projectRoot, ruleName));
        }

        // Override names are an exact asset path, "*text" (path contains text) or "^regex".
        static bool RuleNameMatches(string assetPath, string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName))
                return false;
            if (ruleName.StartsWith("*"))
                return assetPath.Contains(ruleName.Substring(1));
            if (ruleName.StartsWith("^"))
                return Regex.IsMatch(assetPath, ruleName.Substring(1));
            return assetPath == ruleName;
        }

        // Luna sorts exact paths before "*" patterns before "^" patterns; among exact paths a longer path
        // sharing a prefix goes first.
        static int CompareRuleNames(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;
            var kindA = RuleNameKind(a);
            var kindB = RuleNameKind(b);
            if (kindA != 0 || kindB != 0)
                return kindA - kindB;
            if (a.StartsWith(b))
                return -1;
            if (b.StartsWith(a))
                return 1;
            return string.CompareOrdinal(a, b);
        }

        static int RuleNameKind(string ruleName)
        {
            if (ruleName.StartsWith("*"))
                return 1;
            if (ruleName.StartsWith("^"))
                return 2;
            return 0;
        }

        // "**/Editor/" style globs; a trailing "/" means everything inside that folder.
        static Regex GlobToRegex(string glob)
        {
            var builder = new StringBuilder("^");
            for (int i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                if (c == '*')
                {
                    var isDouble = i + 1 < glob.Length && glob[i + 1] == '*';
                    if (isDouble && i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        i += 2;
                    }
                    else if (isDouble)
                    {
                        builder.Append(".*");
                        i += 1;
                    }
                    else
                        builder.Append("[^/]*");
                }
                else if (c == '?')
                    builder.Append("[^/]");
                else
                    builder.Append(Regex.Escape(c.ToString()));
            }
            builder.Append(glob.EndsWith("/") ? ".*" : "(?:/.*)?$");
            return new Regex(builder.ToString());
        }
    }
}
