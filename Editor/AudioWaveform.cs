using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Peak waveform of a fully decoded clip as a texture: one column per pixel, min/max of all channels mixed down.
    public static class AudioWaveform
    {
        public static Texture2D Render(AudioClip clip, int width, int height, Color color)
        {
            if (clip == null || clip.samples == 0 || width <= 0 || height <= 0)
                return null;
            var channels = Mathf.Max(1, clip.channels);
            var data = new float[clip.samples * channels];
            if (!clip.GetData(data, 0))
                return null;

            var pixels = new Color32[width * height];
            var background = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = background;
            var line = (Color32)color;
            var middle = height / 2f;
            var framesPerColumn = Mathf.Max(1, clip.samples / width);
            for (int x = 0; x < width; x++)
            {
                var start = x * framesPerColumn;
                if (start >= clip.samples)
                    break;
                var end = Mathf.Min(clip.samples, start + framesPerColumn);
                var min = 0f;
                var max = 0f;
                for (int frame = start; frame < end; frame++)
                {
                    var sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += data[frame * channels + c];
                    var value = sum / channels;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }
                var top = Mathf.Clamp(Mathf.RoundToInt(middle + max * middle), 0, height - 1);
                var bottom = Mathf.Clamp(Mathf.RoundToInt(middle + min * middle), 0, height - 1);
                for (int y = bottom; y <= top; y++)
                    pixels[y * width + x] = line;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
