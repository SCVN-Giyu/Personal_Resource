using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    [Serializable]
    public class LunaPrediction
    {
        // Typical size of the .json sidecar Luna ships next to every texture (measured 112-125 bytes).
        const double SIDECAR_BYTES = 120;
        const double BASE64_RATIO = 4.0 / 3.0;

        public bool success;
        public string error;
        public bool usedSourceFile;
        public long inputBytes;
        public string outputExtension;
        public long outputBytes;

        // What the texture costs inside the playable when images are inlined as base64 (Luna's default).
        public double PlayableBytes
        {
            get { return (outputBytes + SIDECAR_BYTES) * BASE64_RATIO; }
        }
    }

    // Runs Luna's texture pipeline locally with Luna's own tools and arguments, so the result matches a real build.
    public static class LunaPipeline
    {
        public const string WORK_FOLDER = "Library/LunaTextureOptimizer/Predict";
        const string PNG = ".png";
        const string JPEG = ".jpeg";
        const string WEBP = ".webp";

        // Luna writes WebP when the rule says so; JPEG when the rule says so, when Unity imported the texture
        // in a format without alpha, or when the alpha source is None; PNG otherwise.
        public static string OutputExtension(LunaTextureRule rule, TextureFormat importedFormat, TextureImporterAlphaSource alphaSource)
        {
            var importedWithoutAlpha = importedFormat == TextureFormat.DXT1 || importedFormat == TextureFormat.RGB24;
            string extension;
            if (importedWithoutAlpha)
                extension = rule.format == "webp" ? WEBP : JPEG;
            else
                extension = rule.format == "jpeg" ? JPEG : rule.format == "webp" ? WEBP : null;

            if (extension == WEBP)
                return WEBP;
            if (extension == JPEG || alphaSource == TextureImporterAlphaSource.None)
                return JPEG;
            return PNG;
        }

        // One texture's trip through Luna's tools. Built on the main thread (Unity API), run on a worker thread.
        class Job
        {
            public LunaPrediction Prediction = new LunaPrediction();
            public string ContentKey;
            public string Extension;
            public string FinalPath;
            public List<KeyValuePair<string, List<string>>> Commands = new List<KeyValuePair<string, List<string>>>();
        }

        public static LunaPrediction Predict(TextureEntry entry, LunaInstall install, string projectRoot, PredictionCache cache = null)
        {
            return PredictMany(new[] { entry }, install, projectRoot, cache, null)[0];
        }

        // Exports every input on the main thread, then runs Luna's tools for all textures in parallel.
        // Results already in the cache under the same input pixels and commands are reused without running anything.
        // A non-null progressTitle shows a cancelable progress bar; canceled textures come back as null.
        public static LunaPrediction[] PredictMany(IList<TextureEntry> entries, LunaInstall install, string projectRoot,
            PredictionCache cache, string progressTitle)
        {
            var results = new LunaPrediction[entries.Count];
            var batchPath = Path.Combine(projectRoot, WORK_FOLDER, Guid.NewGuid().ToString("N"));
            var jobs = new List<KeyValuePair<int, Job>>();
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (progressTitle != null && EditorUtility.DisplayCancelableProgressBar(progressTitle, "Exporting " + entries[i].FileName, (float)i / entries.Count))
                        return results;
                    var job = Prepare(entries[i], install, projectRoot, Path.Combine(batchPath, i.ToString()));
                    var cached = job.ContentKey != null && cache != null ? cache.Get(job.ContentKey) : null;
                    if (cached != null && cached.success)
                        results[i] = CopyOf(cached, job.Prediction);
                    else if (job.Commands.Count == 0)
                        results[i] = job.Prediction;
                    else
                        jobs.Add(new KeyValuePair<int, Job>(i, job));
                }

                ParallelWork.Run(jobs.Select(j => j.Value).ToList(), Run, progressTitle, "Running Luna's tools");
                foreach (var pair in jobs)
                {
                    if (pair.Value.Prediction.success || pair.Value.Prediction.error != null)
                        results[pair.Key] = pair.Value.Prediction;
                    if (pair.Value.Prediction.success && cache != null)
                        cache.Set(pair.Value.ContentKey, pair.Value.Prediction);
                }
                return results;
            }
            finally
            {
                if (progressTitle != null)
                    EditorUtility.ClearProgressBar();
                if (Directory.Exists(batchPath))
                    Directory.Delete(batchPath, true);
            }
        }

        public const string PREVIEW_FOLDER = "Library/LunaTextureOptimizer/TexturePreview";

        // Runs Luna's tools for one texture and keeps Luna's input and output as PNGs Unity can show
        // (WebP output decoded with Luna's own magick). Files are named by the prediction key, so they are reused.
        public static bool RenderPreview(TextureEntry entry, LunaInstall install, string projectRoot, string key,
            out string inputPng, out string outputPng, out string error)
        {
            var folder = Path.Combine(projectRoot, PREVIEW_FOLDER);
            string name;
            using (var sha = SHA1.Create())
                name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
            inputPng = Path.Combine(folder, name + ".input.png");
            outputPng = Path.Combine(folder, name + ".output.png");
            error = null;
            if (File.Exists(inputPng) && File.Exists(outputPng))
                return true;

            var workPath = Path.Combine(projectRoot, WORK_FOLDER, Guid.NewGuid().ToString("N"));
            try
            {
                var job = Prepare(entry, install, projectRoot, workPath);
                if (job.Commands.Count > 0)
                    Run(job);
                if (!job.Prediction.success)
                {
                    error = job.Prediction.error ?? "Luna's tools produced no output.";
                    return false;
                }
                Directory.CreateDirectory(folder);
                if (job.Extension == WEBP)
                {
                    if (!ProcessRunner.Run(install.MagickPath, new[] { job.FinalPath, "png32:" + outputPng }, out error))
                        return false;
                }
                else
                {
                    File.Copy(job.FinalPath, outputPng, true);
                }
                File.Copy(Path.Combine(workPath, "input.png"), inputPng, true);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (Directory.Exists(workPath))
                    Directory.Delete(workPath, true);
            }
        }

        static Job Prepare(TextureEntry entry, LunaInstall install, string projectRoot, string workPath)
        {
            var job = new Job();
            try
            {
                Directory.CreateDirectory(workPath);
                var inputPath = Path.Combine(workPath, "input.png");
                LunaInputExporter.Export(entry, projectRoot, inputPath);
                job.Prediction.usedSourceFile = LunaInputExporter.UsesSourceFile(entry);
                job.Prediction.inputBytes = new FileInfo(inputPath).Length;

                job.Extension = OutputExtension(entry.LunaRule, entry.ImportedFormat, entry.AlphaSource);
                var convertedPath = Path.Combine(workPath, "convert" + job.Extension);
                job.FinalPath = convertedPath;
                job.Commands.Add(Command(install.MagickPath, MagickArguments(entry, job.Extension, inputPath, convertedPath).ToArray()));
                var finalPath = Path.Combine(workPath, "final" + job.Extension);
                var secondTool = SecondCompression(entry, job.Extension, install, convertedPath, finalPath);
                if (secondTool != null)
                {
                    job.Commands.Add(secondTool.Value);
                    job.FinalPath = finalPath;
                }
                job.ContentKey = ContentKey(inputPath, workPath, install, job.Commands);
            }
            catch (Exception exception)
            {
                job.Commands.Clear();
                Fail(job.Prediction, exception.Message);
            }
            return job;
        }

        // Hash of the exact input pixels plus every command with its work folder blanked out.
        static string ContentKey(string inputPath, string workPath, LunaInstall install, List<KeyValuePair<string, List<string>>> commands)
        {
            using (var sha = SHA1.Create())
            {
                var builder = new StringBuilder("content|").Append(install.Version).Append('|');
                builder.Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(inputPath))).Replace("-", string.Empty));
                foreach (var command in commands)
                {
                    builder.Append('|').Append(Path.GetFileName(command.Key));
                    foreach (var argument in command.Value)
                        builder.Append(' ').Append(argument.Replace(workPath, "{work}"));
                }
                return builder.ToString();
            }
        }

        // Worker thread: no Unity API in here.
        static void Run(Job job)
        {
            try
            {
                foreach (var command in job.Commands)
                {
                    string error;
                    if (!ProcessRunner.Run(command.Key, command.Value, out error))
                    {
                        Fail(job.Prediction, Path.GetFileNameWithoutExtension(command.Key) + " " + error);
                        return;
                    }
                }
                job.Prediction.outputExtension = job.Extension;
                job.Prediction.outputBytes = new FileInfo(job.FinalPath).Length;
                job.Prediction.success = true;
            }
            catch (Exception exception)
            {
                Fail(job.Prediction, exception.Message);
            }
        }

        static LunaPrediction CopyOf(LunaPrediction cached, LunaPrediction current)
        {
            return new LunaPrediction
            {
                success = true,
                usedSourceFile = current.usedSourceFile,
                inputBytes = current.inputBytes,
                outputExtension = cached.outputExtension,
                outputBytes = cached.outputBytes,
            };
        }

        // Same command Luna builds in its first compression step.
        static List<string> MagickArguments(TextureEntry entry, string extension, string inputPath, string outputPath)
        {
            var rule = entry.LunaRule;
            var arguments = new List<string> { inputPath };
            if (extension == PNG)
                arguments.AddRange(new[] { "-filter", "Mitchell", "-interpolate", "average16", "-interpolative-resize" });
            else
                arguments.Add("-resize");
            arguments.Add(rule.maxWidth > 0 && rule.maxHeight > 0 ? rule.maxWidth + "x" + rule.maxHeight + ">" : "100%");

            if (entry.AlphaSource == TextureImporterAlphaSource.FromGrayScale)
                arguments.AddRange(new[] { "-alpha", "copy", "-alpha", "activate" });
            if (extension == JPEG && !entry.IsSrgb && PlayerSettings.colorSpace == ColorSpace.Linear)
                arguments.AddRange(new[] { "-colorspace", "RGB" });

            if (extension == WEBP)
            {
                arguments.AddRange(new[] { "-define", "webp:lossless=true" });
                arguments.Add(outputPath);
            }
            else if (extension == JPEG)
            {
                if (rule.quality >= 0)
                    arguments.AddRange(new[] { "-quality", rule.quality + "%" });
                arguments.Add(outputPath);
            }
            else
            {
                var prefix = rule.format == "default" || string.IsNullOrEmpty(rule.format) ? string.Empty : rule.format + ":";
                arguments.Add(prefix + outputPath);
            }
            return arguments;
        }

        // Same tool choice as Luna's second compression step; null when Luna skips it.
        static KeyValuePair<string, List<string>>? SecondCompression(TextureEntry entry, string extension, LunaInstall install,
            string inputPath, string outputPath)
        {
            var rule = entry.LunaRule;
            var isJpeg = rule.format == "jpeg" || extension == JPEG;
            var isWebp = rule.format == "webp" || extension == WEBP;
            var noPngCompression = rule.format == "default" || string.IsNullOrEmpty(rule.compression) || rule.compression == "none";
            if (isJpeg || (!isWebp && noPngCompression))
                return null;

            if (rule.format == "webp")
                return Command(install.CwebpPath, inputPath, "-q", rule.quality.ToString(), "-alpha_q", "100", "-m", "6", "-o", outputPath);

            var isQuant = rule.compression == "quantQuality" || rule.compression == "quantSize";
            if (isQuant && !entry.IsSrgb && PlayerSettings.colorSpace == ColorSpace.Linear)
                return Pngcrush(install, inputPath, outputPath);
            if (rule.compression == "quantQuality")
                return Command(install.PngquantPath, "--force", "--strip", "--quality", "1-75", inputPath, "--output", outputPath);
            if (rule.compression == "quantSize")
                return Command(install.PngquantPath, "--force", "--quality=1-65", "--speed=1", "--strip", inputPath, "--output", outputPath);
            if (rule.compression == "crush")
                return Pngcrush(install, inputPath, outputPath);
            return null;
        }

        static KeyValuePair<string, List<string>> Pngcrush(LunaInstall install, string inputPath, string outputPath)
        {
            return Command(install.PngcrushPath, "-force", "-rem", "gAMA", "-rem", "cHRM", "-rem", "iCCP", "-rem", "sRGB", inputPath, outputPath);
        }

        static KeyValuePair<string, List<string>> Command(string executable, params string[] arguments)
        {
            return new KeyValuePair<string, List<string>>(executable, new List<string>(arguments));
        }

        static LunaPrediction Fail(LunaPrediction prediction, string error)
        {
            prediction.success = false;
            prediction.error = error;
            return prediction;
        }
    }
}
