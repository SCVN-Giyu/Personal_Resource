using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Runs Luna's sound step locally: the same .wav input Luna's editor exports, then Luna's own ffmpeg command
    // (mp3: matches a real build byte for byte) or the rule's Opus script. The output is kept so it can be listened to.
    public static class LunaSoundPipeline
    {
        public const string NO_TIME = "00:00.000";
        const string WORK_FOLDER = "Library/LunaTextureOptimizer/PredictSound";
        const string PREVIEW_FOLDER = "Library/LunaTextureOptimizer/SoundPreview";
        const string MP3 = ".mp3";
        const string WAV = ".wav";
        const float PCM_SCALE = 32767f;

        class Job
        {
            public LunaPrediction Prediction = new LunaPrediction();
            public string ContentKey;
            public string OutputPath;
            public string PreviewPath;
            public string Executable;            // null: Luna's ffmpeg
            public string OutputExtension;
            public List<string> Arguments;
        }

        // Where the playable preview of an entry's current rule is kept (keyed like the prediction cache):
        // the mp3 itself, or the Opus output decoded to .wav because Unity cannot play Opus.
        public static string PreviewPathFor(SoundEntry entry, string projectRoot, LunaInstall install)
        {
            using (var sha = SHA1.Create())
            {
                var key = PredictionCache.KeyFor(entry, projectRoot, install);
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", string.Empty);
                var extension = LunaSoundFormat.IsOpus(LunaSoundFormat.Detect(entry.LunaRule)) ? WAV : MP3;
                return Path.Combine(projectRoot, PREVIEW_FOLDER, hash + extension);
            }
        }

        // Exports every input on the main thread, then runs ffmpeg for all sounds in parallel. Results in the cache
        // under the same input samples and command are reused. Canceled sounds come back as null.
        public static LunaPrediction[] PredictMany(IList<SoundEntry> entries, LunaInstall install, string projectRoot,
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
                    if (cached != null && cached.success && File.Exists(job.PreviewPath))
                        results[i] = CopyOf(cached, job.Prediction);
                    else if (job.Arguments == null)
                        results[i] = job.Prediction;
                    else
                        jobs.Add(new KeyValuePair<int, Job>(i, job));
                }

                var ffmpeg = install.FfmpegPath;
                ParallelWork.Run(jobs.Select(j => j.Value).ToList(), job => Run(job, job.Executable ?? ffmpeg), progressTitle, "Running ffmpeg");
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

        static Job Prepare(SoundEntry entry, LunaInstall install, string projectRoot, string workPath)
        {
            var job = new Job();
            try
            {
                Directory.CreateDirectory(workPath);
                var inputPath = Path.Combine(workPath, "input.wav");
                ExportInput(entry, projectRoot, inputPath);
                job.Prediction.usedSourceFile = entry.UsesSourceFile;
                job.Prediction.inputBytes = new FileInfo(inputPath).Length;
                var format = LunaSoundFormat.Detect(entry.LunaRule);
                if (format == SoundFormat.Custom)
                    throw new InvalidOperationException("This sound uses a custom Luna script this tool did not write, so it cannot be predicted.");
                job.OutputExtension = LunaSoundFormat.Extension(format);
                job.OutputPath = Path.Combine(workPath, "output" + job.OutputExtension);
                job.PreviewPath = PreviewPathFor(entry, projectRoot, install);
                if (LunaSoundFormat.IsOpus(format))
                {
                    job.Executable = LunaSoundFormat.ScriptExecutable(entry.LunaRule.script);
                    if (job.Executable == null || !File.Exists(job.Executable))
                        throw new InvalidOperationException("The Opus script's ffmpeg is not on this machine: " + job.Executable);
                    job.Arguments = LunaSoundFormat.OpusArguments(format, entry.LunaRule, inputPath, job.OutputPath);
                }
                else
                {
                    job.Arguments = FfmpegArguments(entry.LunaRule, inputPath, job.OutputPath);
                }
                job.ContentKey = ContentKey(inputPath, workPath, install, job.Executable, job.Arguments);
            }
            catch (Exception exception)
            {
                job.Arguments = null;
                Fail(job.Prediction, exception.Message);
            }
            return job;
        }

        // Luna's editor copies .wav files unchanged; other clips are written as 16-bit PCM from Unity's decoded
        // samples, truncated with (short)(sample * 32767). Verified byte-identical with Luna's stage1 files.
        public static void ExportInput(SoundEntry entry, string projectRoot, string outputPath)
        {
            if (entry.UsesSourceFile)
            {
                File.Copy(Path.Combine(projectRoot, entry.AssetPath), outputPath, true);
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entry.AssetPath);
            if (clip == null)
                throw new InvalidOperationException("Unity could not load the audio clip.");
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
                throw new InvalidOperationException("Unity cannot read this clip's samples (Load Type " + entry.LoadType
                    + "). Set Load Type to Decompress On Load.");

            using (var writer = new BinaryWriter(File.Create(outputPath)))
            {
                var dataBytes = samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2);
                writer.Write((short)(clip.channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                foreach (var sample in samples)
                    writer.Write((short)(sample * PCM_SCALE));
            }
        }

        // Same command as Luna's compressSound step:
        // ffmpeg -y -i in -acodec mp3 (-b:a Nk | -q:a 8) [-ac C] [-ss start -t length] out
        static List<string> FfmpegArguments(LunaSoundRule rule, string inputPath, string outputPath)
        {
            var arguments = new List<string> { "-y", "-i", inputPath, "-acodec", "mp3" };
            if (rule.bitrate > 0)
                arguments.AddRange(new[] { "-b:a", rule.bitrate + "k" });
            else
                arguments.AddRange(new[] { "-q:a", "8" });
            if (rule.channels > 0)
                arguments.AddRange(new[] { "-ac", rule.channels.ToString() });
            if (!string.IsNullOrEmpty(rule.endTime) && rule.endTime != NO_TIME)
            {
                // Luna passes the length as JavaScript prints the float it read from luna.json.
                var length = ((double)rule.lengthInSeconds).ToString("R", CultureInfo.InvariantCulture);
                arguments.AddRange(new[] { "-ss", rule.startTime, "-t", length });
            }
            arguments.Add(outputPath);
            return arguments;
        }

        // Hash of the exact input file plus the command with its work folder blanked out.
        static string ContentKey(string inputPath, string workPath, LunaInstall install, string executable, List<string> arguments)
        {
            using (var sha = SHA1.Create())
            {
                var builder = new StringBuilder("sound|").Append(install.Version).Append('|').Append(executable).Append('|');
                builder.Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(inputPath))).Replace("-", string.Empty));
                foreach (var argument in arguments)
                    builder.Append(' ').Append(argument.Replace(workPath, "{work}"));
                return builder.ToString();
            }
        }

        // Worker thread: no Unity API in here.
        static void Run(Job job, string ffmpeg)
        {
            try
            {
                string error;
                if (!ProcessRunner.Run(ffmpeg, job.Arguments, out error))
                {
                    Fail(job.Prediction, "ffmpeg " + error);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(job.PreviewPath));
                if (job.OutputExtension == MP3)
                    File.Copy(job.OutputPath, job.PreviewPath, true);
                else if (!ProcessRunner.Run(ffmpeg, new[] { "-y", "-i", job.OutputPath, job.PreviewPath }, out error))
                {
                    Fail(job.Prediction, "decoding for preview: ffmpeg " + error);
                    return;
                }
                job.Prediction.outputExtension = job.OutputExtension;
                job.Prediction.outputBytes = new FileInfo(job.OutputPath).Length;
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

        static void Fail(LunaPrediction prediction, string error)
        {
            prediction.success = false;
            prediction.error = error;
        }
    }
}
