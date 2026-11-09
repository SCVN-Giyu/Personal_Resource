using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Luna's input and predicted output for one texture, side by side at the same on-screen size, so the output
    // looks the way it would when the game draws it. Follows the row's current (also unsaved) rule.
    public class TexturePreviewPanel
    {
        const float LABEL_HEIGHT = 18f;
        const float GAP = 6f;
        const int CHECKER_CELL = 8;
        static readonly string[] ZOOM_LABELS = { "Fit", "1:1", "2:1", "4:1" };
        static readonly float[] ZOOMS = { 0f, 1f, 2f, 4f };
        static readonly string[] BACKGROUND_LABELS = { "Checker", "Black", "White" };

        int zoomIndex;
        int backgroundIndex;
        Vector2 scroll;
        string shownKey;
        string pendingKey;
        string error;
        Texture2D input;
        Texture2D output;
        Texture2D checker;
        readonly Action repaint;

        public TexturePreviewPanel(Action repaint)
        {
            this.repaint = repaint;
        }

        public void Clear()
        {
            Release();
            shownKey = null;
            pendingKey = null;
            error = null;
        }

        // Draws the panel; renders the preview outside the GUI pass when the texture or its rule changed.
        public void Draw(Rect area, TextureEntry entry, LunaInstall install, string projectRoot)
        {
            var toolbar = new Rect(area.x, area.y, area.width, EditorGUIUtility.singleLineHeight + 2f);
            DrawToolbar(toolbar, entry);
            var body = new Rect(area.x, toolbar.yMax + 2f, area.width, area.height - toolbar.height - 2f);

            if (entry == null)
            {
                GUI.Label(body, "Click a texture name to preview it.", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (install == null)
            {
                GUI.Label(body, "No Luna install found, so Luna's output cannot be rendered.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var key = PredictionCache.KeyFor(entry, projectRoot, install);
            if (key != shownKey && key != pendingKey)
            {
                pendingKey = key;
                EditorApplication.delayCall += () => Render(entry, install, projectRoot, key);
            }
            if (key != shownKey)
            {
                GUI.Label(body, "Rendering Luna's output…", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (error != null)
            {
                EditorGUI.HelpBox(new Rect(body.x, body.y, body.width, 38f), "Preview failed: " + error, MessageType.Warning);
                return;
            }
            DrawImages(body, entry);
        }

        void DrawToolbar(Rect rect, TextureEntry entry)
        {
            var x = rect.x;
            GUI.Label(new Rect(x, rect.y, 40f, rect.height), "Zoom");
            x += 40f;
            zoomIndex = GUI.Toolbar(new Rect(x, rect.y, 160f, rect.height), zoomIndex, ZOOM_LABELS, EditorStyles.miniButton);
            x += 172f;
            GUI.Label(new Rect(x, rect.y, 75f, rect.height), "Background");
            x += 75f;
            backgroundIndex = GUI.Toolbar(new Rect(x, rect.y, 165f, rect.height), backgroundIndex, BACKGROUND_LABELS, EditorStyles.miniButton);
            x += 177f;
            if (entry != null)
                GUI.Label(new Rect(x, rect.y, rect.xMax - x, rect.height), entry.FileName, EditorStyles.boldLabel);
        }

        void DrawImages(Rect body, TextureEntry entry)
        {
            var paneWidth = (body.width - GAP) / 2f;
            var left = new Rect(body.x, body.y, paneWidth, body.height);
            var right = new Rect(left.xMax + GAP, body.y, paneWidth, body.height);

            var prediction = entry.Prediction;
            var outputLabel = "Luna output · " + (prediction != null && prediction.success ? prediction.outputExtension.TrimStart('.') : entry.LunaOutputFormat)
                + " · " + output.width + "×" + output.height
                + (prediction != null && prediction.success ? " · " + FormatBytes(prediction.outputBytes) + " (" + FormatBytes(prediction.PlayableBytes) + " in playable)" : string.Empty)
                + (entry.HasPendingChange ? " · unsaved" : string.Empty);
            var inputLabel = (prediction != null && !prediction.usedSourceFile ? "Luna input (Unity's import)" : "Source (Luna reads this file)")
                + " · " + input.width + "×" + input.height + " · " + FormatBytes(entry.FileBytes);
            GUI.Label(new Rect(left.x, left.y, left.width, LABEL_HEIGHT), new GUIContent(inputLabel, entry.AssetPath), EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(right.x, right.y, right.width, LABEL_HEIGHT), outputLabel, EditorStyles.miniBoldLabel);
            left.yMin += LABEL_HEIGHT;
            right.yMin += LABEL_HEIGHT;

            // Both images get the input's on-screen size: the output is stretched like the game stretches it.
            var zoom = ZOOMS[zoomIndex];
            if (zoom <= 0f)
                zoom = Mathf.Min(left.width / input.width, left.height / input.height);
            var shown = new Vector2(input.width * zoom, input.height * zoom);
            var filter = zoom >= 2f ? FilterMode.Point : FilterMode.Bilinear;
            input.filterMode = filter;
            output.filterMode = filter;

            // One scroll position for both panes, so they always show the same pixels.
            var view = new Rect(0f, 0f, Mathf.Max(shown.x, left.width - 14f), Mathf.Max(shown.y, left.height - 14f));
            GUI.BeginScrollView(left, scroll, view, GUIStyle.none, GUIStyle.none);
            DrawImage(new Rect(0f, 0f, shown.x, shown.y), input);
            GUI.EndScrollView();
            scroll = GUI.BeginScrollView(right, scroll, view);
            DrawImage(new Rect(0f, 0f, shown.x, shown.y), output);
            GUI.EndScrollView();
        }

        void DrawImage(Rect rect, Texture2D texture)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            switch (backgroundIndex)
            {
                case 0:
                    GUI.DrawTextureWithTexCoords(rect, Checker(), new Rect(0f, 0f, rect.width / (CHECKER_CELL * 2), rect.height / (CHECKER_CELL * 2)));
                    break;
                case 1:
                    EditorGUI.DrawRect(rect, Color.black);
                    break;
                default:
                    EditorGUI.DrawRect(rect, Color.white);
                    break;
            }
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        void Render(TextureEntry entry, LunaInstall install, string projectRoot, string key)
        {
            if (pendingKey != key)
                return;
            string inputPng;
            string outputPng;
            string renderError;
            Release();
            if (LunaPipeline.RenderPreview(entry, install, projectRoot, key, out inputPng, out outputPng, out renderError))
            {
                input = Load(inputPng);
                output = Load(outputPng);
                if (input == null || output == null)
                    renderError = "Unity could not read the rendered image.";
            }
            error = renderError;
            shownKey = key;
            pendingKey = null;
            if (error != null)
                Release();
            repaint();
        }

        static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            if (texture.LoadImage(File.ReadAllBytes(path)))
                return texture;
            UnityEngine.Object.DestroyImmediate(texture);
            return null;
        }

        void Release()
        {
            if (input != null)
                UnityEngine.Object.DestroyImmediate(input);
            if (output != null)
                UnityEngine.Object.DestroyImmediate(output);
            input = null;
            output = null;
        }

        Texture2D Checker()
        {
            if (checker != null)
                return checker;
            checker = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var light = new Color(0.8f, 0.8f, 0.8f);
            var dark = new Color(0.6f, 0.6f, 0.6f);
            checker.SetPixels(new[] { light, dark, dark, light });
            checker.Apply();
            return checker;
        }

        public void Dispose()
        {
            Release();
            if (checker != null)
                UnityEngine.Object.DestroyImmediate(checker);
            checker = null;
        }

        static string FormatBytes(double bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024 * 1024)).ToString("0.00") + " MB";
            if (bytes >= 1024)
                return (bytes / 1024).ToString("0.0") + " KB";
            return bytes.ToString("0") + " B";
        }
    }
}
