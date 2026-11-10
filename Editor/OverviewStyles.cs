using UnityEditor;
using UnityEngine;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // IMGUI look of the Overview tab, matching the UI Toolkit tabs: cards, a hero total with a meter, section
    // titles, muted notes and rounded bars. Styles are rebuilt after a domain reload (their textures are lost).
    public static class OverviewStyles
    {
        public const float ROW_HEIGHT = 20f;
        static readonly Color ACCENT = new Color(0.345f, 0.592f, 0.851f);
        static readonly Color GOOD = new Color(0.3f, 0.7f, 0.35f);
        static readonly Color WARN = new Color(0.95f, 0.65f, 0.1f);
        static readonly Color OVER = new Color(0.85f, 0.25f, 0.2f);
        const float WARN_RATIO = 0.85f;

        static GUIStyle card;
        static Texture2D cardTexture;
        public static GUIStyle HeroValue { get; private set; }
        public static GUIStyle HeroCaption { get; private set; }
        public static GUIStyle SectionTitle { get; private set; }
        public static GUIStyle Muted { get; private set; }
        public static GUIStyle MutedRight { get; private set; }
        public static GUIStyle RowLabel { get; private set; }
        public static GUIStyle SizeBold { get; private set; }
        public static GUIStyle PrimaryToolbarButton { get; private set; }

        // Tint for the tab's main action (multiplies the toolbar button's own background).
        public static Color AccentButton
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0.45f, 0.75f, 1.6f) : new Color(0.55f, 0.8f, 1.4f); }
        }

        public static Color Hover
        {
            get { return EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.06f); }
        }

        static Color Track
        {
            get { return new Color(0.5f, 0.5f, 0.5f, 0.22f); }
        }

        public static void Ensure()
        {
            if (card != null && cardTexture != null)
                return;
            cardTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            cardTexture.SetPixel(0, 0, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.035f) : new Color(1f, 1f, 1f, 0.45f));
            cardTexture.Apply();
            card = new GUIStyle
            {
                normal = { background = cardTexture },
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(8, 8, 8, 0),
            };
            HeroValue = new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, fixedHeight = 32f, alignment = TextAnchor.LowerLeft };
            HeroCaption = new GUIStyle(EditorStyles.label) { fontSize = 13, fixedHeight = 28f, alignment = TextAnchor.LowerLeft, normal = { textColor = MutedColor } };
            SectionTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            Muted = new GUIStyle(EditorStyles.label) { normal = { textColor = MutedColor }, wordWrap = false };
            MutedRight = new GUIStyle(Muted) { alignment = TextAnchor.MiddleRight };
            RowLabel = new GUIStyle(EditorStyles.label);
            SizeBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
            PrimaryToolbarButton = new GUIStyle(EditorStyles.toolbarButton) { fontStyle = FontStyle.Bold };
        }

        static Color MutedColor
        {
            get { return EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f); }
        }

        public static void BeginCard()
        {
            Ensure();
            EditorGUILayout.BeginVertical(card);
        }

        public static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        // "2.65 MB  of 5.00 MB · ironsource"
        public static void Hero(string value, string caption)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(value, HeroValue, GUILayout.ExpandWidth(false));
            GUILayout.Label(caption, HeroCaption);
            EditorGUILayout.EndHorizontal();
        }

        // Section title with a muted note on the same line.
        public static void Section(string title, string note)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, SectionTitle, GUILayout.ExpandWidth(false));
            if (!string.IsNullOrEmpty(note))
                GUILayout.Label(note, Muted);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        // Budget meter: green, orange near the cap, red over it.
        public static void Meter(float ratio, float height)
        {
            var rect = GUILayoutUtility.GetRect(0f, height * 0.6f, GUILayout.ExpandWidth(true));
            rect.y += 4f;
            EditorGUI.DrawRect(rect, Track);
            var color = ratio > 1f ? OVER : ratio > WARN_RATIO ? WARN : GOOD;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), color);
            GUILayout.Space(6f);
        }

        // Share of the whole as a slim bar.
        public static void Bar(Rect rect, float share)
        {
            EditorGUI.DrawRect(rect, Track);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(share), rect.height), ACCENT);
        }
    }
}
