using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Mirrors Luna's editor-side texture export: the image file Luna's pipeline starts from.
    public static class LunaInputExporter
    {
        // Formats Luna copies byte-for-byte; every other format is rendered to RGBA32 first.
        static readonly TextureFormat[] RAW_COPY_FORMATS =
        {
            TextureFormat.Alpha8, TextureFormat.ARGB4444, TextureFormat.RGB24, TextureFormat.RGBA32,
            TextureFormat.ARGB32, TextureFormat.RGB565, TextureFormat.R16, TextureFormat.RGBA4444,
            TextureFormat.BGRA32, TextureFormat.RHalf, TextureFormat.RGHalf, TextureFormat.RGBAHalf,
            TextureFormat.RFloat, TextureFormat.RGFloat, TextureFormat.RGBAFloat, TextureFormat.RGB9e5Float,
            TextureFormat.BC6H, TextureFormat.RG16, TextureFormat.R8,
        };

        // Luna copies the source file only for Default-type PNGs that Unity does not rescale to a power of two.
        public static bool UsesSourceFile(TextureEntry entry)
        {
            if (entry.Extension != ".png" || entry.TextureType != TextureImporterType.Default)
                return false;
            return entry.NpotScale == TextureImporterNPOTScale.None
                || Mathf.IsPowerOfTwo(entry.Width) || Mathf.IsPowerOfTwo(entry.Height);
        }

        public static void Export(TextureEntry entry, string projectRoot, string outputPath)
        {
            if (UsesSourceFile(entry))
            {
                File.Copy(entry.SourceFileOverride ?? Path.Combine(projectRoot, entry.AssetPath), outputPath, true);
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.AssetPath);
            var copy = System.Array.IndexOf(RAW_COPY_FORMATS, texture.format) >= 0 ? CopyRaw(texture) : RenderToRgba32(texture);
            try
            {
                var png = ImageConversion.EncodeArrayToPNG(copy.GetRawTextureData(), copy.graphicsFormat, (uint)copy.width, (uint)copy.height, 0);
                File.WriteAllBytes(outputPath, png);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        static Texture2D CopyRaw(Texture2D source)
        {
            var copy = new Texture2D(Mathf.Max(1, source.width), Mathf.Max(1, source.height), source.format, source.mipmapCount > 1);
            copy.LoadRawTextureData(source.GetRawTextureData());
            return copy;
        }

        static Texture2D RenderToRgba32(Texture2D source)
        {
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount > 1);
            copy.ReadPixels(new Rect(0f, 0f, temporary.width, temporary.height), 0, 0, true);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            return copy;
        }
    }
}
