using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    public class TextureEntry
    {
        public string AssetPath;
        public string FileName;
        public string Extension;
        public long FileBytes;
        public int Width;
        public int Height;
        public int ImportedWidth;
        public int ImportedHeight;
        public bool HasAlpha;
        public TextureImporterType TextureType;
        public TextureImporterCompression UnityCompression;
        public bool HasPlatformOverride;
        public TextureFormat ImportedFormat;
        public TextureImporterAlphaSource AlphaSource;
        public TextureImporterNPOTScale NpotScale;
        public bool IsSrgb;
        public LunaTextureRule LunaRule;
        public bool IsLunaOverride;
        public bool HasLastBuild;
        public double LastBuildBytes;
        public bool LastBuildOutdated;
        public LunaPrediction Prediction;
        public string SourceFileOverride;   // full path of a file Luna should read instead of the asset's own file
        public LunaTextureRule SavedRule;    // the rule in luna.json while LunaRule holds unsaved changes
        public double PendingFromBytes;      // predicted size before the unsaved changes, -1 when unknown

        // Everything except Default-type PNGs is re-encoded from Unity's imported texture,
        // so Unity's compression leaks into the Luna build.
        public bool UnityCompressionAffectsLuna
        {
            get { return !LunaInputExporter.UsesSourceFile(this) && UnityCompression != TextureImporterCompression.Uncompressed; }
        }

        public string LunaOutputFormat
        {
            get { return LunaPipeline.OutputExtension(LunaRule, ImportedFormat, AlphaSource).TrimStart('.'); }
        }

        public TextureEntry Clone()
        {
            return (TextureEntry)MemberwiseClone();
        }

        public bool HasPendingChange
        {
            get { return SavedRule != null; }
        }

        // Pixel size of the image Luna resizes: the source file, or Unity's import when Luna re-encodes it.
        public Vector2Int LunaInputSize
        {
            get { return LunaInputExporter.UsesSourceFile(this) ? new Vector2Int(Width, Height) : new Vector2Int(ImportedWidth, ImportedHeight); }
        }

        // Same as ImageMagick's "WxH>" geometry: shrink to fit the box, keep the aspect ratio, never enlarge.
        public Vector2Int OutputSizeFor(int maxWidth, int maxHeight)
        {
            var input = LunaInputSize;
            if (maxWidth <= 0 || maxHeight <= 0 || (input.x <= maxWidth && input.y <= maxHeight))
                return input;
            var scale = Mathf.Min((float)maxWidth / input.x, (float)maxHeight / input.y);
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(input.x * scale)), Mathf.Max(1, Mathf.RoundToInt(input.y * scale)));
        }

        public bool HasPrediction
        {
            get { return Prediction != null && Prediction.success; }
        }
    }
}
