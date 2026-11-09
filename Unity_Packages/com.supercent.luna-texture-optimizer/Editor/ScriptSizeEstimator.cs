using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Supercent.LunaTextureOptimizer.Editor
{
    [Serializable]
    public class ScriptFileState
    {
        public string path;   // project-relative, e.g. Assets/Scripts/Player.cs
        public string hash;
        public long bytes;
        public long compressed;   // Deflate size: tracks how much the code adds after Luna's compression better than raw bytes
    }

    [Serializable]
    public class FileScriptSize
    {
        public string path;
        public double js;     // this file's share of Luna's scripts size, in playable bytes
    }

    // Turns an exact scripts measurement into per-file sizes, and estimates the scripts size of the current
    // code from the files changed since, instantly and without compiling. The estimate is replaced by the exact
    // number as soon as the next live measurement finishes.
    public static class ScriptSizeEstimator
    {
        static readonly Regex NAMESPACE = new Regex(@"\bnamespace\s+([\w.]+)");
        static readonly Regex TYPE = new Regex(@"\b(?:class|struct|interface|enum|record)\s+(\w+)");

        public static List<ScriptFileState> ScanFiles(string projectRoot)
        {
            var result = new List<ScriptFileState>();
            using (var sha = SHA1.Create())
            {
                foreach (var file in Directory.GetFiles(Path.Combine(projectRoot, "Assets"), "*.cs", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.Ordinal))
                {
                    var bytes = File.ReadAllBytes(file);
                    result.Add(new ScriptFileState
                    {
                        path = file.Substring(projectRoot.Length + 1).Replace('\\', '/'),
                        hash = Convert.ToBase64String(sha.ComputeHash(bytes)),
                        bytes = bytes.LongLength,
                        compressed = DeflateSize(bytes),
                    });
                }
            }
            return result;
        }

        static long DeflateSize(byte[] bytes)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(bytes, 0, bytes.Length);
                return output.Length;
            }
        }

        static double Weight(ScriptFileState file, bool compressed)
        {
            return compressed ? file.compressed : file.bytes;
        }

        // Splits the user-code part of an exact measurement over the .cs files that declare each type.
        // Reflection metadata and types no file declares stay in the returned "unmapped" bytes.
        public static List<FileScriptSize> Attribute(LunaMeasurement measurement, string projectRoot, out double unmapped)
        {
            unmapped = 0;
            var result = new List<FileScriptSize>();
            var sizes = measurement.scripts;
            if (sizes == null || sizes.files == null || sizes.user == null || sizes.user.types.Count == 0)
                return result;
            var userFile = sizes.files.FirstOrDefault(f => f.src.EndsWith("UnityScriptsCompiler.js", StringComparison.Ordinal));
            if (userFile == null)
                return result;

            var scale = measurement.stats.scripts / Math.Max(1, sizes.files.Sum(f => f.br));
            var userBytes = userFile.br * scale;
            var userScale = userBytes / Math.Max(1, sizes.user.types.Sum(t => t.br) + sizes.user.metadataBr + sizes.user.otherBr);

            var typeFiles = TypeFiles(projectRoot);
            var perFile = new Dictionary<string, double>();
            unmapped = (sizes.user.metadataBr + sizes.user.otherBr) * userScale;
            foreach (var type in sizes.user.types)
            {
                var file = FileOf(type.name, typeFiles);
                if (file == null)
                {
                    unmapped += type.br * userScale;
                    continue;
                }
                double sum;
                perFile.TryGetValue(file, out sum);
                perFile[file] = sum + type.br * userScale;
            }
            result.AddRange(perFile.Select(p => new FileScriptSize { path = p.Key, js = p.Value }));
            return result;
        }

        // Instant estimate of Luna's scripts size for the current files. changedFiles = files that differ from
        // the measured state (0 means the measured number is still exact).
        public static double Estimate(LunaMeasurement measurement, List<ScriptFileState> current, out int changedFiles)
        {
            changedFiles = 0;
            var exact = measurement.stats.scripts;
            if (measurement.scriptFiles == null || measurement.fileScripts == null)
                return exact;

            var before = measurement.scriptFiles.ToDictionary(f => f.path);
            var js = measurement.fileScripts.ToDictionary(f => f.path, f => f.js);
            var mappedJs = js.Values.Sum();
            // Measurements saved before compressed sizes existed fall back to raw bytes everywhere.
            var compressed = measurement.scriptFiles.All(f => f.compressed > 0);
            var mappedCs = measurement.scriptFiles.Where(f => js.ContainsKey(f.path)).Sum(f => Weight(f, compressed));
            var ratio = mappedCs > 0 ? mappedJs / mappedCs : 0;

            var delta = 0.0;
            var now = new HashSet<string>();
            foreach (var file in current)
            {
                now.Add(file.path);
                ScriptFileState old;
                if (before.TryGetValue(file.path, out old) && old.hash == file.hash)
                    continue;
                changedFiles++;
                double oldJs;
                var hasJs = js.TryGetValue(file.path, out oldJs);
                if (old != null && hasJs && Weight(old, compressed) > 0)
                    delta += oldJs * (Weight(file, compressed) / Weight(old, compressed)) - oldJs;   // edited file: scale its share
                else if (old == null)
                    delta += Weight(file, compressed) * ratio;                                       // new file: project average
            }
            foreach (var old in measurement.scriptFiles.Where(f => !now.Contains(f.path)))
            {
                changedFiles++;
                double oldJs;
                if (js.TryGetValue(old.path, out oldJs))
                    delta -= oldJs;                                                // deleted file
            }
            // Reflection metadata grows and shrinks with the code.
            if (mappedJs > 0)
                delta += delta * (measurement.unmappedScripts / mappedJs);
            return exact + delta;
        }

        // Bridge type name -> declaring .cs file (null when no project file declares it), for locating types.
        public static Dictionary<string, string> TypeFileMap(string projectRoot, IEnumerable<string> bridgeNames)
        {
            var typeFiles = TypeFiles(projectRoot);
            var result = new Dictionary<string, string>();
            foreach (var name in bridgeNames)
                result[name] = FileOf(name, typeFiles);
            return result;
        }

        // Full type name (namespace.Type) -> declaring file. Nested types resolve through their outer type.
        static Dictionary<string, string> TypeFiles(string projectRoot)
        {
            var result = new Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(Path.Combine(projectRoot, "Assets"), "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                var namespaceMatch = NAMESPACE.Match(text);
                var prefix = namespaceMatch.Success ? namespaceMatch.Groups[1].Value + "." : string.Empty;
                var relative = file.Substring(projectRoot.Length + 1).Replace('\\', '/');
                foreach (Match type in TYPE.Matches(text))
                {
                    var name = prefix + type.Groups[1].Value;
                    if (!result.ContainsKey(name))
                        result[name] = relative;
                }
            }
            return result;
        }

        // Bridge names: Namespace.Outer.Inner, generics as Name$1.
        static string FileOf(string bridgeName, Dictionary<string, string> typeFiles)
        {
            var name = Regex.Replace(bridgeName, @"\$\d+", string.Empty);
            while (!string.IsNullOrEmpty(name))
            {
                string file;
                if (typeFiles.TryGetValue(name, out file))
                    return file;
                var dot = name.LastIndexOf('.');
                if (dot < 0)
                    return null;
                // Nested type: try the outer type, but also the inner name directly under the namespace.
                var inner = name.Substring(dot + 1);
                var outer = name.Substring(0, dot);
                var namespaceDot = outer.LastIndexOf('.');
                if (namespaceDot > 0 && typeFiles.TryGetValue(outer.Substring(0, namespaceDot) + "." + inner, out file))
                    return file;
                name = outer;
            }
            return null;
        }
    }
}
