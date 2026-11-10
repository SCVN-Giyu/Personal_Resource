using System;
using System.Globalization;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Luna's sound trim: startTime / endTime as "MM:SS.mmm" and lengthInSeconds (end - start, stored as a float).
    // "00:00.000" for both means the whole clip. Luna passes them to ffmpeg as -ss start -t length.
    public static class SoundTrim
    {
        public const float MIN_LENGTH = 0.05f;
        const float WHOLE_CLIP_TOLERANCE = 0.0015f;
        const float SILENCE_LEVEL = 0.003f;   // about -50 dB
        const float EDGE_PADDING = 0.03f;     // kept around the sound so attacks and tails are not cut

        public static float Start(SoundEntry entry)
        {
            return entry.HasTrim ? Mathf.Clamp(Parse(entry.LunaRule.startTime), 0f, entry.LengthSeconds) : 0f;
        }

        public static float End(SoundEntry entry)
        {
            return entry.HasTrim ? Mathf.Clamp(Parse(entry.LunaRule.endTime), 0f, entry.LengthSeconds) : entry.LengthSeconds;
        }

        // Seconds Luna keeps of the clip.
        public static float Kept(SoundEntry entry)
        {
            return End(entry) - Start(entry);
        }

        // Clamped to the clip; the whole clip clears the trim.
        public static void Apply(LunaSoundRule rule, float start, float end, float clipLength)
        {
            start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clipLength - MIN_LENGTH));
            end = Mathf.Clamp(end, start + MIN_LENGTH, clipLength);
            if (start <= WHOLE_CLIP_TOLERANCE && end >= clipLength - WHOLE_CLIP_TOLERANCE)
            {
                rule.startTime = LunaSoundPipeline.NO_TIME;
                rule.endTime = LunaSoundPipeline.NO_TIME;
                rule.lengthInSeconds = 0f;
                return;
            }
            rule.startTime = Format(start);
            rule.endTime = Format(end);
            rule.lengthInSeconds = (float)Math.Round(Parse(rule.endTime) - Parse(rule.startTime), 3);
        }

        public static string Format(float seconds)
        {
            var ms = (long)Math.Round(Math.Max(0.0, seconds) * 1000.0);
            var minutes = ms / 60000;
            var rest = ms % 60000;
            return minutes.ToString("00") + ":" + (rest / 1000).ToString("00") + "." + (rest % 1000).ToString("000");
        }

        // "MM:SS.mmm" (also "SS.mmm" or "HH:MM:SS.mmm"); 0 when empty or unreadable.
        public static float Parse(string time)
        {
            if (string.IsNullOrEmpty(time))
                return 0f;
            double total = 0;
            foreach (var part in time.Split(':'))
            {
                double value;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return 0f;
                total = total * 60 + value;
            }
            return (float)total;
        }

        // Where the sound starts and ends once leading and trailing silence is left out (with a little padding).
        // False for a silent or unreadable clip.
        public static bool FindSound(AudioClip clip, out float start, out float end)
        {
            start = 0f;
            end = 0f;
            if (clip == null || clip.samples == 0)
                return false;
            var channels = Mathf.Max(1, clip.channels);
            var data = new float[clip.samples * channels];
            if (!clip.GetData(data, 0))
                return false;
            int first = -1;
            int last = -1;
            for (int frame = 0; frame < clip.samples; frame++)
            {
                if (IsLoud(data, frame, channels))
                {
                    first = frame;
                    break;
                }
            }
            if (first < 0)
                return false;
            for (int frame = clip.samples - 1; frame >= first; frame--)
            {
                if (IsLoud(data, frame, channels))
                {
                    last = frame;
                    break;
                }
            }
            var frequency = (float)clip.frequency;
            start = Mathf.Max(0f, first / frequency - EDGE_PADDING);
            end = Mathf.Min(clip.length, (last + 1) / frequency + EDGE_PADDING);
            return end - start >= MIN_LENGTH;
        }

        static bool IsLoud(float[] data, int frame, int channels)
        {
            for (int c = 0; c < channels; c++)
            {
                if (Mathf.Abs(data[frame * channels + c]) > SILENCE_LEVEL)
                    return true;
            }
            return false;
        }
    }
}
