using System.Text;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public class SoundEntry
    {
        const double BASE64_RATIO = 4.0 / 3.0;
        const string SAMPLE_ID = "00000";    // Luna's asset ids in this kind of project have 5 digits

        public string AssetPath;
        public string FileName;
        public string Extension;
        public long FileBytes;
        public int Channels;
        public int Frequency;
        public float LengthSeconds;
        public AudioClipLoadType LoadType;
        public bool ForceToMono;
        public LunaSoundRule LunaRule;
        public bool IsLunaOverride;
        public bool HasLastBuild;
        public double LastBuildBytes;
        public bool LastBuildOutdated;
        public LunaPrediction Prediction;
        public LunaSoundRule SavedRule;      // the rule in luna.json while LunaRule holds unsaved changes
        public double PendingFromBytes;      // predicted size before the unsaved changes, -1 when unknown

        // Luna copies .wav files as they are; anything else is decoded from Unity's imported clip,
        // so Unity's import settings (Force To Mono, compression) reach the build.
        public bool UsesSourceFile
        {
            get { return Extension == ".wav"; }
        }

        public bool HasPendingChange
        {
            get { return SavedRule != null; }
        }

        public bool HasPrediction
        {
            get { return Prediction != null && Prediction.success; }
        }

        public SoundFormat Format
        {
            get { return LunaSoundFormat.Detect(LunaRule); }
        }

        public bool HasTrim
        {
            get { return !string.IsNullOrEmpty(LunaRule.endTime) && LunaRule.endTime != LunaSoundPipeline.NO_TIME; }
        }

        // What the sound costs inside the playable when assets are inlined as base64 (most networks):
        // the mp3 plus the .json sidecar Luna writes next to it.
        public double PlayableBytes
        {
            get { return HasPrediction ? (Prediction.outputBytes + SidecarBytes) * BASE64_RATIO : -1; }
        }

        // Luna's stage2 sidecar: {"id":N,"path":"...","assetBundleId":-1,"data":["<clip name>"]}, plus "ext" when not mp3
        public int SidecarBytes
        {
            get
            {
                var json = "{\"id\":" + SAMPLE_ID + ",\"path\":\"" + AssetPath + "\",\"assetBundleId\":-1,\"data\":[\""
                    + System.IO.Path.GetFileNameWithoutExtension(AssetPath) + "\"]"
                    + (string.IsNullOrEmpty(LunaRule.ext) ? string.Empty : ",\"ext\":\"" + LunaRule.ext + "\"") + "}";
                return Encoding.UTF8.GetByteCount(json);
            }
        }

        public SoundEntry Clone()
        {
            return (SoundEntry)MemberwiseClone();
        }
    }
}
