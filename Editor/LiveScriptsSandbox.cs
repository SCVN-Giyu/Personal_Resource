using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // A throwaway copy of the project for compiling the CURRENT scripts with Luna's own tasks
    // (code:compose, code:build, code:stage) and measuring them with package:stats, without a Luna build.
    //
    // Safety rules (Luna's SyntaxTransformer rewrites every file its project lists, and code:stage deletes and
    // rewrites folders): scripts are COPIED, never linked; LunaTemp is an APFS clone of the real one
    // (independent copy, ~0.2 s); the Luna compiler folder is private to the sandbox. Only files that are
    // read and never written (Packages, ProjectSettings) are links.
    public static class LiveScriptsSandbox
    {
        public const string FOLDER = LunaStatsRunner.WORK_FOLDER + "/live";
        const string PROJECT = "project";

        public static string ProjectPath(string projectRoot)
        {
            return Path.Combine(projectRoot, FOLDER, PROJECT);
        }

        public static string SandboxRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, FOLDER);
        }

        // Fingerprint of everything the scripts measurement depends on: every .cs file under Assets
        // (from ScriptSizeEstimator.ScanFiles), Luna's unity settings (modules, excludes, Mecanim WASM), Luna's version.
        // roots: ScriptRoots() - which scripts Luna compiles depends on the scenes, not only on the .cs files.
        public static string ScriptsFingerprint(string projectRoot, LunaInstall install, List<ScriptFileState> files, List<string> roots)
        {
            var builder = new StringBuilder(install.Version).Append('\n');
            foreach (var file in files)
                builder.Append(file.path).Append(':').Append(file.hash).Append('\n');
            builder.Append(string.Join(",", roots)).Append('\n');
            builder.Append(UnitySection(File.ReadAllText(Path.Combine(projectRoot, LunaSettings.FILE_NAME))));
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))).Replace("-", string.Empty);
        }

        // The "unity" block of luna.json (scripts, modules, Mecanim WASM); texture rules live elsewhere.
        static string UnitySection(string lunaJson)
        {
            var start = lunaJson.IndexOf("\"unity\":", StringComparison.Ordinal);
            var end = lunaJson.IndexOf("\"assets\":", start < 0 ? 0 : start, StringComparison.Ordinal);
            return start >= 0 && end > start ? lunaJson.Substring(start, end - start) : lunaJson;
        }

        const string SOURCE_STAMP = ".lunatemp-source";

        // Brings the sandbox project up to date with the current project: changed .cs files are copied, deleted ones
        // removed, luna.json refreshed; LunaTemp is re-cloned only when the real one comes from a newer Luna build.
        public static bool Prepare(string projectRoot, List<string> roots, out string error)
        {
            error = null;
            var realLunaTemp = Path.Combine(projectRoot, "LunaTemp");
            var hasBuild = HasBuildOutput(projectRoot);

            var project = ProjectPath(projectRoot);
            Directory.CreateDirectory(project);
            File.Copy(Path.Combine(projectRoot, LunaSettings.FILE_NAME), Path.Combine(project, LunaSettings.FILE_NAME), true);
            foreach (var readOnly in new[] { "Packages", "ProjectSettings" })
            {
                var link = Path.Combine(project, readOnly);
                if (!File.Exists(link) && !Directory.Exists(link)
                    && !ProcessRunner.Run("/bin/ln", new[] { "-s", Path.Combine(projectRoot, readOnly), link }, out error))
                    return false;
            }

            SyncScripts(projectRoot, project);

            var stamp = Path.Combine(project, SOURCE_STAMP);
            var source = hasBuild ? LunaStatsRunner.BuildOutputTime(projectRoot).Ticks.ToString() : NO_BUILD;
            var sandboxLunaTemp = Path.Combine(project, "LunaTemp");
            // Without a build the skeleton is rebuilt every time: its script roots follow the current scenes.
            var freshClone = !hasBuild || !Directory.Exists(sandboxLunaTemp) || !File.Exists(stamp) || File.ReadAllText(stamp) != source;
            if (freshClone)
            {
                if (Directory.Exists(sandboxLunaTemp))
                    SafeDelete(sandboxLunaTemp, Path.Combine(projectRoot, LunaStatsRunner.WORK_FOLDER));
                if (hasBuild)
                {
                    // APFS clone (independent copy in ~0.2 s); plain copy on other file systems.
                    if (!ProcessRunner.Run("/bin/cp", new[] { "-c", "-R", realLunaTemp, sandboxLunaTemp }, out error)
                        && !ProcessRunner.Run("/bin/cp", new[] { "-R", realLunaTemp, sandboxLunaTemp }, out error))
                        return false;
                }
                else
                    CreateSkeleton(projectRoot, sandboxLunaTemp);
                File.WriteAllText(stamp, source);
            }
            // Luna compiles only scripts reachable from the scenes' components. Use the CURRENT scenes, not the list
            // the last build exported (matched Luna's own list 17/17 and 63/63).
            WriteRoots(sandboxLunaTemp, roots);
            return CheckNoLinks(project, freshClone, out error);
        }

        static void WriteRoots(string lunaTemp, List<string> roots)
        {
            var json = "[" + string.Join(",", roots.Select(r => "\"" + r.Replace("\\", "/") + "\"")) + "]";
            foreach (var folder in new[] { "stage2/js", "stage3/js" })
            {
                Directory.CreateDirectory(Path.Combine(lunaTemp, folder));
                File.WriteAllText(Path.Combine(lunaTemp, folder, "rootPaths.json"), json);
            }
        }

        const string NO_BUILD = "no-build";

        public static bool HasBuildOutput(string projectRoot)
        {
            var lunaTemp = Path.Combine(projectRoot, "LunaTemp");
            return Directory.Exists(Path.Combine(lunaTemp, "stage2")) && File.Exists(Path.Combine(lunaTemp, "stage3", "js", "deserializers.js"));
        }

        // What Luna's script stages read from the Unity-side export, recreated without a build:
        // stage1/js/package.json (plugins), stage1/js/unity.json (Unity install path), rootPaths.json (written by Prepare),
        // an empty deserializers.js (generated from the serialized scenes during export; estimated separately)
        // and an empty stats.json for package:stats.
        static void CreateSkeleton(string projectRoot, string lunaTemp)
        {
            foreach (var folder in new[] { "stage1/js", "stage2/js", "stage2/assets", "stage3/js", "stage3/assets" })
                Directory.CreateDirectory(Path.Combine(lunaTemp, folder));
            var package = "{\"requires\":[" + string.Join(",", RequiredPlugins(projectRoot).Select(r => "\"" + r + "\"")) + "],\"source_packages\":[]}";
            File.WriteAllText(Path.Combine(lunaTemp, "stage1/js/package.json"), package);
            File.WriteAllText(Path.Combine(lunaTemp, "stage3/js/package.json"), package);
            // package:stats evaluates stage2's copy and bundles stage3's.
            File.WriteAllText(Path.Combine(lunaTemp, "stage2/js/deserializers.js"), "var Deserializers = {}\n");
            File.WriteAllText(Path.Combine(lunaTemp, "stage3/js/deserializers.js"), "var Deserializers = {}\n");
            File.WriteAllText(Path.Combine(lunaTemp, "stage3/stats.json"), "{\"types\":[],\"total\":0}");
            // config:unity reads the Unity install path from here (Luna's export writes the same value).
            File.WriteAllText(Path.Combine(lunaTemp, "stage1/js/unity.json"),
                "{\"applicationPath\":\"" + UnityEditor.EditorApplication.applicationContentsPath.Replace("\\", "/") + "\"}");
        }

        // Component (MonoBehaviour) scripts reachable from Luna's scenes, includes and exported Resources,
        // outside Editor folders: the same list Luna's export writes to rootPaths.json.
        public static List<string> ScriptRoots(string projectRoot)
        {
            var settings = LunaSettings.Load(projectRoot);
            var roots = settings.Scenes.Concat(settings.Includes).Where(p => !string.IsNullOrEmpty(UnityEditor.AssetDatabase.AssetPathToGUID(p))).ToList();
            if (settings.ExportsResources)
                roots.AddRange(UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                    && p.Contains("/Resources/") && !p.Contains("/Editor/") && !UnityEditor.AssetDatabase.IsValidFolder(p)));
            return UnityEditor.AssetDatabase.GetDependencies(roots.ToArray(), true)
                .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) && !p.Contains("/Editor/"))
                .Where(p =>
                {
                    var script = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(p);
                    var type = script != null ? script.GetClass() : null;
                    return type != null && typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type);
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        // Luna's optional engine libraries (DOTween.js, newtonsoft.json.js) ship when the project contains the plugin.
        static IEnumerable<string> RequiredPlugins(string projectRoot)
        {
            var assets = Path.Combine(projectRoot, "Assets");
            if (Directory.GetFiles(assets, "DOTween.dll", SearchOption.AllDirectories).Length > 0)
                yield return "DOTween";
            var manifest = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (Directory.GetFiles(assets, "Newtonsoft.Json.dll", SearchOption.AllDirectories).Length > 0
                || (File.Exists(manifest) && File.ReadAllText(manifest).Contains("com.unity.nuget.newtonsoft-json")))
                yield return "Newtonsoft.Json";
        }

        // Copies new and changed scripts (same size + time = unchanged) and removes deleted ones. Copies, never links.
        static void SyncScripts(string projectRoot, string project)
        {
            var realAssets = Path.Combine(projectRoot, "Assets");
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(realAssets, "*.cs", SearchOption.AllDirectories))
            {
                var relative = file.Substring(projectRoot.Length + 1);
                wanted.Add(relative);
                var target = Path.Combine(project, relative);
                var sourceInfo = new FileInfo(file);
                var targetInfo = new FileInfo(target);
                if (targetInfo.Exists && targetInfo.Length == sourceInfo.Length && targetInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                File.SetLastWriteTimeUtc(target, sourceInfo.LastWriteTimeUtc);
            }
            var sandboxAssets = Path.Combine(project, "Assets");
            Directory.CreateDirectory(sandboxAssets);
            foreach (var file in Directory.GetFiles(sandboxAssets, "*.cs", SearchOption.AllDirectories))
            {
                if (!wanted.Contains(file.Substring(project.Length + 1)))
                    File.Delete(file);
            }
        }

        // Only Packages and ProjectSettings may be links; anything else could let a write reach the real project.
        static bool CheckNoLinks(string project, bool includeLunaTemp, out string error)
        {
            error = null;
            foreach (var path in Directory.GetFileSystemEntries(project, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (name == "Packages" || name == "ProjectSettings")
                    continue;
                if (IsLink(path))
                {
                    error = "Sandbox safety check failed: " + path + " is a link.";
                    return false;
                }
            }
            foreach (var dir in includeLunaTemp ? new[] { "Assets", "LunaTemp" } : new[] { "Assets" })
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(Path.Combine(project, dir), "*", SearchOption.AllDirectories))
                {
                    if (IsLink(path))
                    {
                        error = "Sandbox safety check failed: " + path + " is a link.";
                        return false;
                    }
                }
            }
            return true;
        }

        static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        // Deletes a folder inside allowedRoot without ever following a link: links are removed as links
        // (rm without -r), only real folders are recursed into. Used for every folder that contains links
        // to the Luna install or the project.
        public static void SafeDelete(string folder, string allowedRoot)
        {
            var allowed = Path.GetFullPath(allowedRoot);
            if (!Path.GetFullPath(folder).StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to delete outside " + allowed + ": " + folder);
            if (IsLink(folder))
            {
                RemoveLink(folder);
                return;
            }
            foreach (var path in Directory.GetFileSystemEntries(folder))
            {
                if (IsLink(path))
                    RemoveLink(path);
                else if (Directory.Exists(path))
                    SafeDelete(path, allowedRoot);
                else
                    File.Delete(path);
            }
            Directory.Delete(folder);
        }

        // rm without -r removes only the link, never what it points to.
        static void RemoveLink(string path)
        {
            string error;
            if (!ProcessRunner.Run("/bin/rm", new[] { "-f", path }, out error))
                throw new IOException("Could not remove link " + path + ": " + error);
        }

        public static Dictionary<string, string> Environment(string projectRoot, string compilerFolder)
        {
            return new Dictionary<string, string>
            {
                { "LUNA_LIVE_SANDBOX", SandboxRoot(projectRoot) },
                { "LUNA_LIVE_COMPILER", compilerFolder },
            };
        }
    }
}
