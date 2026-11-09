using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Replaced by the Textures tab of the Luna Optimizer window. Kept so the old menu item and saved window layouts
    // still work: opening it opens the hub on that tab instead.
    public class LunaTextureOptimizerWindow : EditorWindow
    {
        [MenuItem("Supercent/Luna Texture Optimizer")]
        static void Open()
        {
            LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Textures);
        }

        void OnEnable()
        {
            EditorApplication.delayCall += () =>
            {
                LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Textures);
                if (this != null)
                    Close();
            };
        }
    }
}
