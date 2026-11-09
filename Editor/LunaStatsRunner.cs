using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    [Serializable]
    public class LunaStatsAsset
    {
        public string title;
        public string path;
        public string lunaPath;   // the processed file in the build output this size was computed from
        public double size;
    }

    [Serializable]
    public class LunaStatsType
    {
        public string title;
        public double size;
        public List<LunaStatsAsset> assets = new List<LunaStatsAsset>();
    }

    // Luna's own "Size Breakdown" numbers (package:stats with STATS=3).
    [Serializable]
    public class LunaStats
    {
        public List<LunaStatsType> types = new List<LunaStatsType>();
        public double total;
        public double scripts;
        public double preloader;
        public double other;
    }

    [Serializable]
    public class ScriptFileSize
    {
        public string src;
        public long raw;
        public long min;
        public long br;
    }

    [Serializable]
    public class ScriptTypeSize
    {
        public string name;
        public long br;
    }

    [Serializable]
    public class UserCodeSizes
    {
        public List<ScriptTypeSize> types = new List<ScriptTypeSize>();
        public long metadataBr;
        public long otherBr;
    }

    // Per-file and per-type compressed sizes from stats-hook.js; only their ratios are used.
    [Serializable]
    public class ScriptSizes
    {
        public string error;
        public long combinedMin;
        public long combinedBr;
        public List<ScriptFileSize> files = new List<ScriptFileSize>();
        public UserCodeSizes user;
    }

    [Serializable]
    public class LunaMeasurement
    {
        public string measuredAt;
        public string buildOutputAt;
        public string label;
        public LunaStats stats;
        public ScriptSizes scripts;
        public double seconds;
        public string network;             // Luna target platform the numbers are for (encoding and template differ)
        public bool live;                  // scripts compiled from the current code, not read from the last build
        public bool noBuild;               // no Luna build existed: scripts without scene deserializers, no asset categories
        public string scriptsFingerprint;  // LiveScriptsSandbox.ScriptsFingerprint at measure time
        public List<ScriptFileState> scriptFiles;   // .cs files as measured (for the instant estimate)
        public List<FileScriptSize> fileScripts;    // each file's share of the scripts size
        public double unmappedScripts;              // reflection metadata and types no file declares
    }

    // Runs Luna's own statistics task (the numbers behind Luna's Size Breakdown) on the last build output,
    // without exporting or compiling. A private copy of Luna's pipeline routes the script minify through
    // stats-hook.js to also get the per-file / per-type split; the Luna install itself is never modified.
    public class LunaStatsRunner
    {
        public const string WORK_FOLDER = "Library/LunaSizeDashboard";
        const string STATS_HOOK = "Tools~/LunaSizeDashboard/stats-hook.js";
        const string LIVE_HOOK = "Tools~/LunaSizeDashboard/live-hook.js";
        const string BROTLI_CACHE = "Tools~/LunaSizeDashboard/brotli-cache.sh";
        const string COMPILER_FOLDER = "LunaCompiler";
        const string INSTRUMENT_VERSION = "2"; // bump when the instrumented layout changes
        const string STATS_JSON_START = "{\"types\"";
        const string ARM_NODE = "/opt/homebrew/bin/node";
        // The statement in Luna's getScripts that minifies all script files at once.
        static readonly Regex MINIFY_CALL = new Regex(@"(_0x[0-9a-f]+)=await (_0x[0-9a-f]+)\[(_0x[0-9a-f]+\(0x[0-9a-f]+\))\]\((_0x[0-9a-f]+),(_0x[0-9a-f]+)\);");
        static readonly Regex METHOD_FIRST_PARAMETER = new Regex(@"async\[[^\]]+\]\((_0x[0-9a-f]+),");
        static readonly Regex LIMITS_TABLE = new Regex(@"\{'applovin':0x[0-9a-f]+[^}]*'tiktok':0x[0-9a-f]+[^}]*\}");
        static readonly Regex TEMPLATES_TABLE = new Regex(@"\{'develop':0x[0-9a-f]+[^}]*\}");
        static readonly Regex TABLE_ENTRY = new Regex(@"'([a-z-]+)':0x([0-9a-f]+)");

        readonly Process process;
        readonly StringBuilder output = new StringBuilder();
        readonly string dumpPath;
        readonly DateTime started;

        public string Label { get; private set; }
        public bool IsDone { get { return process == null || process.HasExited; } }
        public double ElapsedSeconds { get { return (DateTime.Now - started).TotalSeconds; } }

        LunaStatsRunner(Process process, string dumpPath, string label)
        {
            this.process = process;
            this.dumpPath = dumpPath;
            Label = label;
            started = DateTime.Now;
        }

        // projectPath is the folder holding luna.json and LunaTemp (the real project, or a what-if copy).
        // live = also compile the sandbox's current scripts with Luna's code:compose (or code-v2:compose)/build/stage first.
        public static LunaStatsRunner Start(LunaInstall install, string projectRoot, string projectPath, string label, bool live, string network, out string error)
        {
            error = null;
            // One run at a time: Luna's tasks rewrite the sandbox and the private compiler folder in place.
            if (!TryLock(projectRoot, out error))
                return null;
            var instrumented = PrepareInstrumentedLuna(install, projectRoot, out var instrumentError);
            if (live && instrumented == null)
            {
                error = "Live scripts measurement unavailable: " + instrumentError;
                Unlock(projectRoot);
                return null;
            }
            var lunaRoot = instrumented ?? install.RootPath;
            var dumpPath = Path.Combine(projectRoot, WORK_FOLDER, "scripts-" + Guid.NewGuid().ToString("N") + ".json");

            var info = new ProcessStartInfo(NodePath(install), string.Join(" ", new[]
            {
                live ? "-r " + Quote(ToolPath(LIVE_HOOK)) : null,
                instrumented != null ? "-r " + Quote(ToolPath(STATS_HOOK)) : null,
                Quote(Path.Combine(lunaRoot, "pipeline", "jake.js")),
                "-f", Quote(Path.Combine(lunaRoot, "pipeline", "Jakefile.js")),
                live ? LiveComposeTask(projectPath) + " code:build code:stage" : null,
                "package:stats",
            }.Where(a => a != null)))
            {
                WorkingDirectory = lunaRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.EnvironmentVariables["PROJECT_PATH"] = projectPath;
            info.EnvironmentVariables["STATS"] = "3";
            info.EnvironmentVariables["TARGET_PLATFORM"] = string.IsNullOrEmpty(network) ? "develop" : network;
            info.EnvironmentVariables["LUNA_SIZE_DUMP"] = dumpPath;
            var cache = Path.Combine(projectRoot, WORK_FOLDER, "cache-" + install.Version);
            info.EnvironmentVariables["LUNA_SIZE_CACHE"] = cache;
            var brotli = Path.Combine(install.RootPath, "tools", "brotli", "mac64", "brotli");
            if (Application.platform == RuntimePlatform.OSXEditor && File.Exists(brotli))
            {
                info.EnvironmentVariables["BROTLI_PATH_OVERRIDE"] = ToolPath(BROTLI_CACHE);
                info.EnvironmentVariables["LUNA_REAL_BROTLI"] = brotli;
                info.EnvironmentVariables["LUNA_BROTLI_CACHE"] = Path.Combine(cache, "brotli");
            }
            if (live)
            {
                foreach (var pair in LiveScriptsSandbox.Environment(projectRoot, Path.Combine(instrumented, "pipeline", "templates", COMPILER_FOLDER)))
                    info.EnvironmentVariables[pair.Key] = pair.Value;
            }

            try
            {
                var process = new Process { StartInfo = info };
                var runner = new LunaStatsRunner(process, dumpPath, label) { projectRoot = projectRoot };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (runner.output) runner.output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (runner.output) runner.output.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (instrumentError != null)
                    error = "Script breakdown unavailable: " + instrumentError;
                return runner;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Unlock(projectRoot);
                return null;
            }
        }

        // Luna's own build composes Compiler V2 projects with code-v2:compose; code:compose would hand Bridge the raw
        // sources, which it can fail on where the decompiled ones compile.
        static string LiveComposeTask(string projectPath)
        {
            var settings = LunaSettings.Load(projectPath);
            return settings != null && settings.UsesCompilerV2 ? "code-v2:compose" : "code:compose";
        }

        public void Cancel()
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { /* already exited */ }
            Unlock(projectRoot);
        }

        string projectRoot;
        const string LOCK_FILE = "run.lock";
        static readonly TimeSpan STALE_LOCK = TimeSpan.FromMinutes(10);

        // A lock file with the owner's process id; a lock whose process is gone or that is very old is taken over.
        static bool TryLock(string projectRoot, out string error)
        {
            error = null;
            var path = Path.Combine(projectRoot, WORK_FOLDER, LOCK_FILE);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                int owner;
                var alive = int.TryParse(File.ReadAllText(path).Split('|')[0].Trim(), out owner) && IsAlive(owner);
                if (alive && DateTime.Now - File.GetLastWriteTime(path) < STALE_LOCK)
                {
                    error = "Another size measurement is still running. Try again when it finishes.";
                    return false;
                }
            }
            File.WriteAllText(path, Process.GetCurrentProcess().Id + "|" + Guid.NewGuid().ToString("N"));
            return true;
        }

        static bool IsAlive(int processId)
        {
            try { return !Process.GetProcessById(processId).HasExited; }
            catch (ArgumentException) { return false; }
        }

        static void Unlock(string projectRoot)
        {
            if (projectRoot == null)
                return;
            var path = Path.Combine(projectRoot, WORK_FOLDER, LOCK_FILE);
            if (File.Exists(path))
                File.Delete(path);
        }

        // Call once IsDone is true.
        public LunaMeasurement Finish(string projectPath, out string error)
        {
            error = null;
            process.WaitForExit();
            Unlock(projectRoot);
            string text;
            lock (output) text = output.ToString();

            var start = text.LastIndexOf(STATS_JSON_START, StringComparison.Ordinal);
            var end = text.LastIndexOf('}');
            if (process.ExitCode != 0 || start < 0 || end < start)
            {
                error = "Luna's statistics task failed (exit " + process.ExitCode + "). Run a Luna build once, then measure again.\n"
                    + Tail(text, 600);
                return null;
            }

            var measurement = new LunaMeasurement
            {
                label = Label,
                measuredAt = DateTime.Now.ToString("o"),
                buildOutputAt = BuildOutputTime(projectPath).ToString("o"),
                seconds = ElapsedSeconds,
                stats = JsonUtility.FromJson<LunaStats>(text.Substring(start, end - start + 1)),
            };
            if (File.Exists(dumpPath))
            {
                measurement.scripts = JsonUtility.FromJson<ScriptSizes>(File.ReadAllText(dumpPath));
                File.Delete(dumpPath);
                if (measurement.scripts != null && !string.IsNullOrEmpty(measurement.scripts.error))
                {
                    error = "Script breakdown failed: " + measurement.scripts.error;
                    measurement.scripts = null;
                }
            }
            return measurement;
        }

        // When the build output Luna's statistics read was written.
        public static DateTime BuildOutputTime(string projectPath)
        {
            var marker = Path.Combine(projectPath, "LunaTemp", "stage3", "js", "deserializers.js");
            return File.Exists(marker) ? File.GetLastWriteTime(marker) : DateTime.MinValue;
        }

        // Size limit and HTML template size ("other") per ad network, read from Luna's own pipeline.
        public static Dictionary<string, KeyValuePair<long, long>> ReadNetworks(LunaInstall install)
        {
            var result = new Dictionary<string, KeyValuePair<long, long>>();
            var jakefile = Path.Combine(install.RootPath, "pipeline", "Jakefile.js");
            if (!File.Exists(jakefile))
                return result;
            var text = File.ReadAllText(jakefile);
            var limits = ParseTable(LIMITS_TABLE.Match(text).Value).ToDictionary(p => p.Key.Replace("-", string.Empty), p => p.Value);
            var templates = ParseTable(TEMPLATES_TABLE.Match(text).Value);
            // Keyed by Luna's platform id (what TARGET_PLATFORM takes); value = (size limit or 0, template size).
            foreach (var pair in templates.Where(p => p.Key != "develop"))
            {
                long limit;
                limits.TryGetValue(pair.Key, out limit);
                result[pair.Key] = new KeyValuePair<long, long>(limit, pair.Value);
            }
            return result;
        }

        static Dictionary<string, long> ParseTable(string table)
        {
            var result = new Dictionary<string, long>();
            foreach (Match entry in TABLE_ENTRY.Matches(table))
                result[entry.Groups[1].Value] = long.Parse(entry.Groups[2].Value, NumberStyles.HexNumber);
            return result;
        }

        // Native Node on Apple Silicon (same result, ~2x faster than Luna's Intel build under Rosetta).
        static string NodePath(LunaInstall install)
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && File.Exists(ARM_NODE))
                return ARM_NODE;
            var platform = Application.platform == RuntimePlatform.WindowsEditor ? "win64/node.exe" : "mac64/bin/node";
            return Path.Combine(install.RootPath, "tools", "node", platform);
        }

        static string ToolPath(string relative)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LunaStatsRunner).Assembly);
            return Path.Combine(package.resolvedPath, relative);
        }

        // Library/LunaSizeDashboard/luna-<version>: links to every part of the Luna install except a patched
        // Jakefile.js. Returns null when the patch point is not found (other Luna version) or links are unsupported.
        static string PrepareInstrumentedLuna(LunaInstall install, string projectRoot, out string error)
        {
            error = null;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                error = "not supported on Windows yet";
                return null;
            }
            var source = Path.Combine(install.RootPath, "pipeline", "Jakefile.js");
            var root = Path.Combine(projectRoot, WORK_FOLDER, "luna-" + install.Version);
            var patched = Path.Combine(root, "pipeline", "Jakefile.js");
            var stamp = Path.Combine(root, "source-stamp.txt");
            var sourceStamp = INSTRUMENT_VERSION + ":" + File.GetLastWriteTimeUtc(source).Ticks + ":" + new FileInfo(source).Length;
            if (File.Exists(patched) && File.Exists(stamp) && File.ReadAllText(stamp) == sourceStamp)
                return root;

            var text = File.ReadAllText(source);
            var passes = text.IndexOf("'passes':0x2", StringComparison.Ordinal);
            var call = passes >= 0 ? MINIFY_CALL.Match(text, passes) : Match.Empty;
            if (!call.Success || call.Index - passes > 2000)
            {
                error = "Luna " + install.Version + " is not supported (minify call not found)";
                return null;
            }
            var methodHeads = METHOD_FIRST_PARAMETER.Matches(text.Substring(Math.Max(0, call.Index - 6000), Math.Min(6000, call.Index)));
            if (methodHeads.Count == 0)
            {
                error = "Luna " + install.Version + " is not supported (getScripts not found)";
                return null;
            }
            var relations = methodHeads[methodHeads.Count - 1].Groups[1].Value;
            var g = call.Groups;
            var replacement = g[1].Value + "=await globalThis.__lunaMinify(" + g[2].Value + "," + g[3].Value + "," + g[4].Value + "," + g[5].Value + "," + relations + ");";
            text = text.Substring(0, call.Index) + replacement + text.Substring(call.Index + call.Length);

            if (Directory.Exists(root))
                LiveScriptsSandbox.SafeDelete(root, Path.Combine(projectRoot, WORK_FOLDER));
            Directory.CreateDirectory(Path.Combine(root, "pipeline"));
            foreach (var entry in Directory.GetFileSystemEntries(install.RootPath))
            {
                if (Path.GetFileName(entry) != "pipeline" && !Link(entry, Path.Combine(root, Path.GetFileName(entry)), out error))
                    return null;
            }
            foreach (var entry in Directory.GetFileSystemEntries(Path.Combine(install.RootPath, "pipeline")))
            {
                var name = Path.GetFileName(entry);
                if (name != "Jakefile.js" && name != "templates" && !Link(entry, Path.Combine(root, "pipeline", name), out error))
                    return null;
            }
            if (!PrepareTemplates(Path.Combine(install.RootPath, "pipeline", "templates"), Path.Combine(root, "pipeline", "templates"), out error))
                return null;
            File.WriteAllText(patched, text);
            File.WriteAllText(stamp, sourceStamp);
            return root;
        }

        // Luna compiles inside templates/LunaCompiler (writes Scripts, bin, typemaps). The sandbox gets a private
        // copy of everything written there; only the read-only packages folder is linked.
        static bool PrepareTemplates(string source, string target, out string error)
        {
            error = null;
            Directory.CreateDirectory(target);
            foreach (var entry in Directory.GetFileSystemEntries(source))
            {
                var name = Path.GetFileName(entry);
                if (name != COMPILER_FOLDER && !Link(entry, Path.Combine(target, name), out error))
                    return false;
            }
            var compilerSource = Path.Combine(source, COMPILER_FOLDER);
            var compiler = Path.Combine(target, COMPILER_FOLDER);
            Directory.CreateDirectory(Path.Combine(compiler, "bin"));
            if (!Link(Path.Combine(compilerSource, "packages"), Path.Combine(compiler, "packages"), out error))
                return false;
            foreach (var file in Directory.GetFiles(compilerSource))
                File.Copy(file, Path.Combine(compiler, Path.GetFileName(file)), true);
            foreach (var folder in new[] { "typemaps", "Scripts" })
            {
                var from = Path.Combine(compilerSource, folder);
                foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(from.Length + 1);
                    if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        continue;
                    var to = Path.Combine(compiler, folder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(file, to, true);
                }
            }
            return true;
        }

        static bool Link(string target, string link, out string error)
        {
            return ProcessRunner.Run("/bin/ln", new[] { "-s", target, link }, out error);
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        static string Tail(string text, int length)
        {
            return text.Length <= length ? text : "…" + text.Substring(text.Length - length);
        }
    }
}
