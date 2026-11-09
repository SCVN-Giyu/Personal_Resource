using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Replaced by the Sounds tab of the Luna Optimizer window. Kept so the old menu item and saved window layouts
    // still work: opening it opens the hub on that tab instead.
    public class LunaSoundOptimizerWindow : EditorWindow
    {
        [MenuItem("Supercent/Luna Sound Optimizer")]
        static void Open()
        {
            LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Sounds);
        }

        void OnEnable()
        {
            EditorApplication.delayCall += () =>
            {
                LunaOptimizerWindow.OpenTab(LunaOptimizerWindow.Tab.Sounds);
                if (this != null)
                    Close();
            };
        }
    }
}
