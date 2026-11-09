using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public enum SoundFormat
    {
        Mp3,
        OggOpus,
        WebmOpus,
        Custom,     // a custom script this tool did not write: shown, never changed
    }

    // Opus output through Luna's custom sound script hook. Luna's ffmpeg has no Opus encoder, so the script calls
    // an ffmpeg with libopus installed on the build machine. Luna runs the script through a shell with %{input}
    // (its exported .wav) and %{output} replaced, and ships the file with the rule's ext; the game then loads
    // <id><ext> with decodeAudioData and has no fallback.
    public static class LunaSoundFormat
    {
        public const string OGG = ".ogg";
        public const string WEBM = ".webm";
        const string INPUT = "%{input}";
        const string OUTPUT = "%{output}";
        const string OPUS_ENCODER = "libopus";
        const string FFMPEG_PREF = "Supercent.LunaTextureOptimizer.OpusFfmpeg";
        static readonly string[] CANDIDATES = { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" };

        public static readonly int[] OPUS_BITRATES = { 16, 24, 32, 40, 48, 64, 96, 128 };
        public const int DEFAULT_OPUS_BITRATE = 32;

        public const string RISKS =
            "• iOS: Ogg Opus only decodes on iOS / Safari 18.4 and later. WebM Opus is reported to work from about iOS 17.4 "
            + "(possibly 15.4). Neither has been tested on a device for this tool.\n"
            + "• No fallback: if a phone cannot decode the file, that sound is silent in the ad.\n"
            + "• Android Chrome decodes both.\n"
            + "• Build machines: the rule calls the ffmpeg at the path below. Every machine that builds this project "
            + "needs an ffmpeg with libopus at that same path, or the Luna build fails.\n"
            + "• Trim: Luna skips its own trim for custom scripts, so the current trim is written into the script. "
            + "Changing the trim in the Luna window later does not update it; set the format again here.";

        static string cachedFfmpeg;
        static bool searched;

        public static string Extension(SoundFormat format)
        {
            switch (format)
            {
                case SoundFormat.OggOpus: return OGG;
                case SoundFormat.WebmOpus: return WEBM;
                default: return ".mp3";
            }
        }

        public static bool IsOpus(SoundFormat format)
        {
            return format == SoundFormat.OggOpus || format == SoundFormat.WebmOpus;
        }

        public static SoundFormat Detect(LunaSoundRule rule)
        {
            if (string.IsNullOrEmpty(rule.script))
                return SoundFormat.Mp3;
            if (rule.script.Contains(OPUS_ENCODER))
            {
                if (rule.ext == OGG)
                    return SoundFormat.OggOpus;
                if (rule.ext == WEBM)
                    return SoundFormat.WebmOpus;
            }
            return SoundFormat.Custom;
        }

        // An ffmpeg that can encode Opus: the one picked by the user, else the first common install path that has libopus.
        public static string FindOpusFfmpeg()
        {
            var picked = EditorPrefs.GetString(FFMPEG_PREF, string.Empty);
            if (picked.Length > 0 && File.Exists(picked))
                return picked;
            if (!searched)
            {
                searched = true;
                cachedFfmpeg = CANDIDATES.FirstOrDefault(HasOpusEncoder);
            }
            return cachedFfmpeg;
        }

        public static bool SetOpusFfmpeg(string path, out string error)
        {
            if (!HasOpusEncoder(path))
            {
                error = path + " is not an ffmpeg with the libopus encoder.";
                return false;
            }
            EditorPrefs.SetString(FFMPEG_PREF, path);
            error = null;
            return true;
        }

        static bool HasOpusEncoder(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            string output, error;
            return ProcessRunner.Run(path, new[] { "-hide_banner", "-encoders" }, out output, out error) && output.Contains(OPUS_ENCODER);
        }

        // The script stored in the Luna rule. Built from the rule so bitrate, channels and trim stay readable as rule fields.
        public static string BuildScript(string ffmpeg, SoundFormat format, LunaSoundRule rule)
        {
            var arguments = OpusArguments(format, rule, INPUT, OUTPUT);
            return ProcessRunner.Quote(ffmpeg) + " " + string.Join(" ", arguments);
        }

        // ffmpeg -y -i in -vn -map_metadata -1 -c:a libopus -b:a Nk [-ac C] [-ss start -t length] -f ogg|webm out
        // -vn: some .wav files carry cover art, which ffmpeg would otherwise try to encode as video (Ogg fails, WebM grows).
        // -map_metadata -1: tags from the source are not needed in the ad.
        public static List<string> OpusArguments(SoundFormat format, LunaSoundRule rule, string input, string output)
        {
            var arguments = new List<string> { "-y", "-i", input, "-vn", "-map_metadata", "-1", "-c:a", OPUS_ENCODER, "-b:a", rule.bitrate + "k" };
            if (rule.channels > 0)
                arguments.AddRange(new[] { "-ac", rule.channels.ToString() });
            if (!string.IsNullOrEmpty(rule.endTime) && rule.endTime != LunaSoundPipeline.NO_TIME)
            {
                var length = ((double)rule.lengthInSeconds).ToString("R", CultureInfo.InvariantCulture);
                arguments.AddRange(new[] { "-ss", rule.startTime, "-t", length });
            }
            arguments.AddRange(new[] { "-f", format == SoundFormat.OggOpus ? "ogg" : "webm", output });
            return arguments;
        }

        // Reads the executable back out of a stored script (quoted by BuildScript).
        public static string ScriptExecutable(string script)
        {
            if (string.IsNullOrEmpty(script) || script[0] != '"')
                return null;
            var end = script.IndexOf('"', 1);
            return end > 0 ? script.Substring(1, end - 1) : null;
        }

        // Sets format, ext and script on a rule. Switching to Opus starts at a typical Opus bitrate.
        public static void Apply(LunaSoundRule rule, SoundFormat format, string ffmpeg, int mp3Bitrate)
        {
            if (!IsOpus(format))
            {
                rule.script = string.Empty;
                rule.ext = string.Empty;
                rule.bitrate = mp3Bitrate;
                return;
            }
            if (!IsOpus(Detect(rule)))
                rule.bitrate = DEFAULT_OPUS_BITRATE;
            rule.ext = Extension(format);
            rule.script = BuildScript(ffmpeg, format, rule);
        }

        // Rebuilds the script after bitrate or channels changed on an Opus rule.
        public static void RefreshScript(LunaSoundRule rule)
        {
            var format = Detect(rule);
            if (!IsOpus(format))
                return;
            var ffmpeg = ScriptExecutable(rule.script) ?? FindOpusFfmpeg();
            if (ffmpeg != null)
                rule.script = BuildScript(ffmpeg, format, rule);
        }

        public static string Label(SoundFormat format)
        {
            switch (format)
            {
                case SoundFormat.OggOpus: return "Ogg Opus";
                case SoundFormat.WebmOpus: return "WebM Opus";
                case SoundFormat.Custom: return "Custom script";
                default: return "MP3";
            }
        }
    }
}
